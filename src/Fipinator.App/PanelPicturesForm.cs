// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Drawing.Drawing2D;
using Fipinator.Core;
using Fipinator.Panel;
using SkiaSharp;

namespace Fipinator.App;

/// <summary>
/// "What's on my FIPs": a picture of every gauge in the running panel, one tile per FIP with its pages in
/// order, so nobody has to remember. Click a tile and that FIP shows its name for a few seconds, to match
/// picture and hardware. Can save the whole panel as one picture (a cheat sheet). Gauges are shown at rest
/// (not moving with the sim): the FIPs show the live version.
/// </summary>
internal sealed class PanelPicturesForm : Form
{
    private readonly Func<PanelRunner?> _runner;
    private readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold) };
    private readonly Label _hint = new()
    {
        AutoSize = true, ForeColor = Color.FromArgb(90, 90, 90),
        Text = "Each box is one FIP: what it shows first, then its other pages in order. Click a box and that FIP shows its name for a few seconds.",
    };
    private readonly FlowLayoutPanel _tiles = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8), BackColor = Color.FromArgb(242, 242, 242) };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.FromArgb(90, 90, 90) };
    private readonly List<Bitmap> _images = [];
    private string? _panelPath;
    private readonly List<(string Serial, string Label, List<(string Name, Bitmap Image)> Pages)> _shown = [];

    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 1000 };
    private DateTime _panelWritten;

    /// <param name="runner">The running panel (to show a FIP's name), or null.</param>
    /// <param name="activePanel">The panel file in use; the pictures follow it when it changes or is edited.</param>
    public PanelPicturesForm(Func<PanelRunner?> runner, Func<string> activePanel)
    {
        _runner = runner;
        _watch.Tick += (_, _) =>
        {
            var path = activePanel();
            var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (!string.Equals(path, _panelPath, StringComparison.OrdinalIgnoreCase) || written != _panelWritten) ShowPanel(path);
        };
        Shown += (_, _) => { ShowPanel(activePanel()); _watch.Start(); };
        FormClosed += (_, _) => _watch.Dispose();
        Text = "FIPinator Live - What's on my FIPs";
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(1100, 760);
        MinimumSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        using (var icon = typeof(MainForm).Assembly.GetManifestResourceStream("fipinator.ico"))
            if (icon is not null) Icon = new Icon(icon);

        var save = Theme.SmallButton("Save a picture of this panel", Theme.Icons.Photo, (_, _) => SaveSheet());
        var change = Theme.SmallButton("Change this panel in FIPinator 3000", Theme.Icons.Wrench, (_, _) =>
        {
            if (GaugeMakerLink.Open(this, _panelPath) is { } problem) _status.Text = problem;
        });
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12, 10, 12, 6) };
        top.Controls.Add(_title);
        top.Controls.Add(_hint);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12, 6, 12, 10) };
        bottom.Controls.Add(save);
        bottom.Controls.Add(change);
        bottom.Controls.Add(_status);
        Controls.Add(_tiles);
        Controls.Add(top);
        Controls.Add(bottom);
        FormClosed += (_, _) => FreeImages();
        Theme.Apply(this);
    }

    /// <summary>Shows the pictures for a panel file (called when opened and whenever the panel changes).</summary>
    public void ShowPanel(string panelPath)
    {
        _panelPath = panelPath;
        _panelWritten = File.Exists(panelPath) ? File.GetLastWriteTimeUtc(panelPath) : DateTime.MinValue;
        Cursor = Cursors.WaitCursor;
        _tiles.SuspendLayout();
        try
        {
            foreach (Control c in _tiles.Controls) c.Dispose();
            _tiles.Controls.Clear();
            FreeImages();
            _shown.Clear();
            var settings = PanelSettings.Load(panelPath);
            var folder = Path.GetDirectoryName(panelPath)!;
            _title.Text = settings.Name ?? Path.GetFileNameWithoutExtension(panelPath);
            foreach (var fip in settings.Fips)
            {
                var pages = PanelSettings.PagePaths(fip, folder).Select(p => (Name: GaugeName(p), Image: Picture(p))).ToList();
                var how = pages.Count == 1 ? "one gauge"
                    : $"{pages.Count} pages · " + string.Join(" · ", new[] { fip.LeftKnobPages ? "left knob flips pages" : fip.AutoFlipSeconds > 0 ? null : "page buttons", fip.AutoFlipSeconds > 0 ? $"flips {PageFlipTimer.Describe(fip.AutoFlipSeconds)}" : null }.OfType<string>());
                var label = $"FIP …{Short(fip.Serial)}  ({how})";
                _shown.Add((fip.Serial, label, pages));
                _tiles.Controls.Add(Tile(fip.Serial, label, pages));
            }
            _status.Text = "";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _title.Text = "This panel can't be shown";
            _status.Text = ex.Message;
            Log.Warn($"Panel pictures: {ex.Message}");
        }
        finally
        {
            _tiles.ResumeLayout();
            Cursor = Cursors.Default;
        }
    }

    private Control Tile(string serial, string label, List<(string Name, Bitmap Image)> pages)
    {
        var tile = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.White,
            Margin = new Padding(6), Padding = new Padding(10), Cursor = Cursors.Hand,
        };
        tile.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) });
        tile.Controls.Add(Thumb(pages[0], 240, 180, pages.Count > 1 ? "1. " : ""));
        if (pages.Count > 1)
        {
            var more = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(260, 0), Margin = new Padding(0, 6, 0, 0) };
            for (var i = 1; i < pages.Count; i++) more.Controls.Add(Thumb(pages[i], 80, 60, $"{i + 1}. ", small: true));
            tile.Controls.Add(more);
        }
        void Click(object? s, EventArgs e) => HighlightFip(serial);
        Hook(tile, Click);
        return tile;
    }

    private static Control Thumb((string Name, Bitmap Image) page, int w, int h, string number, bool small = false)
    {
        var box = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, small ? 6 : 0, small ? 4 : 0) };
        box.Controls.Add(new PictureBox { Image = page.Image, Size = new Size(w, h), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0) });
        box.Controls.Add(new Label
        {
            Text = number + page.Name, AutoSize = false, Width = w, Height = small ? 32 : 22, Margin = new Padding(0, 2, 0, 0),
            Font = new Font("Segoe UI", small ? 7.5f : 9f), ForeColor = Color.FromArgb(60, 60, 60), AutoEllipsis = true,
        });
        return box;
    }

    private static void Hook(Control control, EventHandler click)
    {
        control.Click += click;
        foreach (Control child in control.Controls) Hook(child, click);
    }

    private void HighlightFip(string serial)
    {
        if (_runner() is not { } runner)
        {
            _status.Text = "The FIPs aren't running right now, so nothing can show on them.";
            return;
        }
        runner.Highlight(serial, TimeSpan.FromSeconds(4));
        _status.Text = $"FIP …{Short(serial)} is showing its name for a few seconds.";
    }

    /// <summary>Saves every FIP and page as one picture in Documents\FIPinator 3000\Panel pictures, and shows it.</summary>
    private void SaveSheet()
    {
        if (_shown.Count == 0 || _panelPath is null) return;
        const int cell = 260, picW = 240, picH = 180, cols = 4;
        var items = _shown.SelectMany(f => f.Pages.Select((p, i) => (Title: $"FIP …{Short(f.Serial)}" + (f.Pages.Count > 1 ? $"  page {i + 1}" : ""), p.Name, p.Image))).ToList();
        var rows = (items.Count + cols - 1) / cols;
        using var sheet = new Bitmap(cols * cell + 20, rows * 236 + 60);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using var titleFont = new Font("Segoe UI", 14f, FontStyle.Bold);
            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var small = new Font("Segoe UI", 8.5f);
            g.DrawString(_title.Text, titleFont, Brushes.Black, 10, 10);
            for (var i = 0; i < items.Count; i++)
            {
                var (x, y) = (10 + i % cols * cell, 50 + i / cols * 236);
                g.DrawString(items[i].Title, font, Brushes.Black, x, y);
                g.DrawImage(items[i].Image, new Rectangle(x, y + 18, picW, picH));
                g.DrawString(items[i].Name, small, Brushes.DimGray, new RectangleF(x, y + 200, picW, 30));
            }
        }
        try
        {
            var folder = Path.Combine(PanelLibrary.ForCurrentUser().Folder, "Panel pictures");
            Directory.CreateDirectory(folder);
            var safe = string.Concat(_title.Text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
            var path = Path.Combine(folder, safe + ".png");
            sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            _status.Text = $"Saved as {Path.GetFileName(path)} in your FIPinator 3000 folder (Panel pictures).";
            Log.Info($"Panel pictures: saved a picture of {_title.Text}.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            _status.Text = $"Couldn't save the picture: {ex.Message}";
            Log.Warn($"Panel pictures: not saved: {ex.Message}");
        }
    }

    private Bitmap Picture(string gaugePath)
    {
        Bitmap image;
        try
        {
            using var renderer = GaugeRenderer.FromFile(gaugePath);
            using var sk = renderer.Render();
            using var data = sk.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());
            using var loaded = new Bitmap(stream);
            image = new Bitmap(loaded);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException or ArgumentException)
        {
            image = new Bitmap(320, 240);
            using var g = Graphics.FromImage(image);
            g.Clear(Color.FromArgb(60, 60, 60));
            using var font = new Font("Segoe UI", 14f);
            g.DrawString("Can't draw this gauge", font, Brushes.White, 20, 100);
            Log.Warn($"Panel pictures: {gaugePath}: {ex.Message}");
        }
        _images.Add(image);
        return image;
    }

    private static string GaugeName(string path)
    {
        try { return GaugeDefinition.Load(path).Name; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException) { return Path.GetFileNameWithoutExtension(path); }
    }

    private static string Short(string serial) => serial.Length > 4 ? serial[^4..] : serial;

    private void FreeImages()
    {
        foreach (var image in _images) image.Dispose();
        _images.Clear();
    }
}
