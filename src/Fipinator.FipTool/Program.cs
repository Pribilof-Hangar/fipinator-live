// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Diagnostics;
using System.Globalization;
using Fipinator.Core;
using Fipinator.Fip;
using Fipinator.Sim;
using Fipinator.Panel;
using SkiaSharp;

// Developer test tool: shows a gauge on a real FIP through Logitech DirectOutput.
//   fipinator-fip list
//   fipinator-fip show <gauge.json> [--fip N] [--set name=value]... [--sweep name]
// Needs the Logitech DirectOutput service running, and SPAD.neXt closed.

const string Usage = """
    Usage:
      fipinator-fip list
          Lists the FIPs that DirectOutput can see.
      fipinator-fip assign SERIAL <gauge.json> [--config FILE]
          Says which gauge a FIP shows (SERIAL can be its last few characters).
      fipinator-fip panel [--config FILE]
          Shows which gauge each FIP is set to show.
      fipinator-fip run [--config FILE] [--seconds S]
          Runs the whole panel: every assigned FIP shows its gauge, live from MSFS 2024.
          Settings are kept in %APPDATA%\FIPinator 3000\panel.json unless --config says otherwise.
      fipinator-fip nudge L:NAME AMOUNT
          Adds AMOUNT to a plane's own value once, like a FIP knob set to change it would, and prints the value
          before and after. No FIP needed.
      fipinator-fip watch <gauge.json> [--seconds S] [--every MS]
          Prints the gauge's live values from MSFS 2024. No FIP needed; SPAD.neXt can stay open.
      fipinator-fip live <gauge.json> --fip SERIAL [--seconds S]
          Shows the gauge on a FIP, moving with live values from MSFS 2024.
      fipinator-fip demo <gauge.json> --sweep name [--seconds S]
          Shows the gauge on every FIP at once, each needle sweeping at a different time.
      fipinator-fip pages SERIAL <gauge.json> <gauge.json>... [--seconds S]
          Puts each gauge on its own page of one FIP and logs what the FIP's page
          up/down buttons do (which page shows, in what order, and when).
      fipinator-fip identify [--seconds S]
          Shows each FIP's number (as used by --fip) in big digits on its screen.
      fipinator-fip show <gauge.json> [--fip N|SERIAL] [--set name=value]... [--sweep name] [--seconds S]
          Shows a gauge on a FIP until you press Enter. Pick the FIP by its serial number
          (or its last 3+ characters), or by number from 'list' (default 1). Numbers can
          change between runs; serial numbers don't.
          --sweep name moves that value's needle back and forth, to check smooth updates.
          --seconds S stops by itself after S seconds.
    Before running: close SPAD.neXt, and start the Logitech DirectOutput service.
    """;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 1 : 0;
}

try
{
    return args[0] switch
    {
        "list" => List(),
        "identify" => Identify(args[1..]),
        "watch" when args.Length >= 2 => Watch(args[1], args[2..]),
        "nudge" when args.Length >= 3 => Nudge(args[1], args[2]),
        "bench" when args.Length >= 2 => Bench(args[1..]),
        "assign" when args.Length >= 3 => Assign(args[1], args[2], args[3..]),
        "panel" => Panel(args[1..]),
        "run" => RunPanel(args[1..]),
        "live" when args.Length >= 2 => Live(args[1], args[2..]),
        "demo" when args.Length >= 2 => Demo(args[1], args[2..]),
        "show" when args.Length >= 2 => Show(args[1], args[2..]),
        "pages" when args.Length >= 3 => Pages(args[1], args[2..]),
        _ => Fail($"Unknown command '{string.Join(' ', args)}'.\n\n{Usage}"),
    };
}
catch (FipException ex)
{
    Console.Error.WriteLine(ex.Message);
    if (ex.Details is not null) Console.Error.WriteLine($"(details: {ex.Details})");
    return 2;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static int List()
{
    using var directOutput = new DirectOutput();
    var devices = directOutput.Devices;
    if (devices.Count == 0)
    {
        Console.WriteLine("DirectOutput is running but sees no FIPs. Check the USB cables, and that SPAD.neXt is closed.");
        return 2;
    }
    Console.WriteLine($"Found {devices.Count} FIP(s):");
    for (var i = 0; i < devices.Count; i++)
        Console.WriteLine($"  {i + 1}. serial {(devices[i].SerialNumber is { Length: > 0 } s ? s : "(none reported)")}");
    return 0;
}

static int Demo(string gaugePath, string[] options)
{
    string? sweep = null;
    var seconds = 60;
    for (var i = 0; i < options.Length; i++)
    {
        switch (options[i])
        {
            case "--sweep" when i + 1 < options.Length: sweep = options[++i]; break;
            case "--seconds" when i + 1 < options.Length && int.TryParse(options[i + 1], out var s) && s >= 1: seconds = s; i++; break;
            default: return Fail($"Unknown option '{options[i]}'. Run with --help to see options.");
        }
    }
    if (sweep is null) return Fail("demo needs --sweep name, for example --sweep ias.");
    if (!File.Exists(gaugePath)) return Fail($"Gauge file not found: {Path.GetFullPath(gaugePath)}");

    using var directOutput = new DirectOutput();
    var devices = directOutput.Devices;
    if (devices.Count == 0)
        return Fail("DirectOutput is running but sees no FIPs. Check the USB cables, and that SPAD.neXt is closed.");

    // Add our page to each FIP one at a time; asking all of them at once has failed (2026-11-07).
    var opened = new bool[devices.Count];
    for (var i = 0; i < devices.Count; i++)
    {
        try { devices[i].Open(); opened[i] = true; }
        catch (FipException ex) { Console.Error.WriteLine($"FIP {devices[i].SerialNumber} couldn't be opened: {ex.Message} (details: {ex.Details})"); }
    }

    Console.WriteLine($"Showing the gauge on {opened.Count(o => o)} of {devices.Count} FIP(s) for {seconds} seconds.");
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    var sent = new int[devices.Count];
    var clock = Stopwatch.StartNew();

    // One thread per FIP, each with its own renderer, so a slow FIP doesn't hold up the others.
    var threads = devices.Select((fip, index) => new Thread(() =>
    {
        using var renderer = GaugeRenderer.FromFile(gaugePath);
        var needle = renderer.Gauge.Layers.OfType<NeedleLayer>().FirstOrDefault(l => l.Value == sweep);
        if (needle is null) { Console.Error.WriteLine($"Gauge has no needle for '{sweep}'."); stop.Cancel(); return; }
        var scale = renderer.Gauge.Scale(needle.Scale);
        var phase = index * 2 * Math.PI / devices.Count;
        var values = new Dictionary<string, double>();
        var buffer = new byte[FipImage.BufferSize];
        if (!opened[index]) return;
        try
        {
            while (!stop.IsCancellationRequested && !fip.IsRemoved)
            {
                var t = (Math.Sin(clock.Elapsed.TotalSeconds * Math.PI / 3 + phase) + 1) / 2;
                values[sweep] = scale.Min + t * (scale.Max - scale.Min);
                using (var bitmap = renderer.Render(values)) FipImage.ToBuffer(bitmap, buffer);
                if (fip.Show(buffer)) Interlocked.Increment(ref sent[index]);
                else stop.Token.WaitHandle.WaitOne(fip.IsPageActive ? 20 : 100); // skipped frame, or our page isn't showing
            }
        }
        catch (FipException ex)
        {
            Console.Error.WriteLine($"FIP {fip.SerialNumber} stopped after {clock.Elapsed.TotalSeconds:0} s: {ex.Message} (details: {ex.Details})");
        }
    }) { IsBackground = true, Name = $"FIP {fip.SerialNumber}" }).ToList();

    threads.ForEach(t => t.Start());
    var last = clock.Elapsed;
    while (!stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
    {
        var elapsed = (clock.Elapsed - last).TotalSeconds;
        last = clock.Elapsed;
        var rates = Enumerable.Range(0, devices.Count).Select(i => Interlocked.Exchange(ref sent[i], 0) / elapsed).ToList();
        Console.WriteLine($"Frames/second per FIP: {string.Join("  ", rates.Select((r, i) => $"{(devices[i].SerialNumber.Length > 4 ? devices[i].SerialNumber[^4..] : $"#{i + 1}")}={r:0.0}"))}  (total {rates.Sum():0.0})");
    }
    threads.ForEach(t => t.Join(TimeSpan.FromSeconds(5)));
    var skipped = devices.Where(d => d.SkippedFrames > 0).Select(d => $"{d.SerialNumber}: {d.SkippedFrames}").ToList();
    Console.WriteLine(skipped.Count == 0 ? "Demo finished. No frames skipped." : $"Demo finished. Skipped frames (DirectOutput hiccups): {string.Join(", ", skipped)}");
    return 0;
}

static int Bench(string[] gauges)
{
    // Times drawing each gauge (what one FIP frame costs before it's sent): 300 frames with a changing value.
    foreach (var path in gauges)
    {
        using var renderer = GaugeRenderer.FromFile(path);
        var names = renderer.Gauge.Values.Keys.ToList();
        var values = new Dictionary<string, double>();
        var times = new List<double>();
        var buffer = new byte[FipImage.BufferSize];
        for (var i = 0; i < 330; i++)
        {
            foreach (var n in names) values[n] = renderer.Gauge.Values[n].Default + i * 0.37;
            var sw = Stopwatch.StartNew();
            using (var bitmap = renderer.Render(values)) FipImage.ToBuffer(bitmap, buffer);
            if (i >= 30) times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        Console.WriteLine($"{renderer.Gauge.Name}: median {times[times.Count / 2]:0.0} ms, 95% {times[(int)(times.Count * 0.95)]:0.0} ms, worst {times[^1]:0.0} ms");
    }
    return 0;
}

static int Nudge(string name, string amountText)
{
    if (!name.StartsWith("L:", StringComparison.Ordinal) || !double.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        return Fail("Use: fipinator-fip nudge L:NAME AMOUNT (for example nudge L:FSS_B727_PCP_EPR_KNOB1 1)");
    using var sim = new SimConnection([new SimVariable(name, "number")]);
    sim.Warning += message => Console.WriteLine($"[sim] Warning: {message}");
    sim.Start();
    var clock = Stopwatch.StartNew();
    while (sim.GetValues()[0] is null && clock.Elapsed < TimeSpan.FromSeconds(15)) Thread.Sleep(100);
    if (sim.GetValues()[0] is not { } before) return Fail($"No value for {name} from the sim (is it running, with that plane?).");
    Console.WriteLine($"{name} before: {before.ToString(CultureInfo.InvariantCulture)}");
    // A whole number of clicks goes in one by one (some planes count each change as one click).
    var clicks = Math.Abs(amount) >= 1 && amount == Math.Round(amount) ? (int)Math.Abs(amount) : 1;
    for (var c = 0; c < clicks; c++) { sim.AddToValue(name, clicks > 1 ? Math.Sign(amount) : amount); Thread.Sleep(60); }
    Thread.Sleep(1500);
    Console.WriteLine($"{name} after:  {sim.GetValues()[0]?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}");
    return 0;
}

static SimConnection StartSim(GaugeRenderer renderer)
{
    var sim = new SimConnection(renderer.Gauge.SimValues().Select(v => new SimVariable(v.Sim, v.Unit)));
    sim.StatusChanged += (_, message) => Console.WriteLine($"[sim] {message}");
    sim.Warning += message => Console.WriteLine($"[sim] Warning: {message}");
    sim.Start();
    return sim;
}

/// <summary>Copies the sim's latest values into the gauge's value names.</summary>
static void ApplySimValues(GaugeRenderer renderer, SimConnection sim, Dictionary<string, double> values)
{
    var names = renderer.Gauge.SimValues();
    var latest = sim.GetValues();
    for (var i = 0; i < names.Count; i++)
    {
        if (latest[i] is { } v) values[names[i].Name] = v;
        else values.Remove(names[i].Name); // back to the gauge's default
    }
}

static int? ReadSeconds(string[] options, ref int i)
{
    if (i + 1 < options.Length && int.TryParse(options[i + 1], out var s) && s >= 1) { i++; return s; }
    return null;
}

static int Watch(string gaugePath, string[] options)
{
    int? seconds = null;
    var every = 500;
    for (var i = 0; i < options.Length; i++)
    {
        if (options[i] == "--seconds" && ReadSeconds(options, ref i) is { } s) seconds = s;
        else if (options[i] == "--every" && i + 1 < options.Length && int.TryParse(options[++i], out var ms) && ms is >= 20 and <= 5000) every = ms;
        else return Fail($"Unknown option '{options[i]}'. Run with --help to see options.");
    }
    if (!File.Exists(gaugePath)) return Fail($"Gauge file not found: {Path.GetFullPath(gaugePath)}");
    using var renderer = GaugeRenderer.FromFile(gaugePath);
    var names = renderer.Gauge.SimValues();
    if (names.Count == 0) return Fail($"Gauge '{renderer.Gauge.Name}' has no values that come from the sim.");

    using var sim = StartSim(renderer);
    using var stop = seconds is { } secs ? new CancellationTokenSource(TimeSpan.FromSeconds(secs)) : new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    if (seconds is null)
    {
        Console.WriteLine("Press Enter to stop.");
        _ = Task.Run(() => { Console.ReadLine(); stop.Cancel(); });
    }

    var lastLine = "";
    var clock = Stopwatch.StartNew();
    while (!stop.Token.WaitHandle.WaitOne(every))
    {
        if (sim.State == SimState.Failed) return 2;
        var latest = sim.GetValues();
        var line = string.Join("   ", names.Select((n, i) =>
            $"{n.Name} = {(latest[i] is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) : "(none yet)")} {n.Unit}"));
        if (line != lastLine && sim.State == SimState.Connected) Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {clock.Elapsed.TotalSeconds,8:0.00}s  {line}");
        lastLine = line;
    }
    Console.WriteLine($"Stopped. Received {sim.Version} update(s) from the sim.");
    return 0;
}

static int Live(string gaugePath, string[] options)
{
    string? fipChoice = null;
    int? seconds = null;
    for (var i = 0; i < options.Length; i++)
    {
        if (options[i] == "--fip" && i + 1 < options.Length) fipChoice = options[++i];
        else if (options[i] == "--seconds" && ReadSeconds(options, ref i) is { } s) seconds = s;
        else return Fail($"Unknown option '{options[i]}'. Run with --help to see options.");
    }
    if (fipChoice is null) return Fail("live needs --fip SERIAL. Run 'fipinator-fip list' to see serial numbers.");
    if (!File.Exists(gaugePath)) return Fail($"Gauge file not found: {Path.GetFullPath(gaugePath)}");
    using var renderer = GaugeRenderer.FromFile(gaugePath);

    using var directOutput = new DirectOutput();
    var fip = directOutput.Devices.FirstOrDefault(d => d.SerialNumber.EndsWith(fipChoice, StringComparison.OrdinalIgnoreCase));
    if (fip is null) return Fail($"No FIP with serial '{fipChoice}'. Run 'fipinator-fip list' to see serial numbers.");
    fip.Open();
    var redraw = true;
    fip.PageActiveChanged += (_, active) => { if (active) redraw = true; };

    using var sim = StartSim(renderer);
    using var stop = seconds is { } secs ? new CancellationTokenSource(TimeSpan.FromSeconds(secs)) : new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    if (seconds is null)
    {
        Console.WriteLine("Press Enter to stop.");
        _ = Task.Run(() => { Console.ReadLine(); stop.Cancel(); });
    }
    Console.WriteLine($"Showing '{renderer.Gauge.Name}' on FIP {fip.SerialNumber} with live sim values.");

    var values = new Dictionary<string, double>();
    var buffer = new byte[FipImage.BufferSize];
    var lastVersion = -1L;
    var lastState = (SimState)(-1);
    var frames = 0;
    while (!stop.IsCancellationRequested && !fip.IsRemoved)
    {
        // Redraw only when something changed: new values, the sim connecting or going away,
        // or our page coming back into view.
        if (sim.Version != lastVersion || sim.State != lastState || redraw)
        {
            lastVersion = sim.Version;
            lastState = sim.State;
            redraw = false;
            ApplySimValues(renderer, sim, values);
            using (var bitmap = renderer.Render(values)) FipImage.ToBuffer(bitmap, buffer);
            try
            {
                if (fip.Show(buffer)) frames++;
                else redraw = fip.IsPageActive; // skipped frame: try again
            }
            catch (FipException) when (fip.IsRemoved) { break; }
        }
        stop.Token.WaitHandle.WaitOne(15);
    }
    Console.WriteLine(fip.IsRemoved ? "Stopped: the FIP was unplugged." : $"Stopped after {frames} frame(s).");
    return fip.IsRemoved ? 2 : 0;
}

static string ConfigPath(string[] options, ref int i, string current)
{
    if (i + 1 < options.Length) return options[++i];
    return current;
}

static int Panel(string[] options)
{
    var path = PanelSettings.DefaultPath;
    for (var i = 0; i < options.Length; i++)
    {
        if (options[i] == "--config") path = ConfigPath(options, ref i, path);
        else return Fail($"Unknown option '{options[i]}'.");
    }
    Console.WriteLine($"Panel settings: {path}");
    if (!File.Exists(path))
    {
        Console.WriteLine("No panel set up yet. Use 'fipinator-fip assign SERIAL GAUGE' for each FIP.");
        return 0;
    }
    PanelSettings settings;
    try { settings = PanelSettings.Load(path); }
    catch (InvalidDataException ex) { return Fail(ex.Message); }
    foreach (var fip in settings.Fips)
    {
        var gauge = GaugeDefinition.Load(PanelSettings.GaugePath(fip, Path.GetDirectoryName(Path.GetFullPath(path))!));
        Console.WriteLine($"  {fip.Serial}: {gauge.Name}  ({fip.Gauge})");
    }
    return 0;
}

static int Assign(string serial, string gaugeFile, string[] options)
{
    var path = PanelSettings.DefaultPath;
    for (var i = 0; i < options.Length; i++)
    {
        if (options[i] == "--config") path = ConfigPath(options, ref i, path);
        else return Fail($"Unknown option '{options[i]}'.");
    }
    if (!File.Exists(gaugeFile)) return Fail($"Gauge file not found: {Path.GetFullPath(gaugeFile)}");
    GaugeDefinition gauge;
    try { gauge = GaugeDefinition.Load(gaugeFile); }
    catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException) { return Fail(ex.Message); }

    // Accept the last few characters of a serial, as long as they match exactly one connected FIP.
    var fullSerial = serial;
    if (DirectOutput.FindLibrary() is not null && DirectOutput.IsServiceRunning())
    {
        using var directOutput = new DirectOutput();
        var matches = directOutput.Devices.Where(d => d.SerialNumber.EndsWith(serial, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1) return Fail($"'{serial}' matches {matches.Count} FIPs; use more of the serial number.");
        if (matches.Count == 1) fullSerial = matches[0].SerialNumber;
        else Console.WriteLine($"Note: no connected FIP has serial '{serial}'. Saving it anyway.");
    }
    else
    {
        Console.WriteLine("Note: the Logitech service isn't running, so the serial number can't be checked. Use the full serial.");
    }

    var settings = File.Exists(path) ? PanelSettings.Load(path) : new PanelSettings();
    var list = settings.Fips.Where(f => !f.Serial.Equals(fullSerial, StringComparison.OrdinalIgnoreCase)).ToList();
    list.Add(new FipAssignment { Serial = fullSerial, Gauge = Path.GetFullPath(gaugeFile) });
    var updated = settings with { Fips = [.. list.OrderBy(f => f.Serial, StringComparer.OrdinalIgnoreCase)] };
    updated.Save(path);
    Console.WriteLine($"FIP {fullSerial} will show '{gauge.Name}'. Saved in {path}");
    return 0;
}

static int RunPanel(string[] options)
{
    var path = PanelSettings.DefaultPath;
    int? seconds = null;
    for (var i = 0; i < options.Length; i++)
    {
        if (options[i] == "--config") path = ConfigPath(options, ref i, path);
        else if (options[i] == "--seconds" && ReadSeconds(options, ref i) is { } s) seconds = s;
        else return Fail($"Unknown option '{options[i]}'. Run with --help to see options.");
    }
    if (!File.Exists(path))
        return Fail($"No panel set up yet ({path}). Use 'fipinator-fip assign SERIAL GAUGE' for each FIP first.");
    PanelSettings settings;
    try { settings = PanelSettings.Load(path); }
    catch (InvalidDataException ex) { return Fail(ex.Message); }

    using var stop = seconds is { } secs ? new CancellationTokenSource(TimeSpan.FromSeconds(secs)) : new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    Console.WriteLine(seconds is null ? "Running the panel. Press Enter to stop." : $"Running the panel for {seconds} seconds.");
    if (seconds is null) _ = Task.Run(() => { Console.ReadLine(); stop.Cancel(); });
    Console.WriteLine($"Log file: {Log.CurrentFile}");
    try
    {
        var runner = new PanelRunner(settings, Path.GetDirectoryName(Path.GetFullPath(path))!);
        runner.Report += message => Console.WriteLine(message);
        foreach (var fip in settings.Fips) Console.WriteLine($"  {fip.Serial}: {fip.Gauge}");
        runner.Run(stop.Token);
        return 0;
    }
    catch (InvalidDataException ex)
    {
        return Fail(ex.Message);
    }
}

static int Pages(string fipChoice, string[] rest)
{
    var seconds = 120;
    var gauges = new List<string>();
    for (var i = 0; i < rest.Length; i++)
    {
        if (rest[i] == "--seconds" && i + 1 < rest.Length && int.TryParse(rest[i + 1], out var s) && s >= 1) { seconds = s; i++; }
        else gauges.Add(rest[i]);
    }
    if (gauges.Count < 2) return Fail("Give at least two gauge files, one per page.");
    var renderers = gauges.Select(GaugeRenderer.FromFile).ToList();

    using var directOutput = new DirectOutput();
    var fip = directOutput.Devices.FirstOrDefault(d => d.SerialNumber.EndsWith(fipChoice, StringComparison.OrdinalIgnoreCase));
    if (fip is null) return Fail($"No FIP's serial ends in '{fipChoice}'. Run 'fipinator-fip list'.");

    var clock = Stopwatch.StartNew();
    var redraw = new AutoResetEvent(true);
    fip.PageChanged += (_, page, active) =>
    {
        Console.WriteLine($"{clock.Elapsed.TotalSeconds,7:0.00}s  page {page} {(active ? "SHOWN" : "hidden")}  ({renderers[page - 1].Gauge.Name})");
        if (active) redraw.Set();
    };
    fip.ButtonsChanged += (_, b) => Console.WriteLine($"{clock.Elapsed.TotalSeconds,7:0.00}s  buttons/knobs: {b}");

    fip.OpenPages(renderers.Count);
    Console.WriteLine($"{renderers.Count} pages on FIP {fip.SerialNumber}; page 1 is showing. Press the FIP's page up/down buttons. Stops after {seconds} s.");
    var end = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < end)
    {
        redraw.WaitOne(TimeSpan.FromMilliseconds(500));
        if (fip.ActivePage is var page and > 0)
        {
            using var bitmap = renderers[page - 1].Render();
            var shown = fip.Show(page, FipImage.ToBuffer(bitmap));
            if (!shown) Console.WriteLine($"{clock.Elapsed.TotalSeconds,7:0.00}s  drawing page {page} was refused");
        }
    }
    Console.WriteLine("Done.");
    return 0;
}

static int Identify(string[] options)
{
    var seconds = 30;
    if (options is ["--seconds", var text] && int.TryParse(text, out var s) && s >= 1) seconds = s;
    else if (options.Length > 0) return Fail("Use: fipinator-fip identify [--seconds S]");

    using var directOutput = new DirectOutput();
    var devices = directOutput.Devices;
    if (devices.Count == 0)
        return Fail("DirectOutput is running but sees no FIPs. Check the USB cables, and that SPAD.neXt is closed.");

    var font = Path.Combine(AppContext.BaseDirectory, "BarlowCondensed-Medium.ttf");
    using var typeface = (File.Exists(font) ? SKTypeface.FromFile(font) : null) ?? SKTypeface.Default;
    using var big = new SKFont(typeface, 150);
    using var small = new SKFont(typeface, 26);
    using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };

    for (var i = 0; i < devices.Count; i++)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(FipImage.Width, FipImage.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0x1E, 0x3A, 0x5F));
            canvas.DrawText($"{i + 1}", 160, 160, SKTextAlign.Center, big, paint);
            canvas.DrawText($"FIP {i + 1}  ·  serial {devices[i].SerialNumber}", 160, 215, SKTextAlign.Center, small, paint);
        }
        devices[i].Open();
        devices[i].Show(FipImage.ToBuffer(bitmap));
        Console.WriteLine($"FIP {i + 1}: serial {devices[i].SerialNumber}");
    }

    Console.WriteLine($"Showing FIP numbers for {seconds} seconds.");
    Thread.Sleep(TimeSpan.FromSeconds(seconds));
    return 0;
}

static int Show(string gaugePath, string[] options)
{
    var values = new Dictionary<string, double>();
    var fipChoice = "1";
    string? sweep = null;
    int? seconds = null;
    for (var i = 0; i < options.Length; i++)
    {
        switch (options[i])
        {
            case "--fip" when i + 1 < options.Length:
                fipChoice = options[++i];
                break;
            case "--set" when i + 1 < options.Length:
                var parts = options[++i].Split('=', 2);
                if (parts.Length != 2 || !double.TryParse(parts[1], CultureInfo.InvariantCulture, out var v))
                    return Fail($"Can't read '{options[i]}'. Use --set name=value, for example --set ias=190.");
                values[parts[0]] = v;
                break;
            case "--sweep" when i + 1 < options.Length:
                sweep = options[++i];
                break;
            case "--seconds" when i + 1 < options.Length && int.TryParse(options[i + 1], out var s) && s >= 1:
                seconds = s;
                i++;
                break;
            default:
                return Fail($"Unknown option '{options[i]}'. Run with --help to see options.");
        }
    }

    if (!File.Exists(gaugePath)) return Fail($"Gauge file not found: {Path.GetFullPath(gaugePath)}");
    using var renderer = GaugeRenderer.FromFile(gaugePath);

    ScaleSpec? sweepScale = null;
    if (sweep is not null)
    {
        var needle = renderer.Gauge.Layers.OfType<NeedleLayer>().FirstOrDefault(l => l.Value == sweep);
        if (needle is null) return Fail($"Gauge '{renderer.Gauge.Name}' has no needle for '{sweep}', so it can't sweep it.");
        sweepScale = renderer.Gauge.Scale(needle.Scale);
    }

    using var directOutput = new DirectOutput();
    var devices = directOutput.Devices;
    if (devices.Count == 0)
        return Fail("DirectOutput is running but sees no FIPs. Check the USB cables, and that SPAD.neXt is closed.");
    // DirectOutput's order can change between runs, so a serial number is the reliable way to pick a FIP.
    var fip = devices.FirstOrDefault(d => d.SerialNumber.Equals(fipChoice, StringComparison.OrdinalIgnoreCase))
        ?? (fipChoice.Length >= 3 ? devices.FirstOrDefault(d => d.SerialNumber.EndsWith(fipChoice, StringComparison.OrdinalIgnoreCase)) : null)
        ?? (fipChoice.Length <= 2 && int.TryParse(fipChoice, out var n) && n >= 1 && n <= devices.Count ? devices[n - 1] : null);
    if (fip is null)
        return Fail($"No FIP matches '{fipChoice}'. Use a number from 1 to {devices.Count}, or a serial number from 'fipinator-fip list'.");
    var fipNumber = devices.ToList().IndexOf(fip) + 1;
    var redraw = new ManualResetEventSlim(true);
    fip.PageActiveChanged += (_, active) =>
    {
        Console.WriteLine(active ? "FIPinator's page is showing again." : "Another page is showing on the FIP (page button pressed).");
        if (active) redraw.Set();
    };
    fip.ButtonsChanged += (_, buttons) => Console.WriteLine($"Buttons/knobs: {buttons}");
    directOutput.DeviceRemoved += d =>
    {
        if (d == fip) Console.WriteLine("The FIP was unplugged. Press Enter to quit.");
    };

    fip.Open();
    Console.WriteLine(seconds is { } limit
        ? $"Showing '{renderer.Gauge.Name}' on FIP {fipNumber} for {limit} seconds."
        : $"Showing '{renderer.Gauge.Name}' on FIP {fipNumber}. Press Enter to stop.");
    if (sweep is null) Console.WriteLine("Press the FIP's soft buttons or turn its knobs to check they're reported here.");

    using var stop = seconds is { } secs ? new CancellationTokenSource(TimeSpan.FromSeconds(secs)) : new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    if (seconds is null) _ = Task.Run(() => { Console.ReadLine(); stop.Cancel(); });

    var buffer = new byte[FipImage.BufferSize];
    var clock = Stopwatch.StartNew();
    var frames = 0;
    var sent = 0;
    var lastReport = clock.Elapsed;
    var frameTime = TimeSpan.FromSeconds(1.0 / 30);

    while (!stop.IsCancellationRequested && !fip.IsRemoved)
    {
        var frameStart = clock.Elapsed;
        if (sweepScale is not null)
        {
            // Full sweep up and back down every 6 seconds.
            var t = (Math.Sin(clock.Elapsed.TotalSeconds * Math.PI / 3) + 1) / 2;
            values[sweep!] = sweepScale.Min + t * (sweepScale.Max - sweepScale.Min);
        }
        else if (!redraw.IsSet)
        {
            WaitHandle.WaitAny([redraw.WaitHandle, stop.Token.WaitHandle], 250);
            continue;
        }
        redraw.Reset();

        using (var bitmap = renderer.Render(values)) FipImage.ToBuffer(bitmap, buffer);
        try
        {
            if (fip.Show(buffer)) sent++;
        }
        catch (FipException) when (fip.IsRemoved)
        {
            break;
        }
        frames++;

        if (sweepScale is not null && clock.Elapsed - lastReport >= TimeSpan.FromSeconds(5))
        {
            var elapsed = (clock.Elapsed - lastReport).TotalSeconds;
            Console.WriteLine($"{sent / elapsed:0.0} frames/second reached the FIP.");
            sent = 0;
            lastReport = clock.Elapsed;
        }

        var wait = frameTime - (clock.Elapsed - frameStart);
        if (sweepScale is not null && wait > TimeSpan.Zero) stop.Token.WaitHandle.WaitOne(wait);
    }

    Console.WriteLine(fip.IsRemoved ? "Stopped: the FIP was unplugged." : $"Stopped after {frames} frame(s). The FIP goes back to its other pages.");
    return fip.IsRemoved ? 2 : 0;
}
