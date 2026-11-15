// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Text.Json;
using Fipinator.Core;
using Fipinator.Fip;
using Fipinator.Panel;
using Fipinator.Sim;

namespace Fipinator.App;

/// <summary>
/// "Check my setup": the first-start welcome. One line each for the Logitech FIP driver, the FIPs and
/// the sim, with a button for whatever is missing, then what to do next. Never blocks anything, and
/// re-checks by itself every second, so plugging in a FIP or starting the sim turns its line green.
/// </summary>
internal sealed class SetupCheckForm : Form
{
    private static readonly Color Good = Theme.Good;
    private static readonly Color Bad = Theme.Bad;
    private static readonly Color Wait = Theme.Wait;
    private static readonly Color Grey = Theme.Muted;

    private readonly Func<PanelRunner?> _runner;
    /// <summary>The Logitech driver version this was made and tested with (owner's PC, 2026-11).</summary>
    private static readonly Version TestedDriver = new(8, 0, 132);
    public const string SpadHelpPage = SpadExport.TogetherGuide; // "Using SPAD.neXt and FIPinator Live together"

    private readonly Label _driverMark = Mark(), _fipsMark = Mark(), _simMark = Mark(), _spadMark = Mark();
    private readonly Label _driver = Line(), _fips = Line(), _sim = Line(), _spadLine = Line();
    private readonly Button _spadHow = new() { Text = "Share the FIPs...", AutoSize = true };
    private readonly Button _spadFolder = new() { Text = "SPAD gauges", AutoSize = true };
    private readonly Button _getDriver = new() { Text = "Get it from Logitech", AutoSize = true };
    private readonly Button _startDriver = new() { Text = "Start the FIP service", AutoSize = true };
    private readonly Button _spad = new() { Text = "Using SPAD.neXt?", AutoSize = true };
    private readonly Label _next = new() { AutoSize = false, Width = 660, Height = 46, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
    private readonly CheckBox _showAtStart = new() { Text = "Show this every time FIPinator Live starts", AutoSize = true };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };

    public SetupCheckForm(Func<PanelRunner?> runner, Action startDriver, Action getDriver, bool showAtStart)
    {
        _runner = runner;
        Text = "FIPinator Live - Check my setup";
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(720, 590); // taller lines since 2026-11-14 (driver advice); resizable like the main window
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        MinimumSize = new Size(736, 520);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        _getDriver.Click += (_, _) => getDriver();
        _startDriver.Click += (_, _) => { startDriver(); Check(); };
        _spad.Click += (_, _) => ShowSpadHelp();
        _spadHow.Click += (_, _) => ShareOrStop();
        _spadFolder.Click += (_, _) => OpenSpadGauges();
        _showAtStart.Checked = showAtStart;
        _showAtStart.CheckedChanged += (_, _) => LiveSettings.Save(LiveSettings.Load() with { ShowWelcome = _showAtStart.Checked });
        // This window sits beside the main one (it isn't modal), so Close has to close it itself:
        // a button's DialogResult only closes modal windows.
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        AcceptButton = close;
        CancelButton = close;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1 };
        layout.Controls.Add(new Label { Text = "Welcome to FIPinator Live", Font = new Font("Segoe UI", 15f, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Label
        {
            Text = "It shows your gauges on Logitech Flight Instrument Panels (FIPs) while you fly. Here's what it needs:",
            AutoSize = false, Width = 660, Height = 42, ForeColor = Grey,
        });
        layout.Controls.Add(Row("1", _driverMark, _driver, _getDriver, _startDriver));
        layout.Controls.Add(Row("2", _fipsMark, _fips, _spad));
        layout.Controls.Add(Row("3", _simMark, _sim));
        layout.Controls.Add(Row("4", _spadMark, _spadLine, _spadHow, _spadFolder));
        layout.Controls.Add(new Label { Text = "What to do next", AutoSize = true, Margin = new Padding(0, 14, 0, 2), ForeColor = Grey });
        layout.Controls.Add(_next);
        layout.Controls.Add(new Label
        {
            Text = "Made and tested for Windows 11 and Microsoft Flight Simulator 2024. It may work elsewhere, but that hasn't been tested.",
            AutoSize = false, Width = 660, Height = 40, ForeColor = Grey, Font = new Font("Segoe UI", 8.5f),
        });
        var bottom = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        bottom.Controls.Add(_showAtStart);
        close.Margin = new Padding(40, 0, 0, 0);
        bottom.Controls.Add(close);
        layout.Controls.Add(bottom);
        Controls.Add(layout);
        Theme.Apply(this);

        _refresh.Tick += (_, _) => Check();
        Shown += (_, _) => { Check(); _refresh.Start(); };
        FormClosed += (_, _) => _refresh.Stop();
    }

    private static Label Mark() => new() { AutoSize = false, Width = 26, Height = 24, Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
    private static Label Line() => new() { AutoSize = false, Width = 380, Height = 64 }; // room for three lines (the driver advice)

    private static FlowLayoutPanel Row(string number, Label mark, Label text, params Button[] buttons)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        row.Controls.Add(new Label { Text = number + ".", AutoSize = false, Width = 22, Height = 24, ForeColor = Grey });
        row.Controls.Add(mark);
        row.Controls.Add(text);
        foreach (var b in buttons) { b.Margin = new Padding(6, 0, 0, 0); row.Controls.Add(b); }
        return row;
    }

    private static void Set(Label mark, Label text, bool? ok, string message)
    {
        mark.Text = ok switch { true => "✓", false => "✗", null => "…" };
        mark.ForeColor = ok switch { true => Good, false => Bad, null => Wait };
        text.Text = message;
    }

    /// <summary>Looks at everything again and updates the lines and the advice.</summary>
    private void Check()
    {
        // 1. The Logitech FIP driver (DirectOutput) and its Windows service.
        var library = DirectOutput.FindLibrary();
        var installed = library is not null;
        var running = installed && DirectOutput.IsServiceRunning();
        var version = DriverVersion(library);
        var older = version is not null && version < TestedDriver;
        // The old Saitek-branded driver (before Logitech took over the FIPs) is found under Saitek's registry key.
        var saitek = library is not null && library.Contains("Saitek", StringComparison.OrdinalIgnoreCase);
        var versionText = version is null ? "" : $" (version {version})";
        Set(_driverMark, _driver, installed && running,
            !installed ? "Logitech FIP driver: not installed. FIPinator Live needs it to reach the FIPs."
            : !running ? $"Logitech FIP driver{versionText}: installed, but its service isn't running."
            : saitek ? $"FIP driver: the older Saitek one is running{versionText}. It may work, but the newest Logitech FIP driver is recommended (button on this line)."
            : older ? $"Logitech FIP driver: running, version {version}. FIPinator was tested with {TestedDriver}; updating to the newest Logitech driver is recommended (button on this line)."
            : $"Logitech FIP driver: installed and running{versionText}.");
        _getDriver.Visible = !installed || older || saitek;
        _startDriver.Visible = installed && !running;

        // 2. The FIPs themselves (only countable once the driver runs).
        var spadRunning = SpadRunning();
        var runner = _runner();
        var fips = runner?.ConnectedSerials.Count ?? 0;
        if (!running) Set(_fipsMark, _fips, null, "FIPs: waiting for the Logitech driver.");
        else if (runner is null) Set(_fipsMark, _fips, null, "FIPs: looking...");
        else if (fips == 0) Set(_fipsMark, _fips, false, spadRunning
            ? "FIPs: none found, and SPAD.neXt is running: it probably has the FIPs right now. Close SPAD.neXt, or see line 4."
            : "FIPs: none found. Check the USB cables and power.");
        else Set(_fipsMark, _fips, true, $"FIPs: {fips} connected.");
        _spad.Visible = false; // SPAD has its own line now

        // 4. SPAD.neXt: the two take turns with the FIPs (SPAD.neXt can't draw while Logitech's service runs).
        var spadHere = spadRunning || SpadExport.SpadInstalled();
        var sharing = spadHere && FipSharingSetup.IsSetUp;
        if (!spadHere) Set(_spadMark, _spadLine, true, "SPAD.neXt: not on this PC, nothing to do.");
        else if (sharing) Set(_spadMark, _spadLine, true, "SPAD.neXt: sharing the FIPs. FIPinator Live has them while it's open, SPAD.neXt the rest of the time.");
        else Set(_spadMark, _spadLine, null, "SPAD.neXt is on this PC. Use both? FIPinator Live can hand the FIPs back to SPAD.neXt whenever it closes.");
        _spadHow.Visible = spadHere;
        _spadHow.Text = sharing ? "Stop sharing..." : "Share the FIPs...";
        _spadFolder.Visible = spadHere;

        // 3. The sim. Not running isn't a problem: panels can be set up and tested without it.
        if (SimConnection.FindLibrary() is null)
            Set(_simMark, _sim, false, "Flight simulator: can't talk to it yet. For now FIPinator needs SimConnect from the MSFS 2024 SDK.");
        else if (runner?.SimStatus.State == SimState.Connected)
            Set(_simMark, _sim, true, "Flight simulator: connected to MSFS 2024.");
        else
            Set(_simMark, _sim, null, "Flight simulator: MSFS 2024 isn't running. That's fine; start it when you want to fly.");

        _next.ForeColor = Theme.Text;
        _next.Text =
            !installed ? "Install the Logitech FIP driver (button on line 1), then come back here."
            : !running ? "Start the Logitech driver (\"Start the FIP service\" on line 1). Windows asks for permission."
            : runner is null ? "Getting ready..."
            : fips == 0 ? "Connect your FIPs (line 2). The line turns green by itself."
            : runner.SimStatus.State != SimState.Connected ? "Your FIPs are ready. Pick a panel in the main window, then start MSFS 2024."
            : "All set. Pick a panel in the main window and fly.";
    }

    /// <summary>Turns "Share the FIPs with SPAD.neXt" on (after explaining it) or off.</summary>
    private void ShareOrStop()
    {
        var sharing = FipSharingSetup.IsSetUp;
        var question = sharing
            ? "Stop sharing the FIPs with SPAD.neXt?\n\nLogitech's FIP service goes back to exactly how it was before (Windows asks for permission once). " +
              "After that, SPAD.neXt can only draw on the FIPs while Logitech's service is stopped."
            : "Share the FIPs with SPAD.neXt?\n\n" +
              "FIPinator Live will have the FIPs while it's open and hand them to SPAD.neXt when you close it. Nothing else to do: no buttons, no commands.\n\n" +
              "Windows asks for permission once. It only lets FIPinator Live switch Logitech's FIP service on and off, and makes that service start only when needed instead of with Windows. You can undo it here any time.\n\n" +
              "Using the X52 Pro joystick's little screen? Then leave this off: that screen needs Logitech's service running.";
        if (MessageBox.Show(this, question, sharing ? "Stop sharing" : "Share the FIPs with SPAD.neXt",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var problem = FipSharingSetup.Run(setup: !sharing);
        MessageBox.Show(this, problem ?? (sharing
                ? "Done. Logitech's FIP service is back as it was."
                : "Done. From now on FIPinator Live hands the FIPs to SPAD.neXt whenever it closes."),
            "FIPinator Live", MessageBoxButtons.OK, problem is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        Check();
    }

    /// <summary>Opens the folder with the free SPAD.neXt versions of the gauges (made by FIPinator 3000).</summary>
    private void OpenSpadGauges()
    {
        var folder = SpadExport.GaugesFolder;
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true, Verb = "open" })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"Couldn't open {folder}: {ex.Message}");
            MessageBox.Show(this, $"Your SPAD.neXt gauges are in: {folder}", "SPAD gauges folder");
        }
    }

    /// <summary>Opens the README's "Already using SPAD.neXt?" section.</summary>
    private void ShowSpadHelp()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SpadHelpPage) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Warn($"Couldn't open the SPAD help page: {ex.Message}");
            MessageBox.Show(this, $"In SPAD.neXt, switch off its support for the Logitech (Saitek) FIPs. More: {SpadHelpPage}", "Using SPAD.neXt?");
        }
    }

    /// <summary>SPAD.neXt runs as SPAD.neXt.exe (checked on the owner's PC).</summary>
    private static bool SpadRunning()
    {
        var found = System.Diagnostics.Process.GetProcessesByName("SPAD.neXt");
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    /// <summary>The Logitech driver's version, from DirectOutput.dll's file version (null if unreadable).</summary>
    internal static Version? DriverVersion(string? library)
    {
        if (library is null) return null;
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(library);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException) { return null; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _refresh.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>FIPinator Live's own small settings, in Documents\FIPinator 3000\fipinator-live.json.</summary>
internal sealed record LiveSettings(bool WelcomeSeen = false, bool ShowWelcome = false, Dictionary<string, int>? Pages = null, bool HandoverNoteHidden = false,
    int SetupWidth = 0, int SetupHeight = 0, bool SetupMaximized = false, int MainWidth = 0, int MainHeight = 0)
{
    private static string Path => System.IO.Path.Combine(PanelLibrary.ForCurrentUser().Folder, "fipinator-live.json");
    private static readonly object Gate = new(); // FIP workers save pages from their own threads (re-entrant)

    public static LiveSettings Load()
    {
        lock (Gate)
        {
            try { return File.Exists(Path) ? JsonSerializer.Deserialize<LiveSettings>(File.ReadAllText(Path), GaugeDefinition.JsonOptions) ?? new() : new(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Log.Warn($"Live settings unreadable, using defaults: {ex.Message}"); return new(); }
        }
    }

    // Pages are remembered per panel and FIP ("Classic twin|SERIAL"): each panel has its own stacks.
    private static string PageKey(string panel, string serial) => $"{panel}|{serial.ToUpperInvariant()}";

    /// <summary>The page a FIP was last on in this panel (1 if not known).</summary>
    public static int PageFor(string panel, string serial) => Load().Pages?.GetValueOrDefault(PageKey(panel, serial), 1) ?? 1;

    /// <summary>Remembers the page a FIP is on in this panel, so it starts there next time.</summary>
    public static void RememberPage(string panel, string serial, int page)
    {
        lock (Gate)
        {
            var settings = Load();
            var pages = new Dictionary<string, int>(settings.Pages ?? []) { [PageKey(panel, serial)] = page };
            Save(settings with { Pages = pages });
        }
    }

    public static void Save(LiveSettings settings)
    {
        lock (Gate)
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, GaugeDefinition.JsonOptions));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Couldn't save live settings: {ex.Message}"); }
    }
}
