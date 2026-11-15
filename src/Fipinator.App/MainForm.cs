// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Diagnostics;
using Fipinator.Core;
using Fipinator.Fip;
using Fipinator.Panel;
using Fipinator.Sim;

namespace Fipinator.App;

/// <summary>
/// The FIPinator window. Starts the panel by itself, shows plainly whether the Logitech
/// software, the sim and each FIP are ready, and stops cleanly when closed.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Color Good = Theme.Good;
    private static readonly Color Bad = Theme.Bad;
    private static readonly Color Wait = Theme.Wait;
    private static readonly Color Plain = Theme.Muted;

    private readonly ComboBox _panel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly PanelLibrary _library = PanelLibrary.ForCurrentUser();
    private CancellationTokenSource _runStop = new();
    private bool _loadingPanels;
    // The three status "lights" at the top: a dot and a short word each; details in the tooltips.
    private readonly Label _fipsDot = Theme.Dot(), _simDot = Theme.Dot(), _logitechDot = Theme.Dot();
    private readonly Label _fipsState = StateLabel(), _sim = StateLabel(), _logitech = StateLabel();
    private readonly ToolTip _tips = new();
    private readonly Button _startService = new() { Text = "Start the FIP service", AutoSize = true, Visible = false, Font = new Font("Segoe UI", 9f) };
    private readonly Button _getLogitech = new() { Text = "Get it", AutoSize = true, Visible = false, Font = new Font("Segoe UI", 9f) };
    private readonly ListView _fips = new() { View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, Height = 196, Scrollable = false, MultiSelect = false, CheckBoxes = true };
    private readonly Button _test = new() { Text = "Test FIPs", AutoSize = true };
    private readonly Button _swap = new() { Text = "Swap the 2 ticked FIPs", AutoSize = true, Font = new Font("Segoe UI", 9f) };
    private readonly Button _reset = new() { Text = "Reset panel to default", AutoSize = true, Font = new Font("Segoe UI", 9f) };
    private readonly Button _setup = new() { Text = "Set up FIPs...", AutoSize = true, Tag = "accent" };
    private readonly Button _pictures = new() { Text = "What's on my FIPs...", AutoSize = true };
    private PanelPicturesForm? _picturesForm;
    private readonly System.Windows.Forms.Timer _testDone = new() { Interval = 6500 };
    private readonly Label _message = new() { AutoSize = false, Height = 40, ForeColor = Plain };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };

    private readonly CancellationTokenSource _stop = new();
    private Task? _panelTask;
    private volatile PanelRunner? _runner;
    private volatile string _problem = "";

    public MainForm()
    {
        Text = "FIPinator Live";
        Font = new Font("Segoe UI", 10f);
        // Resizable (owner, 2026-11-14: texts were cut off); everything stretches with the window and its size is
        // remembered. The default size fits everything.
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(600, 720);
        var remembered = LiveSettings.Load();
        ClientSize = remembered.MainWidth >= 584 && remembered.MainHeight >= 680 ? new Size(remembered.MainWidth, remembered.MainHeight) : new Size(600, 724);
        FormClosing += (_, _) =>
        {
            if (WindowState == FormWindowState.Normal)
                LiveSettings.Save(LiveSettings.Load() with { MainWidth = ClientSize.Width, MainHeight = ClientSize.Height });
        };
        using (var icon = typeof(MainForm).Assembly.GetManifestResourceStream("fipinator.ico"))
            if (icon is not null) Icon = new Icon(icon);

        // "FIPinator" plus an amber "Live", like 3000's "FIPinator 3000".
        var title = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        title.Controls.Add(new Label { Text = "FIPinator", Font = new Font("Segoe UI", 15f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0) });
        title.Controls.Add(new Label { Text = "Live", Font = new Font("Segoe UI", 15f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0), ForeColor = Theme.Accent });
        var hint = new Label { Text = "Leave this open while you fly. Close it to stop.", AutoSize = true, ForeColor = Plain, Margin = new Padding(0, 0, 0, 0) };
        var free = new Label { Text = "FREE and open source: free to use, change, share and fork.", AutoSize = true, ForeColor = Theme.FreeGreen, Font = new Font("Segoe UI", 8.5f), Margin = new Padding(0, 2, 0, 0) };

        _fips.Columns.Add("FIP", 80);
        _fips.Columns.Add("Gauge", 250);
        _fips.Columns.Add("State", 192);
        // The last column fills the rest, so no light strip is left in the header.
        _fips.HandleCreated += (_, _) => _fips.Columns[2].Width = -2;

        // Tools and Help: small buttons with icons (they were plain links, easy to miss on a dark window).
        var checkSetup = Theme.SmallButton("Check my setup", Theme.Icons.Check, (_, _) => ShowSetupCheck());
        var maker = Theme.SmallButton("FIPinator 3000", Theme.Icons.Wrench, (_, _) => OpenGaugeMaker());
        var settings = Theme.SmallButton("Panels folder", Theme.Icons.Folder, (_, _) => OpenFolder(_library.PanelsFolder));
        var report = Theme.SmallButton("Copy problem report", Theme.Icons.Report, (_, _) => CopyProblemReport());
        var github = Theme.SmallButton("Report a problem", Theme.Icons.Report, (_, _) => ReportOnGitHub());
        var logs = Theme.SmallButton("Logs", Theme.Icons.Logs, (_, _) => OpenFolder(Log.Folder));
        var about = Theme.SmallButton("About", Theme.Icons.Info, (_, _) => { using var form = new AboutForm(); form.ShowDialog(this); });
        var tips = _tips;
        tips.SetToolTip(checkSetup, "Checks the Logitech driver, your FIPs and the sim, and says what to do next.");
        tips.SetToolTip(maker, "Opens FIPinator 3000, the gauge maker (optional).");
        tips.SetToolTip(settings, "Your saved panels (which gauge goes on which FIP), one file each. Copy them to back them up or share them.");
        tips.SetToolTip(report, "Copies a problem report to paste into a message. Your user name and FIP serial numbers are hidden.");
        tips.SetToolTip(github, "Opens GitHub's \"New issue\" page, filled in. You read it, add what happened and submit it there. Nothing is sent by FIPinator.");
        tips.SetToolTip(logs, "Technical details for when something goes wrong.");
        Theme.AddIcon(_setup, Theme.Icons.Settings);
        Theme.AddIcon(_test, Theme.Icons.Play);
        Theme.AddIcon(_pictures, Theme.Icons.Photo);
        Theme.AddIcon(_swap, Theme.Icons.Swap, 9f);
        Theme.AddIcon(_reset, Theme.Icons.Reset, 9f);
        foreach (var b in new[] { _setup, _test, _pictures }) b.Padding = new Padding(8, 3, 8, 3);
        _panel.SelectedIndexChanged += (_, _) => OnPanelChosen();
        _panel.DropDown += (_, _) => LoadPanelList(); // pick up panels added while running

        _startService.Click += (_, _) => StartLogitechService();
        _getLogitech.Click += (_, _) => OpenLogitechPage();
        _test.Click += (_, _) => StartTest();
        _testDone.Tick += (_, _) => FinishTest();
        _swap.Click += (_, _) => SwapSelected();
        _reset.Click += (_, _) => ResetPanel();
        _setup.Click += (_, _) => SetUpFips();
        _pictures.Click += (_, _) => ShowPictures();
        // Tick boxes instead of Ctrl-click: tick two FIPs, then Swap. At most two can be ticked.
        _fips.ItemCheck += (_, e) =>
        {
            if (e.NewValue == CheckState.Checked && _fips.CheckedItems.Count >= 2) e.NewValue = CheckState.Unchecked;
        };
        _fips.ItemChecked += (_, _) => UpdateSwapButton();
        _fips.MouseClick += (_, e) =>
        {
            // Clicking anywhere on a row ticks or unticks it (the box itself already does).
            var hit = _fips.HitTest(e.Location);
            if (hit.Item is not null && hit.Location != ListViewHitTestLocations.StateImage) hit.Item.Checked = !hit.Item.Checked;
        };

        const int width = 568; // inside the 16 px border at the default size; rows stretch with the window
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, AutoSize = false };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        const AnchorStyles stretch = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;

        // Title and hint on the left, the panel picker on the right.
        var header = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Width = width, AutoSize = true, Margin = new Padding(0), Anchor = stretch };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var words = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        words.Controls.Add(title);
        words.Controls.Add(hint);
        words.Controls.Add(free);
        header.Controls.Add(words, 0, 0);
        var picker = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        picker.Controls.Add(new Label { Text = "Panel", AutoSize = true, ForeColor = Plain, Font = new Font("Segoe UI", 9f), Margin = new Padding(0) });
        _panel.Margin = new Padding(0);
        picker.Controls.Add(_panel);
        header.Controls.Add(picker, 1, 0);
        layout.Controls.Add(header);

        // The three status lights.
        var cards = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, Width = width, Height = 74, Margin = new Padding(0, 10, 0, 8), Anchor = stretch };
        for (var i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        cards.Controls.Add(StatusCard("FIPs", _fipsDot, _fipsState), 0, 0);
        cards.Controls.Add(StatusCard("Sim", _simDot, _sim), 1, 0);
        cards.Controls.Add(StatusCard("Logitech", _logitechDot, _logitech, _getLogitech, _startService), 2, 0);
        layout.Controls.Add(cards);

        _fips.Width = width;
        _fips.Margin = new Padding(0);
        _fips.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
        _fips.Resize += (_, _) => { if (_fips.Columns.Count == 3) _fips.Columns[2].Width = -2; };
        layout.Controls.Add(_fips);
        var listButtons = Row(null, _swap, _reset);
        listButtons.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(listButtons);
        var main = Row(null, _setup, _test, _pictures);
        main.Margin = new Padding(0, 10, 0, 0);
        layout.Controls.Add(main);
        _message.Width = width;
        _message.Margin = new Padding(0, 8, 0, 0);
        _message.Anchor = stretch;
        layout.Controls.Add(_message);

        // Tools and Help, under a thin line.
        layout.Controls.Add(new Label { AutoSize = false, Width = width, Height = 1, BackColor = Theme.Border, Margin = new Padding(0, 4, 0, 8), Anchor = stretch });
        var more = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Width = width, Height = 140, Margin = new Padding(0), Anchor = stretch };
        more.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        more.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        more.Controls.Add(Group("Tools", checkSetup, maker), 0, 0); // slim Live (owner, 2026-11-14): no Panels folder button
        more.Controls.Add(Group("Help", report, github, logs, about), 1, 0);
        layout.Controls.Add(more);
        // Every row its own height; the FIP list takes the spare height when the window grows.
        for (var row = 0; row < layout.Controls.Count; row++)
            layout.RowStyles.Add(ReferenceEquals(layout.Controls[row], _fips) ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        Theme.Apply(this);

        _refresh.Tick += (_, _) => RefreshStatus();
        Shown += (_, _) =>
        {
            LoadPanelList();
            RefreshStatus();
            _refresh.Start();
            // Sharing the FIPs with SPAD.neXt: the Logitech service is off while Live is closed; turn it on.
            if (FipSharingSetup.IsSetUp && !DirectOutput.IsServiceRunning())
                Task.Run(() => Log.Info(LogitechService.Start(TimeSpan.FromSeconds(20))
                    ? "Sharing with SPAD.neXt: Logitech service started." : "Sharing with SPAD.neXt: Logitech service didn't start."));
            _panelTask = Task.Run(RunPanelLoop);
            // The welcome on first start (and every start if the user asked for that).
            var live = LiveSettings.Load();
            if (!live.WelcomeSeen || live.ShowWelcome)
            {
                LiveSettings.Save(live with { WelcomeSeen = true });
                BeginInvoke(ShowSetupCheck);
            }
        };
        FormClosing += OnClosing;
    }

    private static FlowLayoutPanel Row(string? caption, params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        if (caption is not null) row.Controls.Add(new Label { Text = caption, AutoSize = true, Width = 170, MinimumSize = new Size(170, 0), Margin = new Padding(0, 4, 6, 0) });
        foreach (var c in controls)
        {
            c.Margin = new Padding(0, c is Button ? 0 : 4, 12, 0);
            row.Controls.Add(c);
        }
        return row;
    }

    private static Label StateLabel() => new() { AutoSize = true, Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), Margin = new Padding(0, 2, 6, 0), Text = "—" };

    /// <summary>One status light: a dot and a caption, the state underneath, and any fix-it buttons.</summary>
    private static Control StatusCard(string caption, Label dot, Label state, params Button[] buttons)
    {
        var card = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill,
            BackColor = Theme.Card, Padding = new Padding(10, 8, 6, 6), Margin = new Padding(0, 0, 8, 0),
        };
        var top = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        top.Controls.Add(dot);
        top.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Plain, Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 2, 0, 0) });
        card.Controls.Add(top);
        var bottom = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        bottom.Controls.Add(state);
        foreach (var b in buttons) { b.Margin = new Padding(0, 0, 4, 0); bottom.Controls.Add(b); }
        card.Controls.Add(bottom);
        return card;
    }

    /// <summary>A small caption ("Tools", "Help") over its buttons.</summary>
    private static Control Group(string caption, params Button[] buttons)
    {
        var group = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        group.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Plain, Font = new Font("Segoe UI", 8.5f), Margin = new Padding(0, 0, 0, 4) });
        var row = new FlowLayoutPanel { WrapContents = true, AutoSize = true, MaximumSize = new Size(262, 0), Margin = new Padding(0) };
        foreach (var b in buttons) { b.Margin = new Padding(0, 0, 6, 6); row.Controls.Add(b); }
        group.Controls.Add(row);
        return group;
    }

    private void SetLight(Label dot, Label state, string text, Color color, string tip)
    {
        dot.ForeColor = color;
        state.Text = text;
        state.ForeColor = color == Good ? Theme.Text : color;
        _tips.SetToolTip(state, tip);
        _tips.SetToolTip(dot, tip);
    }

    private sealed record PanelChoice(string Name, string Path)
    {
        public override string ToString() => Name;
    }

    private void LoadPanelList()
    {
        _loadingPanels = true;
        _panel.Items.Clear();
        var active = _library.ActivePath();
        foreach (var (name, path) in _library.List())
        {
            var index = _panel.Items.Add(new PanelChoice(name, path));
            if (string.Equals(path, active, StringComparison.OrdinalIgnoreCase)) _panel.SelectedIndex = index;
        }
        if (_panel.SelectedIndex < 0 && _panel.Items.Count > 0) _panel.SelectedIndex = 0;
        _panel.Enabled = _panel.Items.Count > 0;
        _loadingPanels = false;
    }

    /// <summary>Switch the FIPs to the chosen panel: stop the running one; the loop starts the new one.</summary>
    private void OnPanelChosen()
    {
        if (_loadingPanels || _panel.SelectedItem is not PanelChoice choice) return;
        if (string.Equals(choice.Path, _library.ActivePath(), StringComparison.OrdinalIgnoreCase) && _runner is not null) return;
        _library.SetActive(choice.Path);
        Log.Info($"Panel chosen: {choice.Name}");
        _message.Text = $"Switching to {choice.Name}...";
        _runStop.Cancel();
    }

    /// <summary>Keeps the panel running; if something it needs is missing, waits and tries again.</summary>
    private void RunPanelLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            // A fresh stop signal per run: choosing another panel cancels just this run.
            _runStop.Dispose();
            _runStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            var runToken = _runStop.Token;
            try
            {
                var path = _library.ActivePath();
                if (!File.Exists(path))
                {
                    _runner = null;
                    _problem = "No panel is set up yet, so there's nothing to show on the FIPs.";
                    runToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                    continue;
                }
                var settings = PanelSettings.Load(path);
                var panelKey = Path.GetFileNameWithoutExtension(path);
                var runner = new PanelRunner(settings, Path.GetDirectoryName(path)!) { StartPageFor = serial => LiveSettings.PageFor(panelKey, serial) };
                runner.Report += message => BeginInvokeSafe(() => _message.Text = message);
                runner.PageShown += (serial, page) => LiveSettings.RememberPage(panelKey, serial, page);
                _runner = runner;
                _problem = "";
                runner.Run(runToken);
                _runner = null;
            }
            catch (FipException ex)
            {
                // Logitech software missing or its service stopped: say so, and try again shortly.
                _runner = null;
                _problem = ex.Message;
                Log.Warn($"Panel waiting: {ex.Message} {ex.Details}");
                runToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(3));
            }
            catch (InvalidDataException ex)
            {
                // A settings or gauge file mistake won't fix itself: wait until another panel is chosen.
                _runner = null;
                _problem = ex.Message;
                Log.Error(ex.Message);
                runToken.WaitHandle.WaitOne();
            }
            catch (Exception ex)
            {
                _runner = null;
                _problem = "Something went wrong. Details are in the log file; FIPinator Live will try again.";
                Log.Error($"Panel failed: {ex}");
                runToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            }
        }
    }

    private void RefreshStatus()
    {
        var installed = DirectOutput.FindLibrary() is not null;
        var running = installed && DirectOutput.IsServiceRunning();
        if (!installed) SetLight(_logitechDot, _logitech, "Missing", Bad, "The Logitech FIP software isn't installed. \"Get it\" opens Logitech's download page.");
        else if (!running) SetLight(_logitechDot, _logitech, "Stopped", Bad, "The Logitech FIP software is installed, but its service isn't running. \"Start the FIP service\" starts it (Windows asks for permission).");
        else SetLight(_logitechDot, _logitech, "Ready", Good, "The Logitech FIP software is installed and running.");
        _getLogitech.Visible = !installed;
        _startService.Visible = installed && !running;

        var runner = _runner;
        var connected = runner?.ConnectedSerials.Count ?? 0;
        if (runner is null) SetLight(_fipsDot, _fipsState, "—", Plain, "Waiting for the Logitech software.");
        else if (connected == 0) SetLight(_fipsDot, _fipsState, "None found", Bad, "No FIPs found. Check the USB cables and power, and that SPAD.neXt isn't using them (Check my setup).");
        else SetLight(_fipsDot, _fipsState, $"{connected} connected", Good, $"{connected} FIP(s) connected.");

        if (runner is not null)
        {
            var (state, message) = runner.SimStatus;
            switch (state)
            {
                case SimState.Connected: SetLight(_simDot, _sim, "Connected", Good, "Connected to MSFS 2024."); break;
                case SimState.Failed: SetLight(_simDot, _sim, "Problem", Bad, message); break;
                default: SetLight(_simDot, _sim, "Not running", Wait, message + " That's fine: start the sim when you want to fly."); break;
            }

            var rows = runner.FipStatus;
            _fips.BeginUpdate();
            if (_fips.Items.Count != rows.Count)
            {
                _fips.Items.Clear();
                foreach (var _ in rows) _fips.Items.Add(new ListViewItem(["", "", ""]));
            }
            for (var i = 0; i < rows.Count; i++)
            {
                var item = _fips.Items[i];
                item.Tag = rows[i].Serial;
                item.SubItems[0].Text = "…" + rows[i].Serial[Math.Max(0, rows[i].Serial.Length - 4)..];
                item.SubItems[1].Text = rows[i].Gauge;
                item.SubItems[2].Text = rows[i].State;
                item.ForeColor = rows[i].State == "Showing" ? Theme.Text : rows[i].State.StartsWith("Stopped", StringComparison.Ordinal) ? Bad : Wait;
            }
            _fips.EndUpdate();
        }
        else
        {
            SetLight(_simDot, _sim, "—", Plain, "Waiting for the FIPs to start.");
            _fips.Items.Clear();
        }


        if (_problem.Length > 0) { _message.Text = _problem; _message.ForeColor = Bad; }
        else if (_message.ForeColor == Bad) { _message.Text = ""; _message.ForeColor = Plain; }
    }

    /// <summary>Opens (or brings back) the "What's on my FIPs" pictures; it follows the active panel by itself.</summary>
    private void ShowPictures()
    {
        if (_picturesForm is { IsDisposed: false })
        {
            if (_picturesForm.WindowState == FormWindowState.Minimized) _picturesForm.WindowState = FormWindowState.Normal;
            _picturesForm.Activate();
            return;
        }
        _picturesForm = new PanelPicturesForm(() => _runner, _library.ActivePath);
        _picturesForm.Show(this);
    }

    /// <summary>Every FIP shows its short name for a few seconds; then report which ones answered.</summary>
    private void StartTest()
    {
        // Buttons stay clickable (greyed-out ones are hard to read on the dark window); say why if it can't run.
        if (_runner is not { } runner)
        {
            _message.ForeColor = Wait;
            _message.Text = "The FIPs aren't running yet, so there's nothing to test. \"Check my setup\" shows what's missing.";
            return;
        }
        if (runner.TestRunning) return;
        runner.StartTest(TimeSpan.FromSeconds(6));
        _message.ForeColor = Plain;
        _message.Text = "Testing: each FIP should now show its name (the last 4 of its serial)...";
        _testDone.Start();
    }

    private void FinishTest()
    {
        _testDone.Stop();
        if (_runner is not { } runner) return;
        var expected = runner.FipStatus.Select(f => f.Serial).ToList();
        var answered = runner.TestAnswered;
        var missing = expected.Where(s => !answered.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        var extra = runner.ConnectedSerials.Where(s => !expected.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        var shortName = (string s) => "…" + s[Math.Max(0, s.Length - 4)..];

        var text = missing.Count == 0
            ? $"Test passed: all {expected.Count} FIPs answered."
            : $"{missing.Count} FIP(s) didn't answer: {string.Join(", ", missing.Select(shortName))}. " +
              "Check the USB cable or hub; unplugging and replugging the FIP usually wakes it.";
        if (extra.Count > 0) text += $" ({extra.Count} other FIP(s) are connected but not used by this panel.)";
        _message.ForeColor = missing.Count == 0 ? Good : Bad;
        _message.Text = text;
        Log.Info($"Test result: {text}");
    }

    private void UpdateSwapButton()
    {
        var ticked = _fips.CheckedItems.Count;
        if (_problem.Length > 0) return;
        _message.ForeColor = Plain;
        _message.Text = ticked switch
        {
            1 => "Tick one more FIP to swap with.",
            2 => "Press \"Swap the 2 ticked FIPs\" to swap their gauges.",
            _ => _message.Text.StartsWith("Tick one more", StringComparison.Ordinal) || _message.Text.StartsWith("Press \"Swap", StringComparison.Ordinal) ? "" : _message.Text,
        };
    }

    private void SwapSelected()
    {
        if (_fips.CheckedItems.Count != 2)
        {
            _message.ForeColor = Wait;
            _message.Text = "Tick two FIPs in the list first, then press \"Swap the 2 ticked FIPs\" to swap their gauges.";
            return;
        }
        var a = _fips.CheckedItems[0].Tag as string;
        var b = _fips.CheckedItems[1].Tag as string;
        if (a is null || b is null) return;
        try
        {
            _library.Swap(_library.ActivePath(), a, b);
            _message.ForeColor = Plain;
            _message.Text = "Swapped. The FIPs switch over in a moment; this layout is saved.";
            _runStop.Cancel(); // restart the panel with the new layout
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _message.ForeColor = Bad;
            _message.Text = $"Couldn't swap: {ex.Message}";
            Log.Error($"Swap failed: {ex}");
        }
    }

    private void ResetPanel()
    {
        var name = (_panel.SelectedItem as PanelChoice)?.Name ?? "this panel";
        if (!_library.HasDefault(_library.ActivePath()))
        {
            _message.ForeColor = Wait;
            _message.Text = $"{name} has no original layout saved, so there's nothing to go back to.";
            return;
        }
        if (MessageBox.Show($"Put {name} back to its original layout?", "Reset panel to default",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            _library.ResetToDefault(_library.ActivePath());
            _message.ForeColor = Plain;
            _message.Text = $"{name} is back to its original layout.";
            _runStop.Cancel();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _message.ForeColor = Bad;
            _message.Text = $"Couldn't reset: {ex.Message}";
            Log.Error($"Reset failed: {ex}");
        }
    }

    private void StartLogitechService()
    {
        // Sharing with SPAD.neXt is set up: no Windows prompt needed.
        if (FipSharingSetup.IsSetUp && LogitechService.Start(TimeSpan.FromSeconds(20))) { RefreshStatus(); return; }
        try
        {
            // Starting a Windows service needs administrator permission; Windows asks the user.
            using var p = Process.Start(new ProcessStartInfo("sc.exe", $"start {DirectOutput.ServiceName}")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            p?.WaitForExit(15000);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The user said no to the permission prompt.
        }
        RefreshStatus();
    }

    /// <summary>Opens Logitech's official Flight Instrument Panel driver page in the web browser.</summary>
    private void OpenLogitechPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = DirectOutput.DownloadPage, UseShellExecute = true })?.Dispose();
            _message.ForeColor = Plain;
            _message.Text = "On Logitech's page, download the Windows (64-bit) driver, run it, then come back: FIPinator Live starts by itself once it's installed.";
            Log.Info("Opened the Logitech FIP driver page.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"Couldn't open the Logitech page: {ex}");
            _message.ForeColor = Bad;
            _message.Text = $"Couldn't open the web browser. The Logitech page is: {DirectOutput.DownloadPage}";
        }
    }

    /// <summary>Opens "Set up FIPs" for the running panel; on save, switches to the saved panel.</summary>
    private void SetUpFips()
    {
        PanelSettings? current = null;
        var active = _library.ActivePath();
        try { if (File.Exists(active)) current = PanelSettings.Load(active); }
        catch (InvalidDataException ex) { Log.Warn($"FIP setup: current panel unreadable: {ex.Message}"); }
        var name = (_panel.SelectedItem as PanelChoice)?.Name ?? "My panel";
        using var form = new FipSetupForm(_library, _runner, current, name);
        if (form.ShowDialog(this) != DialogResult.OK || form.SavedPath is null) return;
        _library.SetActive(form.SavedPath);
        LoadPanelList();
        _message.ForeColor = Good;
        _message.Text = "Saved. The FIPs switch over in a moment.";
        _runStop.Cancel(); // restart with the saved panel
    }

    /// <summary>Puts a problem report (personal details hidden) on the clipboard, ready to paste into a message.</summary>
    private void CopyProblemReport()
    {
        try
        {
            var name = (_panel.SelectedItem as PanelChoice)?.Name ?? "(none)";
            Clipboard.SetText(ProblemReport.Build(_runner, name));
            _message.ForeColor = Good;
            _message.Text = "Problem report copied. Paste it into an email or message (Ctrl+V). Your user name and FIP serial numbers are hidden.";
            Log.Info("Problem report copied to the clipboard.");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            _message.ForeColor = Bad;
            _message.Text = "Couldn't copy to the clipboard. Try again in a moment.";
            Log.Warn($"Problem report copy failed: {ex.Message}");
        }
    }

    /// <summary>Opens GitHub's "New issue" page filled in; the user reads it, adds what happened and submits it.</summary>
    private void ReportOnGitHub()
    {
        var name = (_panel.SelectedItem as PanelChoice)?.Name ?? "(none)";
        AboutForm.Open(ProblemReport.IssueUrl(_runner, name));
        _message.ForeColor = Plain;
        _message.Text = "GitHub's \"New issue\" page is opening in your browser, already filled in (your user name and FIP serial numbers are hidden). Add what happened, then submit it there.";
        Log.Info("Report a problem: GitHub issue page opened.");
    }

    private SetupCheckForm? _setupCheck;

    /// <summary>The "Check my setup" window (the first-start welcome). Only one at a time.</summary>
    private void ShowSetupCheck()
    {
        if (_setupCheck is { IsDisposed: false }) { _setupCheck.Activate(); return; }
        _setupCheck = new SetupCheckForm(() => _runner, StartLogitechService, OpenLogitechPage, LiveSettings.Load().ShowWelcome);
        _setupCheck.Show(this);
    }

    /// <summary>Starts FIPinator 3000, the gauge maker, from where it's installed (or says it isn't).</summary>
    private void OpenGaugeMaker()
    {
        // Not installed: a short window saying what 3000 can do and where to get it.
        if (GaugeMakerLink.Open(this, null) is { } problem) { _message.ForeColor = Bad; _message.Text = problem; }
    }

    private void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            // Let Windows open the folder the standard way (same as double-clicking it).
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true, Verb = "open" })?.Dispose();
            Log.Info($"Opened folder: {folder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"Couldn't open folder {folder}: {ex}");
            _message.ForeColor = Bad;
            _message.Text = $"Couldn't open the folder. You can open it by hand: {folder}";
        }
    }

    /// <summary>
    /// Sharing the FIPs with SPAD.neXt: turns the Logitech service off so SPAD.neXt can draw again, and says
    /// the one thing left to do (SPAD.neXt redraws a FIP when its page button is pressed; tested 2026-11-10).
    /// </summary>
    private void HandFipsToSpad()
    {
        if (!FipSharingSetup.IsSetUp) return;
        var stopped = LogitechService.Stop(TimeSpan.FromSeconds(15));
        Log.Info(stopped ? "Sharing with SPAD.neXt: Logitech service stopped." : "Sharing with SPAD.neXt: Logitech service didn't stop.");
        var settings = LiveSettings.Load();
        if (settings.HandoverNoteHidden) return;
        using var note = new Form
        {
            Text = "FIPinator Live", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(440, 170), Font = Font, ShowInTaskbar = true,
        };
        var text = new Label
        {
            AutoSize = false, Location = new Point(18, 16), Size = new Size(404, 80),
            Text = stopped
                ? "Your FIPs are back with SPAD.neXt.\n\nPress the page button on each FIP once to show your SPAD.neXt gauges (or restart SPAD.neXt)."
                : "The FIPs couldn't be handed back to SPAD.neXt (Logitech's service didn't stop). Restarting the PC fixes it.",
        };
        var hide = new CheckBox { Text = "Don't show this again", AutoSize = true, Location = new Point(18, 126) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(338, 120), Size = new Size(84, 30), Tag = "accent" };
        note.Controls.AddRange([text, hide, ok]);
        note.AcceptButton = ok;
        Theme.Apply(note);
        note.ShowDialog();
        if (hide.Checked) LiveSettings.Save(LiveSettings.Load() with { HandoverNoteHidden = true });
    }

    private void BeginInvokeSafe(Action action)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        _refresh.Stop();
        _stop.Cancel();
        // Give the panel a moment to take its pages off the FIPs and disconnect cleanly.
        _panelTask?.Wait(TimeSpan.FromSeconds(6));
        HandFipsToSpad();
        Log.Info("App closed.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refresh.Dispose();
            _testDone.Dispose();
            _runStop.Dispose();
            _stop.Dispose();
        }
        base.Dispose(disposing);
    }
}
