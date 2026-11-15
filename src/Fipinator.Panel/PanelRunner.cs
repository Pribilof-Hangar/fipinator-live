// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Collections.Concurrent;
using Fipinator.Core;
using Fipinator.Fip;
using Fipinator.Sim;
using SkiaSharp;

namespace Fipinator.Panel;

/// <summary>
/// Runs a whole panel: every FIP in the settings shows its gauge, driven by one SimConnect
/// connection. Each FIP has its own worker thread and only redraws when its values change.
/// Copes with the sim not running yet (gauges rest at zero with a "NO SIM DATA" banner), page
/// switches, skipped frames, and FIPs being unplugged and plugged back in.
/// </summary>
public sealed class PanelRunner(PanelSettings settings, string settingsFolder)
{
    private readonly Lock _openLock = new();
    private readonly ConcurrentDictionary<string, string> _fipStatus = new(StringComparer.OrdinalIgnoreCase);
    private SimConnection? _sim;

    /// <summary>Plain-language messages for the user (sim connected, FIP unplugged, ...).</summary>
    public event Action<string>? Report;

    /// <summary>Current sim state and message, for a status display.</summary>
    public (SimState State, string Message) SimStatus =>
        _sim is { } sim ? (sim.State, sim.StatusMessage) : (SimState.Waiting, "Starting...");

    /// <summary>Each FIP's serial, gauge name and current state, for a status display.</summary>
    public IReadOnlyList<(string Serial, string Gauge, string State)> FipStatus =>
        [.. settings.Fips.Select(f => (f.Serial, _gaugeNames.GetValueOrDefault(f.Serial, f.Gauge), _fipStatus.GetValueOrDefault(f.Serial, "Starting...")))];

    private readonly ConcurrentDictionary<string, string> _gaugeNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _testShown = new(StringComparer.OrdinalIgnoreCase);
    private long _testUntilTicks;
    private DirectOutput? _directOutput;

    /// <summary>
    /// Test: for a few seconds every FIP shows its short name instead of its gauge, so the
    /// list can be matched to the physical panels and you can see which FIPs respond.
    /// Works with or without the sim.
    /// </summary>
    public void StartTest(TimeSpan duration)
    {
        _testShown.Clear();
        Interlocked.Exchange(ref _testUntilTicks, DateTime.UtcNow.Add(duration).Ticks);
        Log.Info("Test: showing FIP names.");
    }

    /// <summary>True while the test screen is showing.</summary>
    public bool TestRunning => DateTime.UtcNow.Ticks < Interlocked.Read(ref _testUntilTicks);

    private readonly ConcurrentDictionary<string, DateTime> _highlightUntil = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Shows just this FIP's short name (the test screen) for a moment, so a picture in the app can be
    /// matched to the real FIP. The other FIPs keep showing their gauges.
    /// </summary>
    public void Highlight(string serial, TimeSpan duration)
    {
        _highlightUntil[serial] = DateTime.UtcNow.Add(duration);
        Log.Info($"FIP {serial}: showing its name.");
    }

    private bool Highlighted(string serial) => _highlightUntil.TryGetValue(serial, out var until) && DateTime.UtcNow < until;

    /// <summary>FIPs in this panel that showed the test screen since the last <see cref="StartTest"/>.</summary>
    public IReadOnlyCollection<string> TestAnswered => [.. _testShown.Keys];

    /// <summary>Which page (1, 2, ...) a FIP should start on; null or out of range means the first.</summary>
    public Func<string, int>? StartPageFor { get; set; }

    /// <summary>A FIP's page was flipped to (serial, page 1..n). Raised on a FIP worker thread.</summary>
    public event Action<string, int>? PageShown;

    /// <summary>Serial numbers of all FIPs Windows/DirectOutput can see right now.</summary>
    public IReadOnlyList<string> ConnectedSerials =>
        _directOutput is { } d ? [.. d.Devices.Select(x => x.SerialNumber)] : [];

    /// <summary>
    /// Runs until <paramref name="stop"/> is cancelled.
    /// </summary>
    /// <exception cref="FipException">The Logitech software or service isn't available.</exception>
    /// <exception cref="InvalidDataException">A gauge file has mistakes.</exception>
    public void Run(CancellationToken stop)
    {
        Log.Tidy();
        Log.Info($"Panel starting with {settings.Fips.Count} FIP(s).");

        // Load every gauge (every page) first, so mistakes show up before anything touches the FIPs.
        // Every layout's gauges too (owner, 2026-11-14: whole layouts that switch together on a FIP button).
        var layouts = settings.AllLayouts();
        var gauges = new List<(FipAssignment Fip, IReadOnlyList<IReadOnlyList<string>> Paths, List<GaugeDefinition> Pages)>();
        foreach (var fip in settings.Fips)
        {
            var paths = Enumerable.Range(0, layouts.Count).Select(l => PanelSettings.PagePaths(settings.FipIn(l, fip.Serial), settingsFolder)).ToList();
            var pages = paths.SelectMany(p => p).Select(GaugeDefinition.Load).ToList();
            gauges.Add((fip, paths, pages));
            _gaugeNames[fip.Serial] = pages[0].Name + (paths[0].Count > 1 ? $" (+{paths[0].Count - 1} pages)" : "")
                + (layouts.Count > 1 ? $" ({layouts.Count} layouts)" : "");
        }
        _layoutNames = [.. layouts.Select(l => l.Name)];
        _layoutButton = layouts.Count > 1 ? settings.LayoutButton : null;
        Volatile.Write(ref _layout, 0);
        if (layouts.Count > 1)
            Log.Info($"Layouts: {string.Join(", ", _layoutNames)}; " + (_layoutButton is { } lb ? $"{lb.Button} on FIP {lb.Serial} switches them." : "no button set to switch them."));

        // One sim connection for all gauges; each distinct (variable, unit) is requested once.
        var variables = gauges.SelectMany(g => g.Pages).SelectMany(g => g.SimValues())
            .Select(v => new SimVariable(v.Sim, v.Unit))
            .Distinct(new VariableComparer())
            .ToList();

        using var directOutput = new DirectOutput();
        _directOutput = directOutput;
        using var sim = new SimConnection(variables);
        _sim = sim;
        sim.StatusChanged += (_, message) => { Report?.Invoke(message); Log.Info($"Sim: {message} {sim.SimName}"); };
        sim.Warning += message => { Report?.Invoke($"Warning: {message}"); Log.Warn($"Sim: {message}"); };
        sim.Start();
        using var recorder = new SwitchRecorder(); // read-only: logs electrical/avionics switch changes

        directOutput.DeviceAdded += d => { Report?.Invoke($"FIP {d.SerialNumber} plugged in."); Log.Info($"FIP added: {d.SerialNumber}"); };
        directOutput.DeviceRemoved += d => { Report?.Invoke($"FIP {d.SerialNumber} unplugged."); Log.Warn($"FIP removed: {d.SerialNumber}"); };

        var workers = gauges.Select(g => new Thread(() => RunFip(g.Fip.Serial, g.Paths, g.Fip.LeftKnobPages, g.Fip.AutoFlipSeconds, directOutput, sim, variables, stop))
        {
            IsBackground = true,
            Name = $"FIP {g.Fip.Serial}",
        }).ToList();
        workers.ForEach(w => w.Start());
        // Once a minute: note in the log anything that could make the FIPs hitch (only when something happened).
        var gcBefore = GC.GetTotalPauseDuration();
        while (!stop.WaitHandle.WaitOne(TimeSpan.FromMinutes(1)))
        {
            var gcNow = GC.GetTotalPauseDuration();
            var gcMs = (gcNow - gcBefore).TotalMilliseconds;
            gcBefore = gcNow;
            var (pauses, longest) = sim.TakePauses();
            var slow = _slowSends.ToArray().Where(kv => kv.Value.Count > 0).Select(kv => $"{kv.Key} {kv.Value.Count}x (longest {kv.Value.LongestMs:0} ms)").ToList();
            _slowSends.Clear();
            if (pauses > 0 || slow.Count > 0 || gcMs > 50)
                Log.Info("Timing, last minute: " + (pauses > 0 ? $"sim values paused {pauses}x over 150 ms (longest {longest:0} ms)" : "no sim pauses")
                    + "; " + (slow.Count > 0 ? "slow sends to FIPs over 80 ms: " + string.Join(", ", slow) : "no slow FIP sends")
                    + $"; FIPinator's own memory clean-up paused it {gcMs:0} ms in all.");
            // How many pictures each FIP got and how long a normal one took (sends under 1 s), to tune MaxSendsAtOnce.
            var sends = _sendStats.ToArray().Where(kv => kv.Value.Count > 0).OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key} {kv.Value.Count} (avg {kv.Value.TotalMs / kv.Value.Count:0.0} ms, {(_sameSkips.TryGetValue(kv.Key, out var same) ? same : 0)} same skipped)").ToList();
            _sendStats.Clear();
            _sameSkips.Clear();
            if (sends.Count > 0) Log.Info($"Sends, last minute ({MaxSendsAtOnce} at once): " + string.Join(", ", sends) + ".");
        }
        workers.ForEach(w => w.Join(TimeSpan.FromSeconds(5)));
        _sim = null;
        _directOutput = null;
        Log.Info("Panel stopped.");
    }

    /// <summary>The layout every FIP shows now (0 = main); the layout button moves it on.</summary>
    private int _layout;
    private IReadOnlyList<string> _layoutNames = [];
    private LayoutButton? _layoutButton;

    private void RunFip(string serial, IReadOnlyList<IReadOnlyList<string>> layoutPaths, bool leftKnobPages, int autoFlipSeconds, DirectOutput directOutput, SimConnection sim, List<SimVariable> variables, CancellationToken stop)
    {
        var renderers = new List<GaugeRenderer>();
        // Knob paging: the FIP gets one DirectOutput page and we pick which gauge it shows (0-based),
        // since DirectOutput can't switch pages for us. Otherwise every gauge is its own FIP page.
        var knobPaging = false; // one DirectOutput page, we pick the gauge (left knob and/or auto-flip)
        var knobFlips = false;  // the left knob flips the gauges
        var selected = 0;
        try
        {
            // All layouts' gauges in one list; layout l is starts[l] .. starts[l] + counts[l] - 1.
            var starts = new int[layoutPaths.Count];
            var counts = new int[layoutPaths.Count];
            for (var l = 0; l < layoutPaths.Count; l++)
            {
                starts[l] = renderers.Count;
                counts[l] = layoutPaths[l].Count;
                foreach (var path in layoutPaths[l]) renderers.Add(GaugeRenderer.FromFile(path));
            }
            var layoutCount = layoutPaths.Count;
            var mine = new int[1]; // the layout this FIP shows (read by the button handler on another thread)
            var layoutBit = _layoutButton is { } lb && lb.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase)
                ? ControlBits.First(c => c.Name == lb.Button).Bit : FipButtons.None;
            var anyPages = counts.Any(c => c > 1);
            // With layouts, Live always picks the gauge itself (one DirectOutput page), like knob paging.
            knobPaging = layoutCount > 1 || ((leftKnobPages || autoFlipSeconds > 0) && counts[0] > 1);
            knobFlips = leftKnobPages && anyPages;
            var flip = new PageFlipTimer(anyPages ? autoFlipSeconds : 0, DateTime.UtcNow);
            var comparer = new VariableComparer();
            var pageValues = renderers.Select(r => r.Gauge.SimValues()).ToList();
            var pageIndexes = pageValues.Select(list => list.Select(v => variables.FindIndex(x => comparer.Equals(x, new SimVariable(v.Sim, v.Unit)))).ToArray()).ToList();

            var buffer = new byte[FipImage.BufferSize];
            var onScreen = new byte[FipImage.BufferSize]; // the last picture this FIP took, to skip sending the same one again
            var onScreenKnown = false;
            var values = new Dictionary<string, double>();
            FipDevice? device = null;
            double[]? lastShown = null;
            var lastNoSim = false;
            var wasTesting = false;
            var redraw = true;
            var frames = 0L;
            var skipped = 0L;
            var sinceSend = new System.Diagnostics.Stopwatch(); // time since this FIP's last gauge picture
            var shownPage = 0;
            var bannerUntil = DateTime.MinValue;
            var bannerShown = false;
            var layoutBannerUntil = DateTime.MinValue;
            // Knob modes: the chosen mode (1, 2, ...) for each gauge on this FIP; S buttons change it.
            var modes = new int[renderers.Count];
            Array.Fill(modes, 1);
            // S-button lights last sent (bit 0 = S1), and for which gauge; -1 = not sent yet.
            var lightsSent = -1;
            var lightsGauge = 0;

            while (!stop.IsCancellationRequested)
            {
                // Find this FIP; it may have been unplugged and plugged back in (a new device object).
                var current = directOutput.Devices.FirstOrDefault(d => d.SerialNumber.Equals(serial, StringComparison.OrdinalIgnoreCase));
                if (current is null || current.IsRemoved)
                {
                    device = null;
                    _fipStatus[serial] = "Not connected";
                    stop.WaitHandle.WaitOne(1000);
                    continue;
                }
                if (!ReferenceEquals(current, device))
                {
                    var start = StartPageFor?.Invoke(serial) ?? 1;
                    if (knobPaging)
                    {
                        Volatile.Write(ref selected, Math.Clamp(start, 1, counts[0]) - 1);
                        Volatile.Write(ref mine[0], 0);
                        lock (_openLock) current.OpenPages(1, 1);
                    }
                    else
                        lock (_openLock) current.OpenPages(renderers.Count, start); // one at a time: opening several at once has failed
                    var fip = current;
                    fip.PageChanged += (_, page, active) =>
                    {
                        if (!active) return;
                        redraw = true;
                        if (renderers.Count > 1) bannerUntil = DateTime.UtcNow.AddSeconds(2);
                        PageShown?.Invoke(serial, page);
                    };
                    var pressed = FipButtons.None;
                    fip.ButtonsChanged += (_, buttons) =>
                    {
                        // Knobs pulse their bit once per click; buttons hold theirs while pressed.
                        // Act on bits that just turned on, for the gauge on the page that's showing.
                        var down = buttons & ~pressed;
                        pressed = buttons;
                        var page = fip.ActivePage;
                        if (page < 1 || page > (knobPaging ? 1 : renderers.Count)) return;
                        var layoutNow = Volatile.Read(ref mine[0]);
                        if (layoutBit != FipButtons.None && (down & layoutBit) != 0)
                        {
                            // This FIP's layout button: every FIP moves on to the next layout together.
                            var nextLayout = (Volatile.Read(ref _layout) + 1) % layoutCount;
                            Volatile.Write(ref _layout, nextLayout);
                            Log.Info($"FIP {serial}: layout button -> layout {nextLayout + 1} of {layoutCount} ({_layoutNames[nextLayout]})");
                            down &= ~layoutBit;
                        }
                        var inLayout = counts[layoutNow];
                        if (knobFlips && inLayout > 1)
                        {
                            var step = ((down & FipButtons.LeftKnobClockwise) != 0 ? 1 : 0) - ((down & FipButtons.LeftKnobAnticlockwise) != 0 ? 1 : 0);
                            if (step != 0)
                            {
                                var next = (Volatile.Read(ref selected) + step + inLayout) % inLayout;
                                Volatile.Write(ref selected, next);
                                redraw = true;
                                bannerUntil = DateTime.UtcNow.AddSeconds(2);
                                PageShown?.Invoke(serial, next + 1);
                                flip.KnobTurned(DateTime.UtcNow); // keep the picked gauge up for a while
                            }
                        }
                        var gaugeIndex = knobPaging ? starts[layoutNow] + Math.Min(Volatile.Read(ref selected), inLayout - 1) : page - 1;
                        var gauge = renderers[gaugeIndex].Gauge;
                        foreach (var (bit, name) in ControlBits)
                        {
                            if (knobFlips && name.StartsWith("leftKnob", StringComparison.Ordinal)) continue; // the left knob flips pages here
                            if ((down & bit) == 0) continue;
                            // An S button that picks a knob mode just switches the mode.
                            var picked = gauge.Modes.FindIndex(m => m.Button == name);
                            if (picked >= 0)
                            {
                                Volatile.Write(ref modes[gaugeIndex], picked + 1);
                                redraw = true;
                                Log.Info($"FIP {serial}: knobs now set {gauge.Modes[picked].Name}");
                                continue;
                            }
                            var mode = gauge.Modes.Count > 0 ? gauge.Modes[Math.Clamp(Volatile.Read(ref modes[gaugeIndex]), 1, gauge.Modes.Count) - 1] : null;
                            if (!(mode?.Controls.TryGetValue(name, out var control) ?? false) && !gauge.Controls.TryGetValue(name, out control)) continue;
                            var connected = sim.State == SimState.Connected;
                            if (control.ChangesValue) foreach (var value in control.SetNames) sim.AddToValue(value, control.Add * control.Repeat);
                            else for (var r = 0; r < control.Repeat; r++) sim.SendEvent(control.Event, control.Data);
                            Log.Info($"FIP {serial}: {name} -> " + (control.ChangesValue ? $"{control.Set} {(control.Add * control.Repeat >= 0 ? "+" : "")}{control.Add * control.Repeat}" : control.Event)
                                + (!control.ChangesValue && control.Repeat > 1 ? $" x{control.Repeat}" : "")
                                + (connected ? "" : " (not sent: the sim isn't running)"));
                        }
                    };
                    device = current;
                    redraw = true;
                    shownPage = 0;
                    _fipStatus[serial] = "Showing";
                    Log.Info($"FIP {serial}: showing {renderers[0].Gauge.Name}" + (counts[0] > 1 ? $" and {counts[0] - 1} more page(s), starting on page {(knobPaging ? Volatile.Read(ref selected) + 1 : current.ActivePage)}" : "")
                        + (layoutCount > 1 ? $" ({layoutCount} layouts" + (layoutBit != FipButtons.None ? ", its " + _layoutButton!.Button + " switches them)" : ")") : "")
                        + (knobFlips ? " (left knob flips pages)" : "") + (flip.On ? $" (flips {PageFlipTimer.Describe(flip.Seconds)})" : ""));
                }

                // Which of our pages is up (0: another program's page, or the FIP's own).
                var pageUp = device.ActivePage;
                if (pageUp < 1 || pageUp > (knobPaging ? 1 : renderers.Count))
                {
                    _fipStatus[serial] = "Another page is showing";
                    stop.WaitHandle.WaitOne(50);
                    continue;
                }
                // Auto-flip: next gauge when it's time (not during a test screen). Its name shows only briefly,
                // and the page isn't remembered each time (that would write the settings every few seconds).
                // Layouts: all FIPs follow the shared layout; on a switch, start at that layout's first gauge.
                var layout = Volatile.Read(ref _layout) % layoutCount;
                if (layout != Volatile.Read(ref mine[0]))
                {
                    Volatile.Write(ref mine[0], layout);
                    Volatile.Write(ref selected, 0);
                    layoutBannerUntil = DateTime.UtcNow.AddSeconds(2);
                    redraw = true;
                }
                var pagesHere = counts[layout];
                if (pagesHere > 1 && flip.Due(DateTime.UtcNow) && !(TestRunning || Highlighted(serial)))
                {
                    Volatile.Write(ref selected, (Volatile.Read(ref selected) + 1) % pagesHere);
                    bannerUntil = DateTime.UtcNow.AddSeconds(1);
                }
                // Which gauge to draw: the knob's choice (within this layout), or the FIP page that's up.
                var gaugeNumber = knobPaging ? Math.Min(Volatile.Read(ref selected), pagesHere - 1) + 1 : pageUp;
                var index = knobPaging ? starts[layout] + gaugeNumber - 1 : pageUp - 1; // in the list of all layouts' gauges
                if (index + 1 != shownPage) { shownPage = index + 1; redraw = true; lastShown = null; }
                var renderer = renderers[index];
                var simValues = pageValues[index];
                var indexes = pageIndexes[index];
                if (renderer.Gauge.Modes.Count > 0) values[GaugeDefinition.KnobModeValue] = Volatile.Read(ref modes[index]);


                // Latest values; with no sim data, instruments rest at zero (flags show, needles park).
                var latest = sim.GetValues();
                var noSim = sim.State != SimState.Connected;
                var now = new double[simValues.Count];
                for (var i = 0; i < simValues.Count; i++)
                {
                    now[i] = !noSim && latest[indexes[i]] is { } v ? v : 0;
                    values[simValues[i].Name] = now[i];
                }

                var testing = TestRunning || Highlighted(serial);
                if (testing != wasTesting) redraw = true; // test screen starts or ends
                wasTesting = testing;

                if (testing)
                {
                    if (redraw)
                    {
                        using (var bitmap = DrawTestScreen(serial, renderer.Gauge.Name)) FipImage.ToBuffer(bitmap, buffer);
                        try
                        {
                            if (device.Show(pageUp, buffer)) { redraw = false; _testShown[serial] = DateTime.UtcNow; }
                            else redraw = device.IsPageActive;
                        }
                        catch (FipException ex) when (device.IsRemoved)
                        {
                            Log.Warn($"FIP {serial}: {ex.Message}");
                            device = null;
                            continue;
                        }
                    }
                    stop.WaitHandle.WaitOne(50);
                    continue;
                }

                var layoutBanner = DateTime.UtcNow < layoutBannerUntil;
                var banner = DateTime.UtcNow < bannerUntil || layoutBanner;
                // At most about 20 pictures a second per FIP: smooth to the eye, and far fewer sends to the Logitech driver.
                var tooSoon = sinceSend.IsRunning && sinceSend.Elapsed.TotalMilliseconds < MinMsBetweenSends;
                if (!tooSoon && (redraw || banner != bannerShown || noSim != lastNoSim || lastShown is null || Changed(lastShown, now)))
                {
                    using (var bitmap = renderer.Render(values))
                    {
                        if (noSim) DrawNoSimBanner(bitmap);
                        if (layoutBanner) DrawPageBanner(bitmap, layout + 1, layoutCount, _layoutNames[layout]);
                        else if (banner) DrawPageBanner(bitmap, gaugeNumber, pagesHere, renderer.Gauge.Name);
                        else if (knobFlips && pagesHere > 1) DrawPageTag(bitmap, gaugeNumber, pagesHere);
                        FipImage.ToBuffer(bitmap, buffer);
                    }
                    // Same picture as the one already on the FIP (values moved too little to show): don't send it, the
                    // driver's time goes to the FIPs whose gauges really move (2026-11-13).
                    if (!redraw && onScreenKnown && buffer.AsSpan().SequenceEqual(onScreen))
                    {
                        lastShown = now;
                        lastNoSim = noSim;
                        bannerShown = banner;
                        UpdateButtonLights(device, renderer, values, index + 1, ref lightsSent, ref lightsGauge);
                        _sameSkips.AddOrUpdate(serial, 1, (_, old) => old + 1);
                        stop.WaitHandle.WaitOne(15);
                        continue;
                    }
                    try
                    {
                        // At most MaxSendsAtOnce FIPs hand a picture to the driver at the same time (2026-11-13: all 7 at
                        // once made the driver stall 5 s now and then; one at a time made the needles step like a clock).
                        System.Diagnostics.Stopwatch sendClock;
                        bool shown;
                        SendSlots.Wait();
                        try
                        {
                            sendClock = System.Diagnostics.Stopwatch.StartNew();
                            shown = device.Show(pageUp, buffer);
                        }
                        finally { SendSlots.Release(); }
                        sinceSend.Restart();
                        if (sendClock.Elapsed.TotalMilliseconds < 1000)
                            _sendStats.AddOrUpdate(serial, (1, sendClock.Elapsed.TotalMilliseconds), (_, old) => (old.Count + 1, old.TotalMs + sendClock.Elapsed.TotalMilliseconds));
                        if (sendClock.Elapsed.TotalMilliseconds > 80)
                            _slowSends.AddOrUpdate(serial, (1, sendClock.Elapsed.TotalMilliseconds), (_, old) => (old.Count + 1, Math.Max(old.LongestMs, sendClock.Elapsed.TotalMilliseconds)));
                        if (shown) { buffer.CopyTo(onScreen, 0); onScreenKnown = true; }
                        else onScreenKnown = false;
                        if (shown)
                        {
                            frames++;
                            redraw = false;
                            lastShown = now;
                            UpdateButtonLights(device, renderer, values, index + 1, ref lightsSent, ref lightsGauge);
                            lastNoSim = noSim;
                            bannerShown = banner;
                            _fipStatus[serial] = (layoutCount > 1 ? $"{_layoutNames[layout]}: " : "") + (pagesHere > 1 ? $"Showing page {gaugeNumber} of {pagesHere}" : "Showing");
                        }
                        else
                        {
                            if (device.IsPageActive) skipped++;
                            else _fipStatus[serial] = "Another page is showing";
                            redraw = device.IsPageActive; // skipped frame: try again; hidden page: wait to be shown
                        }
                    }
                    catch (FipException ex) when (device.IsRemoved)
                    {
                        Log.Warn($"FIP {serial}: {ex.Message}");
                        device = null;
                        continue;
                    }
                }
                stop.WaitHandle.WaitOne(15);
            }
            Log.Info($"FIP {serial}: stopped after {frames} frame(s), {skipped} skipped.");
        }
        catch (Exception ex) when (ex is FipException or InvalidDataException or IOException)
        {
            _fipStatus[serial] = $"Stopped: {ex.Message}";
            Report?.Invoke($"FIP {serial} stopped: {ex.Message}");
            Log.Error($"FIP {serial} stopped: {ex}");
        }
        finally
        {
            foreach (var r in renderers) r.Dispose();
        }
    }

    /// <summary>
    /// "2/4 · RPM 1-4" for a moment after a page flip, along the bottom edge where it covers the
    /// least of the gauge.
    /// </summary>
    private static void DrawPageBanner(SKBitmap bitmap, int page, int pages, string gaugeName)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "BarlowCondensed-Medium.ttf");
        _bannerFont ??= (File.Exists(fontPath) ? SKTypeface.FromFile(fontPath) : null) ?? SKTypeface.Default;
        var name = gaugeName.Length > 26 ? gaugeName[..25] + "…" : gaugeName;
        var text = $"{page}/{pages} · {name}";
        using var canvas = new SKCanvas(bitmap);
        using var font = new SKFont(_bannerFont, 17) { Subpixel = true };
        var width = font.MeasureText(text) + 22;
        using var back = new SKPaint { Color = new SKColor(0, 0, 0, 205), IsAntialias = true };
        using var edge = new SKPaint { Color = new SKColor(0x7C, 0xE0, 0x7C), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var box = SKRect.Create(160 - width / 2, 208, width, 26);
        canvas.DrawRoundRect(box, 6, 6, back);
        canvas.DrawRoundRect(box, 6, 6, edge);
        canvas.DrawText(text, 160, 227, SKTextAlign.Center, font, ink);
    }

    /// <summary>
    /// A small "1/4" tag in the bottom-left corner, next to the left knob, on FIPs where that knob flips pages:
    /// shows which page is up and that there are more (owner's idea, 2026-11-12).
    /// </summary>
    private static void DrawPageTag(SKBitmap bitmap, int page, int pages)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "BarlowCondensed-Medium.ttf");
        _bannerFont ??= (File.Exists(fontPath) ? SKTypeface.FromFile(fontPath) : null) ?? SKTypeface.Default;
        var text = $"{page}/{pages}";
        using var canvas = new SKCanvas(bitmap);
        using var font = new SKFont(_bannerFont, 14) { Subpixel = true };
        var width = font.MeasureText(text) + 12;
        using var back = new SKPaint { Color = new SKColor(0, 0, 0, 170), IsAntialias = true };
        using var edge = new SKPaint { Color = new SKColor(0x7C, 0xE0, 0x7C, 200), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var box = SKRect.Create(3, 219, width, 18);
        canvas.DrawRoundRect(box, 4, 4, back);
        canvas.DrawRoundRect(box, 4, 4, edge);
        canvas.DrawText(text, 3 + width / 2, 233, SKTextAlign.Center, font, ink);
    }

    /// <summary>
    /// Sets the S-button lights for the gauge showing: the chosen knob mode's button, and any
    /// "buttonLights" whose value is on. Only sends when something changed; leaves the lights alone
    /// on gauges that use neither.
    /// </summary>
    private static void UpdateButtonLights(FipDevice device, GaugeRenderer renderer, Dictionary<string, double> values, int gaugeNumber, ref int sent, ref int sentFor)
    {
        var gauge = renderer.Gauge;
        var want = 0;
        if (gauge.Modes.Count > 0)
        {
            var mode = (int)values.GetValueOrDefault(GaugeDefinition.KnobModeValue, 1);
            want |= Bit(gauge.Modes[Math.Clamp(mode, 1, gauge.Modes.Count) - 1].Button);
        }
        foreach (var (button, value) in gauge.ButtonLights)
            if (renderer.ValueOf(value, values) > 0.5) want |= Bit(button);
        if (want == sent && gaugeNumber == sentFor) return;
        if (sent <= 0 && want == 0) { sent = 0; sentFor = gaugeNumber; return; } // all off already
        try
        {
            for (var i = 1; i <= 6; i++)
                if (!device.SetButtonLight(i, (want & (1 << (i - 1))) != 0)) return; // our page isn't up: try again next frame
            sent = want;
            sentFor = gaugeNumber;
        }
        catch (FipException ex)
        {
            Log.Warn($"FIP {device.SerialNumber}: button lights: {ex.Message}");
            sent = want; // don't retry every frame
            sentFor = gaugeNumber;
        }

        static int Bit(string button) => int.TryParse(button.AsSpan(6), out var n) && n is >= 1 and <= 6 ? 1 << (n - 1) : 0;
    }

    private static readonly (FipButtons Bit, string Name)[] ControlBits =
    [
        (FipButtons.RightKnobClockwise, "rightKnobClockwise"),
        (FipButtons.RightKnobAnticlockwise, "rightKnobAnticlockwise"),
        (FipButtons.LeftKnobClockwise, "leftKnobClockwise"),
        (FipButtons.LeftKnobAnticlockwise, "leftKnobAnticlockwise"),
        (FipButtons.Button1, "button1"), (FipButtons.Button2, "button2"), (FipButtons.Button3, "button3"),
        (FipButtons.Button4, "button4"), (FipButtons.Button5, "button5"), (FipButtons.Button6, "button6"),
    ];

    /// <summary>True if any value moved enough to matter (ignores tiny sim noise).</summary>
    /// <summary>
    /// True when a value moved enough to redraw: a small fixed step plus a little of the value itself, so small values
    /// (an EPR bug moves 0.005 per knob click) redraw on every change while big jittery ones (altitude) don't redraw
    /// on every tiny wobble. (It was a fixed 0.05, which held the 727's EPR bug back for about ten clicks.)
    /// </summary>
    /// <summary>Per FIP: sends to the FIP that took over 80 ms since the last summary (count, longest).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, double LongestMs)> _slowSends = new();

    /// <summary>Shortest gap between two pictures on one FIP: 50 ms, so about 20 pictures a second at most.</summary>
    private const double MinMsBetweenSends = 50;

    /// <summary>How many FIPs may hand a picture to the Logitech driver at the same time (shared by every panel).</summary>
    private const int MaxSendsAtOnce = 3;
    private static readonly SemaphoreSlim SendSlots = new(MaxSendsAtOnce, MaxSendsAtOnce);

    /// <summary>Per FIP: pictures not sent since the last summary because the FIP already showed exactly that picture.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _sameSkips = new();

    /// <summary>Per FIP: sends under 1 s since the last summary (count, total time).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, double TotalMs)> _sendStats = new();

    internal static bool Changed(double[] before, double[] now)
    {
        for (var i = 0; i < now.Length; i++)
            if (Math.Abs(before[i] - now[i]) > 0.001 + Math.Abs(now[i]) * 0.0001) return true;
        return false;
    }

    private static SKTypeface? _bannerFont;

    private static void DrawNoSimBanner(SKBitmap bitmap)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "BarlowCondensed-Medium.ttf");
        _bannerFont ??= (File.Exists(fontPath) ? SKTypeface.FromFile(fontPath) : null) ?? SKTypeface.Default;
        using var canvas = new SKCanvas(bitmap);
        using var back = new SKPaint { Color = new SKColor(0, 0, 0, 190), IsAntialias = true };
        using var text = new SKPaint { Color = new SKColor(0xE0, 0xB1, 0x3C), IsAntialias = true };
        using var font = new SKFont(_bannerFont, 15);
        canvas.DrawRoundRect(SKRect.Create(110, 196, 100, 22), 4, 4, back);
        canvas.DrawText("NO SIM DATA", 160, 212, SKTextAlign.Center, font, text);
    }

    /// <summary>Big short name (last 4 of the serial) and the gauge this FIP shows, on a dark screen.</summary>
    private static SKBitmap DrawTestScreen(string serial, string gaugeName)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "BarlowCondensed-Medium.ttf");
        _bannerFont ??= (File.Exists(fontPath) ? SKTypeface.FromFile(fontPath) : null) ?? SKTypeface.Default;
        var bitmap = new SKBitmap(new SKImageInfo(FipImage.Width, FipImage.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0x10, 0x30, 0x48));
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var green = new SKPaint { Color = new SKColor(0x7C, 0xE0, 0x7C), IsAntialias = true };
        using var big = new SKFont(_bannerFont, 110);
        using var small = new SKFont(_bannerFont, 22);
        var shortName = serial.Length > 4 ? serial[^4..] : serial;
        canvas.DrawText("FIP TEST OK", 160, 40, SKTextAlign.Center, small, green);
        canvas.DrawText(shortName, 160, 150, SKTextAlign.Center, big, white);
        canvas.DrawText(gaugeName, 160, 200, SKTextAlign.Center, small, white);
        return bitmap;
    }

    private sealed class VariableComparer : IEqualityComparer<SimVariable>
    {
        public bool Equals(SimVariable? a, SimVariable? b) =>
            a is not null && b is not null
            && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Unit, b.Unit, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(SimVariable v) =>
            HashCode.Combine(v.Name.ToUpperInvariant(), v.Unit.ToUpperInvariant());
    }
}
