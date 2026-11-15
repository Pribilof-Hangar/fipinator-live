// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using Fipinator.Panel;

namespace Fipinator.App;

/// <summary>
/// "Set up FIPs": pick the gauge(s) for each FIP and save them as a panel. A FIP can hold several
/// gauges as pages, flipped with the FIP's page up/down buttons. Starts from the panel that's
/// running; FIPs it lists that aren't plugged in are kept. Never overwrites a panel without asking.
/// </summary>
internal sealed class FipSetupForm : Form
{
    private sealed record Item(GaugeChoice? Gauge)
    {
        public override string ToString() => Gauge is null ? "(nothing)" : $"{Gauge.Group}  -  {Gauge.Name}";
    }

    private sealed class FipBlock
    {
        public required string Serial { get; init; }
        public required FlowLayoutPanel Panel { get; init; }
        public List<ComboBox> Pages { get; } = [];
        public required CheckBox LeftKnob { get; init; }
        public required ComboBox AutoFlip { get; init; }
    }

    private sealed record FlipChoice(int Seconds)
    {
        public override string ToString() => Seconds == 0 ? "Off" : $"Every {Seconds} seconds";
    }

    private const int MaxPages = 16;
    private readonly PanelLibrary _library;
    private readonly PanelSettings? _current;
    private readonly List<Item> _items;
    private readonly TextBox _name = new() { Width = 300 };
    private readonly List<FipBlock> _blocks = [];
    private readonly ToolTip _tips = new();
    private readonly Label _note = new() { AutoSize = false, Width = 640, Height = 40, ForeColor = Color.FromArgb(90, 90, 90) };

    /// <summary>The panel file saved, or null if the user cancelled.</summary>
    public string? SavedPath { get; private set; }

    public FipSetupForm(PanelLibrary library, PanelRunner? runner, PanelSettings? current, string currentName)
    {
        _library = library;
        _current = current;
        Text = "FIPinator Live - Set up FIPs";
        Font = new Font("Segoe UI", 10f);
        // Resizable and maximizable (owner, 2026-11-14), so a big screen shows every FIP without scrolling. The
        // window has a fixed size and its content fills it (never both sizing each other: that once made it 0 x 0).
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimumSize = new Size(740, 420);
        var remembered = LiveSettings.Load();
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = remembered.SetupWidth >= 740 && remembered.SetupHeight >= 420
            ? new Size(Math.Min(remembered.SetupWidth, screen.Width), Math.Min(remembered.SetupHeight, screen.Height))
            : new Size(Math.Min(780, screen.Width), Math.Min(820, screen.Height));
        if (remembered.SetupMaximized) WindowState = FormWindowState.Maximized;
        FormClosing += (_, _) =>
        {
            var normal = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size;
            LiveSettings.Save(LiveSettings.Load() with { SetupWidth = normal.Width, SetupHeight = normal.Height, SetupMaximized = WindowState == FormWindowState.Maximized });
        };

        _items = [new(null), .. GaugeCatalog.All().Select(g => new Item(g))];

        // Every connected FIP, plus any in the current panel that aren't plugged in right now.
        var connected = runner?.ConnectedSerials ?? [];
        var serials = connected.Concat(current?.Fips.Select(f => f.Serial) ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var folder = Path.GetDirectoryName(library.ActivePath()) ?? library.PanelsFolder;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1 };
        layout.Controls.Add(new Label { Text = "Set up FIPs", Font = new Font("Segoe UI", 15f, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Label
        {
            Text = "Pick the gauge each FIP shows. For more (several gauges on one FIP to flip through, flipping by itself, " +
                   "layouts, arranging FIPs like your desk), use FIPinator 3000: \"Open in FIPinator 3000\" below. " +
                   "\"Show names on the FIPs\" puts each FIP's short name on its screen, so you can tell which is which.",
            AutoSize = false, Width = 660, Height = 84, ForeColor = Color.FromArgb(90, 90, 90),
        });

        if (serials.Count == 0)
            layout.Controls.Add(new Label { Text = "No FIPs found. Check that the Logitech driver is running and the FIPs are plugged in (Check my setup).", AutoSize = false, Width = 640, Height = 44 });

        // The FIP rows sit in a scrolling box once there are many pages.
        // The FIP rows fill the window's spare height and scroll only when they don't fit.
        var list = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, AutoScroll = true };
        foreach (var serial in serials)
        {
            var plugged = connected.Contains(serial, StringComparer.OrdinalIgnoreCase);
            var assigned = current?.Fips.FirstOrDefault(f => f.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase));
            var knob = new CheckBox
            {
                Text = "Left knob flips pages (good for engine gauges)", AutoSize = true, Checked = assigned?.LeftKnobPages ?? false,
                Margin = new Padding(198, 0, 0, 0),
            };
            _tips.SetToolTip(knob, "Turn this FIP's left knob to flip through its pages. The left knob then doesn't set things on the gauge.");
            var autoFlip = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
            var flipChoices = PageFlipTimer.Choices.ToList();
            var saved = assigned?.AutoFlipSeconds ?? 0;
            if (saved > 0 && !flipChoices.Contains(saved)) flipChoices.Add(Math.Clamp(saved, PageFlipTimer.MinSeconds, PageFlipTimer.MaxSeconds)); // set by hand in the panel file
            autoFlip.Items.AddRange([.. flipChoices.Distinct().Order().Select(s => new FlipChoice(s))]);
            autoFlip.SelectedItem = autoFlip.Items.OfType<FlipChoice>().FirstOrDefault(c => c.Seconds == (saved <= 0 ? 0 : Math.Clamp(saved, PageFlipTimer.MinSeconds, PageFlipTimer.MaxSeconds)));
            _tips.SetToolTip(autoFlip, "Show this FIP's next page by itself, every few seconds. Off by default. Turning the left knob (if ticked) holds the page for 30 seconds.");
            var block = new FipBlock
            {
                Serial = serial, LeftKnob = knob, AutoFlip = autoFlip,
                Panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 4, 0, 0) },
            };
            var paths = assigned is null ? [] : PanelSettings.PagePaths(assigned, folder);
            var title = $"FIP ...{(serial.Length > 4 ? serial[^4..] : serial)}" + (plugged ? "" : " (not plugged in)");
            // Live sets one gauge per FIP (owner, 2026-11-14); pages, flipping and layouts are set in FIPinator 3000
            // and kept as they are when this window saves.
            AddPage(block, title, paths.Count > 0 ? paths[0] : null, removable: false);
            _blocks.Add(block);
            list.Controls.Add(block.Panel);
            var extras = new List<string>();
            if (paths.Count > 1) extras.Add($"{paths.Count - 1} more page(s)");
            if (assigned?.LeftKnobPages == true) extras.Add("left knob flips pages");
            if (assigned?.AutoFlipSeconds > 0) extras.Add($"flips every {assigned.AutoFlipSeconds} s");
            if (current?.Layouts.Count > 0) extras.Add($"{current.Layouts.Count + 1} layouts");
            list.Controls.Add(new Label
            {
                Text = extras.Count > 0 ? $"Also: {string.Join(", ", extras)} (set in FIPinator 3000, kept as they are)" : "",
                AutoSize = true, ForeColor = Color.FromArgb(110, 110, 110), Margin = new Padding(198, 0, 0, extras.Count > 0 ? 10 : 4),
            });
        }
        layout.Controls.Add(list);

        var nameRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 14, 0, 0) };
        nameRow.Controls.Add(new Label { Text = "Save as panel:", AutoSize = false, Width = 190, Height = 26, Margin = new Padding(0, 4, 8, 0) });
        _name.Text = currentName;
        nameRow.Controls.Add(_name);
        layout.Controls.Add(nameRow);
        layout.Controls.Add(_note);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        var names = new Button { Text = "Show names on the FIPs", AutoSize = true };
        names.Click += (_, _) =>
        {
            if (runner is null) { _note.ForeColor = Theme.Wait; _note.Text = "The FIPs aren't running right now, so they can't show their names."; }
            else runner.StartTest(TimeSpan.FromSeconds(6));
        };
        var save = new Button { Text = "Save and use", AutoSize = true, Tag = "accent" };
        save.Click += (_, _) => Save();
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var open3000 = new Button { Text = "Open in FIPinator 3000", AutoSize = true };
        _tips.SetToolTip(open3000, "Opens this panel in FIPinator 3000's \"Set up your panel\": drag gauges between FIPs like on your desk, layouts, and more.");
        open3000.Click += (_, _) =>
        {
            var named = Path.Combine(_library.PanelsFolder, _name.Text.Trim() + ".json");
            var problem = GaugeMakerLink.Open(this, File.Exists(named) ? named : _library.ActivePath());
            if (problem is not null) { _note.ForeColor = Theme.Bad; _note.Text = problem; }
        };
        buttons.Controls.Add(names);
        buttons.Controls.Add(open3000);
        buttons.Controls.Add(new Label { Width = 40 });
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        // Every row its natural height, except the FIP list, which takes the rest.
        for (var row = 0; row < layout.Controls.Count; row++)
            layout.RowStyles.Add(ReferenceEquals(layout.Controls[row], list) ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        CancelButton = cancel;
        Controls.Add(layout);
        Theme.Apply(this);
    }

    /// <summary>Adds one page row (a gauge list) to a FIP's block.</summary>
    private void AddPage(FipBlock block, string? title, string? selectedPath, bool removable)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        var pageNumber = block.Pages.Count + 1;
        row.Controls.Add(new Label
        {
            Text = title ?? $"      page {pageNumber}", AutoSize = false, Width = 190, Height = 26, Margin = new Padding(0, 4, 8, 0),
            ForeColor = title is null ? Color.FromArgb(90, 90, 90) : SystemColors.ControlText,
        });
        var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400, MaxDropDownItems = 24 };
        choice.Items.AddRange([.. _items]);
        var match = selectedPath is null ? -1 : _items.FindIndex(i => i.Gauge is not null && string.Equals(Path.GetFullPath(i.Gauge.Path), selectedPath, StringComparison.OrdinalIgnoreCase));
        if (match < 0 && selectedPath is not null && File.Exists(selectedPath))
        {
            // A gauge from somewhere else (e.g. a project folder): keep it as a choice.
            _items.Add(new Item(new GaugeChoice(SafeName(selectedPath), "This panel", selectedPath)));
            choice.Items.Add(_items[^1]);
            match = _items.Count - 1;
        }
        choice.SelectedIndex = Math.Max(0, match);
        row.Controls.Add(choice);

        if (removable)
        {
            var remove = new Button { Text = "Remove", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                block.Pages.Remove(choice);
                block.Panel.Controls.Remove(row);
                Renumber(block);
            };
            row.Controls.Add(remove);
        }
        // No "+ Add page" here: more pages are added in FIPinator 3000 (owner, 2026-11-14).
        block.Pages.Add(choice);
        block.Panel.Controls.Add(row);
    }

    private static void Renumber(FipBlock block)
    {
        for (var i = 1; i < block.Panel.Controls.Count; i++)
            if (block.Panel.Controls[i].Controls[0] is Label label) label.Text = $"      page {i + 1}";
    }

    private static string SafeName(string path)
    {
        try { return GaugeDefinition.Load(path).Name; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { return Path.GetFileNameWithoutExtension(path); }
    }

    private void Save()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _note.ForeColor = Theme.Bad;
            _note.Text = "Give the panel a name without \\ / : * ? \" < > |";
            return;
        }
        var fips = new List<FipAssignment>();
        foreach (var block in _blocks)
        {
            var gauges = block.Pages.Select(c => (c.SelectedItem as Item)?.Gauge?.Path).Where(p => p is not null).Select(p => p!).ToList();
            if (gauges.Count == 0) continue;
            var before = _current?.Fips.FirstOrDefault(f => f.Serial.Equals(block.Serial, StringComparison.OrdinalIgnoreCase));
            fips.Add(new FipAssignment
            {
                Serial = block.Serial, Gauge = gauges[0],
                Pages = before?.Pages.ToList() ?? [], LeftKnobPages = before?.LeftKnobPages ?? false, AutoFlipSeconds = before?.AutoFlipSeconds ?? 0,
            });
        }
        if (fips.Count == 0)
        {
            _note.ForeColor = Theme.Bad;
            _note.Text = "Pick a gauge for at least one FIP.";
            return;
        }

        var path = Path.Combine(_library.PanelsFolder, name + ".json");
        if (File.Exists(path) && MessageBox.Show(this, $"There's already a panel called \"{name}\". Replace it with this one?", "Save panel",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        try
        {
            // Keep what this window doesn't show (made in FIPinator 3000): desk positions, layouts, the layout button.
            PanelSettings? old = null;
            try { if (File.Exists(path)) old = PanelSettings.Load(path); }
            catch (InvalidDataException) { }
            fips = [.. fips.Select(f => old?.Fips.FirstOrDefault(o => o.Serial.Equals(f.Serial, StringComparison.OrdinalIgnoreCase)) is { } o
                ? f with { DeskX = o.DeskX, DeskY = o.DeskY } : f)];
            var kept = old is null ? new PanelSettings() : old with { Fips = [] };
            var serials = fips.Select(f => f.Serial).ToHashSet(StringComparer.OrdinalIgnoreCase);
            (kept with
            {
                Name = name,
                Fips = fips,
                Layouts = [.. kept.Layouts.Select(l => l with { Fips = [.. l.Fips.Where(f => serials.Contains(f.Serial))] })],
                LayoutButton = kept.LayoutButton is { } b && serials.Contains(b.Serial) ? b : null,
            }).Save(path);
            SavedPath = path;
            Log.Info($"FIP setup: saved panel {name}: {fips.Count} FIP(s), {fips.Sum(f => f.Pages.Count + 1)} gauge(s).");
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _note.ForeColor = Theme.Bad;
            _note.Text = $"Couldn't save: {ex.Message}";
            Log.Error($"FIP setup save failed: {ex}");
        }
    }
}
