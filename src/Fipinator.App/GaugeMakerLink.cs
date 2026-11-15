// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Diagnostics;
using Fipinator.Core;

namespace Fipinator.App;

/// <summary>
/// Opens FIPinator 3000 from Live: on its start page, or straight on "Set up your panel" with a panel picked
/// (<c>--panel "path"</c>). When 3000 isn't installed it says what 3000 can do, that it's optional and FREE, and
/// where to get it (owner, 2026-11-14).
/// </summary>
internal static class GaugeMakerLink
{
    public static string Exe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FIPinator 3000", "FIPinator3000.exe");

    /// <summary>Starts 3000 (with the panel, if given), or shows what it is when it isn't installed. Returns a problem to show, or null.</summary>
    public static string? Open(IWin32Window? owner, string? panelPath)
    {
        if (!File.Exists(Exe))
        {
            using var info = new GaugeMakerInfoForm();
            info.ShowDialog(owner);
            return null;
        }
        try
        {
            var start = new ProcessStartInfo(Exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(Exe)! };
            if (panelPath is not null && File.Exists(panelPath)) { start.ArgumentList.Add("--panel"); start.ArgumentList.Add(panelPath); }
            Process.Start(start)?.Dispose();
            Log.Info(panelPath is null ? "Opened FIPinator 3000." : $"Opened FIPinator 3000 on panel {Path.GetFileName(panelPath)}.");
            return null;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error($"Couldn't start FIPinator 3000: {ex}");
            return $"Couldn't start FIPinator 3000: {ex.Message}";
        }
    }
}

/// <summary>"FIPinator 3000 isn't installed": what it can do, optional and FREE, and where to get it.</summary>
internal sealed class GaugeMakerInfoForm : Form
{
    public GaugeMakerInfoForm()
    {
        Text = "FIPinator 3000";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(20) };
        layout.Controls.Add(new Label { Text = "FIPinator 3000 isn't installed", Font = new Font("Segoe UI", 14f, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Label
        {
            AutoSize = false, Width = 470, Height = 280, Margin = new Padding(0, 10, 0, 0),
            Text = "FIPinator 3000 is FIPinator Live's optional, FREE companion: it changes the gauges and panels that Live puts on your FIPs. With it you can:\n\n" +
                   "•  Set up your panel like your desk: drag gauges onto your FIPs, any gauge on any FIP, add pages, " +
                   "make layouts (e.g. Nav and Engines) and save it as a new panel.\n" +
                   "•  Change how a gauge looks: quick colours, or everything in the full editor.\n" +
                   "•  Combine gauges: several needles on one dial, or two gauges side by side.\n" +
                   "•  Start from any of the ready-made gauges.\n" +
                   "•  Make brand-new gauges with an AI, using the AI README and gauge-making guide.\n\n" +
                   "You don't need it to fly: FIPinator Live works fine on its own.",
        });
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
        var get = new Button { Text = "Get FIPinator 3000 (GitHub)", AutoSize = true, Tag = "accent" };
        get.Click += (_, _) => AboutForm.Open(IssueReport.GaugeMakerPage + "/releases");
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.Add(get);
        buttons.Controls.Add(close);
        layout.Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;
        Controls.Add(layout);
        Theme.Apply(this);
    }
}
