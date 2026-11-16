using System.Diagnostics;

namespace StratagemHelper;

/// <summary>
/// Tabbed settings window. Changes apply straight away; the main window saves them.
/// Keyboard: Tab moves between controls, Ctrl+Tab / Ctrl+Shift+Tab switch tabs, arrow keys pick options, Esc closes.
/// </summary>
internal sealed class SettingsForm : Form
{
    private const int W = 560, Pad = 18, Inner = W - Pad * 2, Indent = 32;

    private readonly Settings _settings;
    private HdTabs _tabs = null!;

    // General
    private readonly HdToggle _enabledBox = new("Hotkeys on");
    private readonly HdToggle _startupBox = new("Start with Windows");
    private readonly HdToggle _overlayBox = new("Show my loadout in the corner of the game");
    private readonly HdToggle _soundBox = new("Play a sound when a scan works or fails");
    private readonly TrackBar _opacitySlider = new()
    {
        Minimum = 20, Maximum = 100, SmallChange = 5, LargeChange = 10, TickFrequency = 10,
        Width = 240, AutoSize = false, Height = 30, TickStyle = TickStyle.None,
        BackColor = Theme.Bg, AccessibleName = "Overlay opacity",
    };
    private readonly Label _opacityValue = new() { AutoSize = false, Size = new Size(56, 26), Font = Theme.Body, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft };
    private readonly HdRadio[] _sizeRadios = { new("100%"), new("125%"), new("150%"), new("200%") };
    private static readonly int[] Sizes = { 100, 125, 150, 200 };
    private readonly HdButton _restartBtn = new("Restart now") { Visible = false };
    private readonly HdBindBox[] _dirBinds = Bindings.DirectionNames.Select(n => new HdBindBox { Label = $"Stratagem {n.ToLowerInvariant()} key", Width = 104 }).ToArray();
    private readonly HdButton _wasdBtn = new(Bindings.WasdName);
    private readonly HdButton _arrowBtn = new("Arrow keys");
    private readonly NumericUpDown _holdNum = new() { Minimum = 10, Maximum = 200, Width = 64 };
    private readonly NumericUpDown _gapNum = new() { Minimum = 10, Maximum = 200, Width = 64 };

    // Controls
    private readonly HdBindBox[] _slotBinds = Enumerable.Range(0, 9).Select(i => new HdBindBox { Label = $"Slot {i + 1} key" }).ToArray();
    private readonly HdBindBox _scanBind = new() { Label = "Scan key" };
    private readonly NumericUpDown _menuDelayNum = new() { Minimum = 20, Maximum = 500, Width = 64 };

    // Voice
    private readonly HdRadio _voiceOff = new("Off");
    private readonly HdRadio _voicePtt = new("Push-to-talk");
    private readonly HdRadio _voiceAlways = new("Always listening");
    private readonly HdBindBox _pttBind = new() { Label = "Push-to-talk key" };
    private readonly NumericUpDown _confNum = new() { Minimum = 30, Maximum = 95, Width = 64 };
    private readonly Label _voiceInfo = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, MaximumSize = new Size(Inner, 0) };

    // Stratagems
    private readonly Label _countLabel = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, MaximumSize = new Size(Inner, 0) };

    // Updates
    private readonly HdToggle _autoUpdateBox = new("Update automatically");
    private readonly Label _updateInfo = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, MaximumSize = new Size(Inner, 0) };

    private HdBindBox? _capturingBox;
    private bool _loading = true;

    public event Action? SettingsChanged;
    public event Action<bool>? HotkeysEnabledChanged;
    public event Action? ReloadListRequested;
    public event Action? CheckUpdatesRequested;
    public event Action? RestartRequested;
    public event Action<string, Color?>? Status;
    /// <summary>Ask the main window to capture the next key / mouse button; the callback gets the binding id.</summary>
    public event Action<Action<string, string?>>? CaptureRequested;
    public event Action? CaptureCancelled;

    public SettingsForm(Settings settings, bool enabled, int stratagemCount, Icon icon)
    {
        Theme.PrepareForm(this);
        _settings = settings;
        Text = "Stratagem Helper — Settings";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Icon = icon;

        BuildUi();

        _enabledBox.Checked = enabled;
        _overlayBox.Checked = settings.ShowOverlay;
        _soundBox.Checked = settings.SoundCues;
        _opacitySlider.Value = Math.Clamp(settings.OverlayOpacity, 20, 100);
        _opacityValue.Text = $"{_opacitySlider.Value}%";
        _autoUpdateBox.Checked = settings.AutoUpdate;
        _startupBox.Checked = Startup.IsEnabled();
        int sizeIdx = Array.IndexOf(Sizes, settings.TextSize);
        _sizeRadios[sizeIdx < 0 ? 0 : sizeIdx].Checked = true;
        ShowDirections();
        _holdNum.Value = Math.Clamp(settings.KeyHoldMs, 10, 200);
        _gapNum.Value = Math.Clamp(settings.KeyGapMs, 10, 200);
        for (int i = 0; i < 9; i++) _slotBinds[i].Binding = settings.SlotBindings.ElementAtOrDefault(i);
        _scanBind.Binding = settings.ScanBinding;
        _menuDelayNum.Value = Math.Clamp(settings.MenuOpenDelayMs, 20, 500);
        _voiceOff.Checked = settings.VoiceMode == 0;
        _voicePtt.Checked = settings.VoiceMode == 1;
        _voiceAlways.Checked = settings.VoiceMode == 2;
        _pttBind.Binding = settings.PttBinding;
        _confNum.Value = Math.Clamp(settings.VoiceConfidence, 30, 95);
        SetStratagemCount(stratagemCount);
        _loading = false;

        Theme.ApplyTextScale(this);
    }

    public void SetStratagemCount(int n) =>
        _countLabel.Text = $"I know {n} stratagems. New ones get picked up when you scan.";

    public void SetVoiceInfo(string text) => _voiceInfo.Text = text;

    public void SetUpdateInfo(string text) => _updateInfo.Text = text;

    /// <summary>Esc closes; Ctrl+Tab / Ctrl+Shift+Tab switch tabs (handled here because Tab never reaches KeyDown).</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_capturingBox == null) // otherwise the next key belongs to the key being set
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData is (Keys.Control | Keys.Tab) or (Keys.Control | Keys.Shift | Keys.Tab))
            {
                _tabs.Selected += (keyData & Keys.Shift) != 0 ? -1 : 1;
                _tabs.Focus();
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------- Layout ----------------

    private static FlowLayoutPanel Page() => new()
    {
        AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        Padding = new Padding(Pad, 14, Pad, 18), Margin = Padding.Empty, BackColor = Theme.Bg,
    };

    /// <summary>A short, plain explanation in sentence case.</summary>
    private static Label Hint(string text, int top = 4, int indent = 0) => new()
    {
        Text = text, Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true,
        MaximumSize = new Size(Inner - indent, 0), Margin = new Padding(indent, top, 0, 0),
    };

    /// <summary>Label sized like an HdBindBox (28 px + 2 px margins) so grid rows line up.</summary>
    private static Label GridLabel(string text)
    {
        var l = Theme.MutedLabel(text, 70);
        l.Height = 28;
        l.Margin = new Padding(0, 2, 0, 2);
        return l;
    }

    private static FlowLayoutPanel Row(int top = 0) => new() { AutoSize = true, Margin = new Padding(0, top, 0, 0), WrapContents = false };

    private void BuildUi()
    {
        var root = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = Theme.Bg, Margin = Padding.Empty, Padding = Padding.Empty,
        };

        var header = new Panel { Size = new Size(W, 52), BackColor = Theme.Bg, Margin = Padding.Empty };
        header.Controls.Add(new Label
        {
            Text = "SETTINGS", Font = Theme.Title, ForeColor = Theme.Yellow,
            AutoSize = true, Location = new Point(Pad - 3, 8), AccessibleName = "Settings",
        });
        root.Controls.Add(header);
        root.Controls.Add(new HazardStripe { Width = W, Margin = Padding.Empty });

        _tabs = new HdTabs("General", "Controls", "Voice", "Stratagems", "Updates") { Width = W, Margin = Padding.Empty };
        root.Controls.Add(_tabs);

        var pages = new[] { BuildGeneral(), BuildControls(), BuildVoice(), BuildStratagems(), BuildUpdates() };
        // Fixed-size host sized to the tallest page so the window doesn't jump between tabs.
        var host = new Panel { Margin = Padding.Empty, BackColor = Theme.Bg };
        int maxH = 0;
        foreach (var p in pages)
        {
            maxH = Math.Max(maxH, p.GetPreferredSize(new Size(W, 0)).Height);
            p.Location = Point.Empty;
            p.Visible = false;
            host.Controls.Add(p);
        }
        host.Size = new Size(W, maxH);
        // Re-measure once scaling (text size / display scaling) has been applied, so there's no empty space at the bottom.
        Load += (_, _) => host.Height = pages.Max(p => p.GetPreferredSize(new Size(host.Width, 0)).Height);
        pages[0].Visible = true;
        _tabs.SelectedChanged += i =>
        {
            CancelCapture();
            for (int k = 0; k < pages.Length; k++) pages[k].Visible = k == i;
        };
        root.Controls.Add(host);
        Controls.Add(root);
    }

    private Control BuildGeneral()
    {
        var page = Page();
        page.Controls.Add(new SectionHeader("General") { Width = Inner, Margin = new Padding(0, 0, 0, 4) });
        _enabledBox.Margin = new Padding(2, 1, 0, 0);
        page.Controls.Add(_enabledBox);
        page.Controls.Add(Hint("Turn this off to pause everything without closing the app.", 0, Indent));
        _startupBox.Margin = new Padding(2, 6, 0, 0);
        page.Controls.Add(_startupBox);
        page.Controls.Add(Hint("It waits in the system tray until you play.", 0, Indent));

        page.Controls.Add(new SectionHeader("Overlay & sound") { Width = Inner, Margin = new Padding(0, 16, 0, 4) });
        _overlayBox.Margin = new Padding(2, 1, 0, 0);
        page.Controls.Add(_overlayBox);
        page.Controls.Add(Hint($"Press {Bindings.Ctrl} + V in game to hide it. Needs borderless or windowed mode.", 0, Indent));

        // Opacity slider: arrow keys / Page Up / Page Down work too; screen readers announce it as "Overlay opacity".
        var opacityRow = Row(4);
        opacityRow.Margin = new Padding(Indent, 4, 0, 0);
        opacityRow.Controls.Add(Theme.MutedLabel("OPACITY", 78));
        _opacitySlider.Margin = new Padding(0);
        opacityRow.Controls.Add(_opacitySlider);
        _opacityValue.Margin = new Padding(6, 0, 0, 0);
        opacityRow.Controls.Add(_opacityValue);
        page.Controls.Add(opacityRow);
        _opacitySlider.ValueChanged += (_, _) =>
        {
            _opacityValue.Text = $"{_opacitySlider.Value}%";
            Change(() => _settings.OverlayOpacity = _opacitySlider.Value);
        };

        _soundBox.Margin = new Padding(2, 6, 0, 2);
        page.Controls.Add(_soundBox);
        var soundRow = Row();
        soundRow.Margin = new Padding(Indent, 0, 0, 0);
        var testOk = new HdButton("Hear success", icon: ButtonIcon.Play) { Margin = new Padding(0, 0, 8, 0), AccessibleName = "Play the success sound" };
        var testFail = new HdButton("Hear fail", icon: ButtonIcon.Play) { Margin = Padding.Empty, AccessibleName = "Play the fail sound" };
        testOk.Click += (_, _) => Sounds.Success();
        testFail.Click += (_, _) => Sounds.Failure();
        soundRow.Controls.Add(testOk);
        soundRow.Controls.Add(testFail);
        page.Controls.Add(soundRow);

        page.Controls.Add(new SectionHeader("Text size") { Width = Inner, Margin = new Padding(0, 16, 0, 4) });
        var sizeRow = Row();
        foreach (var (r, i) in _sizeRadios.Select((r, i) => (r, i)))
        {
            r.Margin = new Padding(i == 0 ? 2 : 0, 0, 14, 0);
            r.AccessibleName = $"Text size {Sizes[i]} percent";
            sizeRow.Controls.Add(r);
            int size = Sizes[i];
            r.CheckedChanged += (_, _) =>
            {
                if (_loading || !r.Checked) return;
                _settings.TextSize = size;
                SettingsChanged?.Invoke();
                _restartBtn.Visible = size != (int)Math.Round(Theme.TextScale * 100);
            };
        }
        _restartBtn.Margin = new Padding(6, 0, 0, 0);
        _restartBtn.Click += (_, _) => RestartRequested?.Invoke();
        sizeRow.Controls.Add(_restartBtn);
        page.Controls.Add(sizeRow);
        page.Controls.Add(Hint("Changes after a restart. Windows display scaling works too.", 2));

        page.Controls.Add(new SectionHeader("Input") { Width = Inner, Margin = new Padding(0, 16, 0, 4) });
        page.Controls.Add(Hint("The keys the game uses for stratagem directions. Change them if you've changed them in the game's controls.", 0));
        // Up / Down on the first row, Left / Right on the second.
        var dirGrid = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 6, 0, 0), Padding = Padding.Empty };
        for (int i = 0; i < 4; i++)
        {
            int col = i % 2 == 0 ? 0 : 2, row = i / 2;
            // Same columns as the key hold / key gap row below.
            var label = GridLabel(Bindings.DirectionNames[i].ToUpperInvariant());
            label.Width = col == 0 ? 110 : 80;
            if (col > 0) label.Margin = new Padding(6, 2, 0, 2);
            dirGrid.Controls.Add(label, col, row);
            _dirBinds[i].Margin = new Padding(0, 2, 0, 2);
            dirGrid.Controls.Add(_dirBinds[i], col + 1, row);
            WireDirection(_dirBinds[i], i);
        }
        page.Controls.Add(dirGrid);
        var presetRow = Row(4);
        var resetLabel = Theme.MutedLabel("RESET TO", 110);
        resetLabel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left; // centred on the buttons
        presetRow.Controls.Add(resetLabel);
        _wasdBtn.Margin = new Padding(0, 0, 10, 0);
        _arrowBtn.Margin = Padding.Empty;
        _wasdBtn.AccessibleName = $"Use {Bindings.WasdName} for stratagem directions";
        _arrowBtn.AccessibleName = "Use the arrow keys for stratagem directions";
        _wasdBtn.Click += (_, _) => SetDirections(Bindings.WasdDirections);
        _arrowBtn.Click += (_, _) => SetDirections(Bindings.ArrowDirections);
        presetRow.Controls.Add(_wasdBtn);
        presetRow.Controls.Add(_arrowBtn);
        page.Controls.Add(presetRow);

        var timingRow = Row(4);
        timingRow.Controls.Add(Theme.MutedLabel("KEY HOLD", 110));
        Theme.StyleNumber(_holdNum, "Key hold, in milliseconds");
        timingRow.Controls.Add(_holdNum);
        timingRow.Controls.Add(Theme.MutedLabel("MS", 40));
        timingRow.Controls.Add(Theme.MutedLabel("KEY GAP", 80));
        Theme.StyleNumber(_gapNum, "Gap between keys, in milliseconds");
        timingRow.Controls.Add(_gapNum);
        timingRow.Controls.Add(Theme.MutedLabel("MS", 40));
        page.Controls.Add(timingRow);
        page.Controls.Add(Hint("If the game misses inputs or picks the wrong stratagem, turn these up a little."));

        _enabledBox.CheckedChanged += (_, _) => { if (!_loading) HotkeysEnabledChanged?.Invoke(_enabledBox.Checked); };
        _overlayBox.CheckedChanged += (_, _) => Change(() => _settings.ShowOverlay = _overlayBox.Checked);
        _soundBox.CheckedChanged += (_, _) => Change(() => _settings.SoundCues = _soundBox.Checked);
        _holdNum.ValueChanged += (_, _) => Change(() => _settings.KeyHoldMs = (int)_holdNum.Value);
        _gapNum.ValueChanged += (_, _) => Change(() => _settings.KeyGapMs = (int)_gapNum.Value);
        _startupBox.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            try
            {
                Startup.SetEnabled(_startupBox.Checked);
                Status?.Invoke(_startupBox.Checked ? "Will start with Windows" : "Won't start with Windows", null);
            }
            catch (Exception ex)
            {
                Status?.Invoke("Couldn't change that: " + ex.Message, Theme.Danger);
            }
        };
        return page;
    }

    private Control BuildControls()
    {
        var page = Page();
        page.Controls.Add(new SectionHeader("One-touch call-ins") { Width = Inner, Margin = new Padding(0, 0, 0, 4) });
        page.Controls.Add(Hint($"One press puts in the whole code, no {Bindings.Ctrl} needed. Use the numpad, a mouse side button or a foot pedal.", 0));

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 10, 0, 0) };
        // Tab order runs down the first column (slots 1-5), then the second (6-9, scan).
        int tab = 0;
        for (int i = 0; i < 9; i++)
        {
            int col = i < 5 ? 0 : 2, row = i < 5 ? i : i - 5;
            grid.Controls.Add(GridLabel($"SLOT {i + 1}"), col, row);
            _slotBinds[i].Margin = new Padding(0, 2, col == 0 ? 40 : 0, 2);
            _slotBinds[i].TabIndex = tab++;
            grid.Controls.Add(_slotBinds[i], col + 1, row);
            int idx = i;
            Wire(_slotBinds[i], id => { _settings.SlotBindings[idx] = id; });
        }
        grid.Controls.Add(GridLabel("SCAN"), 2, 4);
        _scanBind.Margin = new Padding(0, 2, 0, 2);
        _scanBind.TabIndex = tab;
        grid.Controls.Add(_scanBind, 3, 4);
        Wire(_scanBind, id => _settings.ScanBinding = id);
        page.Controls.Add(grid);
        page.Controls.Add(Hint("Click a box (or press Enter on it), then press the key you want. Esc cancels, Backspace clears. Numpad keys need Num Lock on."));

        var delayRow = Row(12);
        delayRow.Controls.Add(Theme.MutedLabel("MENU DELAY", 110));
        Theme.StyleNumber(_menuDelayNum, "Menu delay, in milliseconds");
        delayRow.Controls.Add(_menuDelayNum);
        delayRow.Controls.Add(Theme.MutedLabel("MS", 40));
        page.Controls.Add(delayRow);
        page.Controls.Add(Hint("If one-touch misses the first arrow, turn this up."));

        page.Controls.Add(new SectionHeader("Always available") { Width = Inner, Margin = new Padding(0, 16, 0, 4) });
        page.Controls.Add(Hint($"Hold {Bindings.Ctrl} + 1–9 to call in", 0));
        page.Controls.Add(Hint($"Hold {Bindings.Ctrl} + C to scan your loadout", 2));
        page.Controls.Add(Hint($"{Bindings.Ctrl} + V hides or shows the overlay", 2));

        _menuDelayNum.ValueChanged += (_, _) => Change(() => _settings.MenuOpenDelayMs = (int)_menuDelayNum.Value);
        return page;
    }

    private Control BuildVoice()
    {
        var page = Page();
        page.Controls.Add(new SectionHeader("Voice call-ins") { Width = Inner, Margin = new Padding(0, 0, 0, 4) });
        page.Controls.Add(Hint("Say “call in” and then a stratagem, like “call in orbital laser”, or a slot, like “call in slot three”.", 0));

        var modeRow = Row(10);
        modeRow.Controls.Add(Theme.MutedLabel("MODE", 110));
        _voiceOff.Margin = new Padding(0, 0, 14, 0);
        _voicePtt.Margin = new Padding(0, 0, 14, 0);
        _voiceAlways.Margin = new Padding(0);
        _voiceOff.AccessibleName = "Voice off";
        _voicePtt.AccessibleName = "Voice on push-to-talk";
        _voiceAlways.AccessibleName = "Voice always listening";
        modeRow.Controls.Add(_voiceOff);
        modeRow.Controls.Add(_voicePtt);
        modeRow.Controls.Add(_voiceAlways);
        page.Controls.Add(modeRow);

        var pttRow = Row(6);
        pttRow.Controls.Add(Theme.MutedLabel("TALK KEY", 110));
        _pttBind.Margin = new Padding(0);
        pttRow.Controls.Add(_pttBind);
        page.Controls.Add(pttRow);
        Wire(_pttBind, id => _settings.PttBinding = id);
        page.Controls.Add(Hint("Your talk key still works in the game, so you can use your voice chat key."));

        var confRow = Row(10);
        confRow.Controls.Add(Theme.MutedLabel("STRICTNESS", 110));
        Theme.StyleNumber(_confNum, "Strictness, in percent");
        confRow.Controls.Add(_confNum);
        confRow.Controls.Add(Theme.MutedLabel("%", 40));
        page.Controls.Add(confRow);
        page.Controls.Add(Hint("Turn it up if voice goes off by accident, or down if it keeps missing you."));

        page.Controls.Add(new SectionHeader("Status") { Width = Inner, Margin = new Padding(0, 16, 0, 4) });
        _voiceInfo.Margin = new Padding(0);
        page.Controls.Add(_voiceInfo);

        void Mode(int m) { if (!_loading) Change(() => _settings.VoiceMode = m); }
        _voiceOff.CheckedChanged += (_, _) => { if (_voiceOff.Checked) Mode(0); };
        _voicePtt.CheckedChanged += (_, _) => { if (_voicePtt.Checked) Mode(1); };
        _voiceAlways.CheckedChanged += (_, _) => { if (_voiceAlways.Checked) Mode(2); };
        _confNum.ValueChanged += (_, _) => Change(() => _settings.VoiceConfidence = (int)_confNum.Value);
        return page;
    }

    private Control BuildStratagems()
    {
        var page = Page();
        page.Controls.Add(new SectionHeader("Stratagems") { Width = Inner, Margin = new Padding(0, 0, 0, 4) });
        _countLabel.Margin = new Padding(0, 2, 0, 8);
        page.Controls.Add(_countLabel);

        var btnRow = Row();
        var editBtn = new HdButton("Edit list") { Margin = new Padding(0, 0, 10, 0) };
        editBtn.Click += (_, _) =>
        {
            Process.Start(new ProcessStartInfo(Storage.StratagemsPath) { UseShellExecute = true });
            Status?.Invoke("Save your changes, then press Reload list", null);
        };
        var reloadBtn = new HdButton("Reload list") { Margin = new Padding(0) };
        reloadBtn.Click += (_, _) => ReloadListRequested?.Invoke();
        btnRow.Controls.Add(editBtn);
        btnRow.Controls.Add(reloadBtn);
        page.Controls.Add(btnRow);
        page.Controls.Add(Hint("In the list, codes use U, D, L and R for up, down, left and right.", 8));
        return page;
    }

    private Control BuildUpdates()
    {
        var page = Page();
        page.Controls.Add(new SectionHeader("Version") { Width = Inner, Margin = new Padding(0, 0, 0, 6) });
        page.Controls.Add(new Label
        {
            Text = $"Stratagem Helper {Updater.Current}", Font = Theme.Heading, ForeColor = Theme.Text,
            AutoSize = true, Margin = new Padding(0, 0, 0, 4),
        });
        _updateInfo.Margin = new Padding(0, 0, 0, 10);
        page.Controls.Add(_updateInfo);

        var btnRow = Row();
        var checkBtn = new HdButton("Check now") { Margin = new Padding(0, 0, 10, 0), AccessibleName = "Check for updates now" };
        checkBtn.Click += (_, _) => CheckUpdatesRequested?.Invoke();
        var releasesBtn = new HdButton("What's new") { Margin = Padding.Empty, AccessibleName = "Open the releases page on GitHub" };
        releasesBtn.Click += (_, _) => Updater.OpenReleasesPage();
        btnRow.Controls.Add(checkBtn);
        btnRow.Controls.Add(releasesBtn);
        page.Controls.Add(btnRow);

        page.Controls.Add(new SectionHeader("Automatic updates") { Width = Inner, Margin = new Padding(0, 18, 0, 4) });
        _autoUpdateBox.Margin = new Padding(2, 1, 0, 0);
        page.Controls.Add(_autoUpdateBox);
        page.Controls.Add(Hint("Updates install themselves when you're not playing.", 0, Indent));

        _autoUpdateBox.CheckedChanged += (_, _) => Change(() => _settings.AutoUpdate = _autoUpdateBox.Checked);
        return page;
    }

    // ---------------- Binding capture ----------------

    private void Wire(HdBindBox box, Action<string?> save) => StartCaptureOnClick(box, (id, _) =>
    {
        string? value = id is Bindings.Backspace or Bindings.Delete ? null : id;
        if (value != null && !Bindings.IsBindable(value))
        {
            Status?.Invoke("That key can't be used", Theme.Danger);
            return;
        }
        box.Binding = value;
        Change(() => save(value));
    });

    /// <summary>
    /// A stratagem direction: stored as the physical key, since that's what gets sent to the game.
    /// Backspace / Delete put back the default for that direction (a direction can't be empty).
    /// </summary>
    private void WireDirection(HdBindBox box, int dir) => StartCaptureOnClick(box, (id, physical) =>
    {
        string? value = id is Bindings.Backspace or Bindings.Delete ? Bindings.WasdDirections[dir] : physical;
        if (value == null || !Bindings.IsBindable(id) || !Bindings.TryGetScan(value, out _, out _))
        {
            Status?.Invoke("That can't be used for stratagem directions", Theme.Danger);
            return;
        }
        var keys = (_settings.DirectionKeys ?? Bindings.WasdDirections).ToArray();
        keys[dir] = value;
        SetDirections(keys);
    });

    private void SetDirections(string[] keys)
    {
        Change(() => _settings.DirectionKeys = keys.ToArray());
        ShowDirections();
    }

    private void ShowDirections()
    {
        var keys = _settings.DirectionKeys ?? Bindings.WasdDirections;
        for (int i = 0; i < 4; i++) _dirBinds[i].Binding = keys.ElementAtOrDefault(i);
    }

    private void StartCaptureOnClick(HdBindBox box, Action<string, string?> captured)
    {
        box.Click += (_, _) =>
        {
            if (_capturingBox == box) { CancelCapture(); return; }
            CancelCapture();
            _capturingBox = box;
            box.Capturing = true;
            box.Focus();
            CaptureRequested?.Invoke((id, physical) => BeginInvoke(() =>
            {
                if (_capturingBox != box) return;
                _capturingBox = null;
                box.Capturing = false;
                if (id == Bindings.Escape) return; // cancel
                captured(id, physical);
            }));
        };
    }

    private void CancelCapture()
    {
        if (_capturingBox == null) return;
        _capturingBox.Capturing = false;
        _capturingBox = null;
        CaptureCancelled?.Invoke();
    }

    private void Change(Action apply)
    {
        if (_loading) return;
        apply();
        SettingsChanged?.Invoke();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CancelCapture();
        base.OnFormClosing(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(this);
    }
}
