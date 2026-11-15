// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Fipinator.Core;
using Fipinator.Fip;
using Fipinator.Panel;
using Fipinator.Sim;

namespace Fipinator.App;

/// <summary>"Few FIPs?": how to put several gauges on one FIP and flip them with the left knob.</summary>
internal sealed class LeftKnobHelpForm : Form
{
    public const string GuidePage = AboutForm.ProjectPage + "/blob/main/docs/guides/few-fips.md";

    public LeftKnobHelpForm()
    {
        Text = "Few FIPs? Flip gauges with the left knob";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(18), ColumnCount = 1 };
        layout.Controls.Add(new Label { Text = "Few FIPs? Flip gauges with the left knob", Font = new Font("Segoe UI", 15f, FontStyle.Bold), AutoSize = true });
        if (Picture() is { } picture)
            layout.Controls.Add(new PictureBox { Image = picture, SizeMode = PictureBoxSizeMode.Zoom, Width = 760, Height = 233, Margin = new Padding(0, 10, 0, 10) });
        layout.Controls.Add(new Label
        {
            AutoSize = false, Width = 760, Height = 250,
            Text = "One FIP can hold up to 16 gauges (pages). With \"Left knob flips pages\" ticked, turning that FIP's left knob " +
                   "to the right shows the next gauge, to the left the one before. A name shows for 2 seconds, and Live " +
                   "remembers the last page.\n\n" +
                   "1.  Open Set up FIPs.\n" +
                   "2.  Pick the first engine gauge for a FIP, then \"+ Add page\" for each of the others.\n" +
                   "3.  Tick \"Left knob flips pages\" for that FIP, then Save and use.\n\n" +
                   "Best for engine gauges: on that FIP the left knob flips pages instead of setting things on the gauge " +
                   "(an altimeter's pressure, a heading bug). The right knob still works. Not ticked: the FIP's page buttons flip the pages instead.\n\n" +
                   "Want them to flip by themselves? Pick \"Flip pages by itself: every 5 seconds\" (off by default). Turning the left knob " +
                   "holds the gauge you picked for 30 seconds, then flipping carries on.",
        });
        var more = new LinkLabel { Text = "More in the guide (with ideas for 2, 3 or 4 FIPs)", AutoSize = true };
        more.LinkClicked += (_, _) => AboutForm.Open(GuidePage);
        layout.Controls.Add(more);
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, Margin = new Padding(0, 12, 0, 0) };
        AcceptButton = close;
        layout.Controls.Add(close);
        Controls.Add(layout);
        Theme.Apply(this);
    }

    private static Image? Picture()
    {
        using var stream = typeof(LeftKnobHelpForm).Assembly.GetManifestResourceStream("few-fips-left-knob.png");
        if (stream is null) return null;
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}

/// <summary>About FIPinator Live: version, licence, where it comes from, what it's made for.</summary>
internal sealed class AboutForm : Form
{
    public const string ProjectPage = IssueReport.ProjectPage;

    public AboutForm()
    {
        Text = "About FIPinator Live";
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(470, 370);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var grey = Color.FromArgb(90, 90, 90);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1 };
        layout.Controls.Add(new Label { Text = "FIPinator Live", Font = new Font("Segoe UI", 15f, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Label { Text = $"Version {Version} (beta)", AutoSize = true, ForeColor = grey });
        layout.Controls.Add(new Label
        {
            AutoSize = false, Width = 430, Height = 240, Margin = new Padding(0, 10, 0, 0),
            Text = "Shows gauges on Logitech (Saitek) Flight Instrument Panels while you fly. FIPinator Live only runs your FIPs; " +
                   "FIPinator 3000, its optional companion, is only for making changes and doesn't need to be open while you fly. Part of the free, open-source FIPinator project.\n\n" +
                   "Made and tested for Windows 11 and Microsoft Flight Simulator 2024. It may work elsewhere, but that " +
                   "hasn't been tested; feel free to fork it and adapt it.\n\n" +
                   "FREE and open source (GNU AGPL-3.0): free to use, change, share and fork. Not made by, affiliated with or endorsed by Logitech, " +
                   "Microsoft or PMDG.",
        });
        var page = new LinkLabel { Text = "Project page (GitHub)", AutoSize = true };
        page.LinkClicked += (_, _) => Open(ProjectPage);
        layout.Controls.Add(page);
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, Margin = new Padding(0, 12, 0, 0) };
        AcceptButton = close;
        layout.Controls.Add(close);
        Controls.Add(layout);
        Theme.Apply(this);
    }

    public static string Version =>
        typeof(AboutForm).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    internal static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex) { Log.Warn($"Couldn't open {url}: {ex.Message}"); }
    }
}

/// <summary>
/// A plain-text report for asking someone for help: version, Windows, the setup checks, the panel
/// and FIP states, and the end of the log. Personal details are hidden: the Windows user name and
/// profile folder, and FIP serial numbers (only their last 4 characters are kept).
/// </summary>
internal static class ProblemReport
{
    /// <summary>The setup part of the report: version, Windows, driver, sim, panel and FIP states.</summary>
    public static List<string> Setup(PanelRunner? runner, string panelName, HashSet<string> serials)
    {
        var lines = new List<string>
        {
            $"FIPinator Live {AboutForm.Version}, {DateTime.Now:yyyy-MM-dd HH:mm}",
            $"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
        };
        var library = DirectOutput.FindLibrary();
        var driver = library is not null;
        var driverVersion = SetupCheckForm.DriverVersion(library);
        lines.Add($"Logitech FIP driver: {(driver ? "installed" : "not installed")}{(driverVersion is null ? "" : $" {driverVersion}")}{(library?.Contains("Saitek", StringComparison.OrdinalIgnoreCase) == true ? " (Saitek)" : "")}, service {(driver && DirectOutput.IsServiceRunning() ? "running" : "not running")}");
        lines.Add($"SimConnect: {(SimConnection.FindLibrary() is not null ? "found" : "not found")}");
        lines.Add($"Panel: {panelName}");
        if (runner is not null)
        {
            var (state, message) = runner.SimStatus;
            lines.Add($"Sim: {state} ({message})");
            foreach (var s in runner.ConnectedSerials) serials.Add(s);
            lines.Add($"FIPs connected: {runner.ConnectedSerials.Count}");
            foreach (var (serial, gauge, fipState) in runner.FipStatus)
            {
                serials.Add(serial);
                lines.Add($"  FIP {Short(serial)}: {gauge} - {fipState}");
            }
        }
        else lines.Add("Panel not running.");
        return lines;
    }

    public static string Build(PanelRunner? runner, string panelName)
    {
        var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = new StringBuilder();
        text.AppendLine("FIPinator Live problem report");
        foreach (var line in Setup(runner, panelName, serials)) text.AppendLine(line);
        text.AppendLine();
        text.AppendLine("--- end of today's log ---");
        foreach (var line in IssueReport.LogTail(150)) text.AppendLine(line);
        return Hide(text.ToString(), serials);
    }

    /// <summary>GitHub's "New issue" page filled in with the setup and the latest log warnings (the user submits it).</summary>
    public static string IssueUrl(PanelRunner? runner, string panelName)
    {
        var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var setup = Setup(runner, panelName, serials).Select(l => Hide(l, serials));
        return IssueReport.GitHubIssueUrl("FIPinator Live", setup);
    }

    private static string Short(string serial) => serial.Length > 4 ? "..." + serial[^4..] : serial;

    /// <summary>Hides the user's profile folder, user name and FIP serial numbers.</summary>
    internal static string Hide(string report, IEnumerable<string> serials) => IssueReport.Hide(report, serials);
}
