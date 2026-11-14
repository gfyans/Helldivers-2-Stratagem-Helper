using System.Diagnostics;

namespace StratagemHelper;

/// <summary>Tabbed settings window. Changes apply immediately; the main window persists them.</summary>
internal sealed class SettingsForm : Form
{
    private const int W = 560, Pad = 18, Inner = W - Pad * 2;

    private readonly Settings _settings;

    // General
    private readonly HdToggle _enabledBox = new("Enabled");
    private readonly HdToggle _autoBox = new("Ctrl+C scans loadout from the in-game menu");
    private readonly HdToggle _focusBox = new("Only when Helldivers 2 is focused");
    private readonly HdToggle _overlayBox = new("Show loadout overlay in game (top-left)");
    private readonly HdToggle _startupBox = new("Start with Windows (in the system tray)");
    private readonly HdToggle _soundBox = new("Sound cues when a scan succeeds or fails");
    private readonly HdRadio _wasdRadio = new("WASD");
    private readonly HdRadio _arrowRadio = new("Arrow keys");
    private readonly NumericUpDown _holdNum = new() { Minimum = 10, Maximum = 200, Width = 64 };
    private readonly NumericUpDown _gapNum = new() { Minimum = 10, Maximum = 200, Width = 64 };

    // Controls
    private readonly HdBindBox[] _slotBinds = Enumerable.Range(0, 9).Select(_ => new HdBindBox()).ToArray();
    private readonly HdBindBox _scanBind = new();
    private readonly NumericUpDown _menuDelayNum = new() { Minimum = 20, Maximum = 500, Width = 64 };

    // Voice
    private readonly HdRadio _voiceOff = new("Off");
    private readonly HdRadio _voicePtt = new("Push-to-talk");
    private readonly HdRadio _voiceAlways = new("Always listening");
    private readonly HdBindBox _pttBind = new();
    private readonly NumericUpDown _confNum = new() { Minimum = 30, Maximum = 95, Width = 64 };
    private readonly Label _voiceInfo = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, MaximumSize = new Size(Inner, 0) };

    // Stratagems
    private readonly Label _countLabel = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted };

    private HdBindBox? _capturingBox;
    private bool _loading = true;

    public event Action? SettingsChanged;
    public event Action<bool>? HotkeysEnabledChanged;
    public event Action? ReloadListRequested;
    public event Action<string, Color?>? Status;
    /// <summary>Ask the main window to capture the next key / mouse button; the callback gets the binding id.</summary>
    public event Action<Action<string>>? CaptureRequested;
    public event Action? CaptureCancelled;

    public SettingsForm(Settings settings, bool enabled, int stratagemCount, Icon icon)
    {
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
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && _capturingBox == null) Close(); };

        BuildUi();

        _enabledBox.Checked = enabled;
        _autoBox.Checked = settings.AutoDetect;
        _focusBox.Checked = settings.OnlyWhenGameFocused;
        _overlayBox.Checked = settings.ShowOverlay;
        _soundBox.Checked = settings.SoundCues;
        _startupBox.Checked = Startup.IsEnabled();
        _wasdRadio.Checked = !settings.UseArrowKeys;
        _arrowRadio.Checked = settings.UseArrowKeys;
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
    }

    public void SetStratagemCount(int n) =>
        _countLabel.Text = $"{n} STRATAGEMS KNOWN  ·  NEW ONES ARE LEARNED AUTOMATICALLY WHEN YOU SCAN";

    public void SetVoiceInfo(string text) => _voiceInfo.Text = text.ToUpperInvariant();

    // ---------------- Layout ----------------

    private static FlowLayoutPanel Page() => new()
    {
        AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        Padding = new Padding(Pad, 14, Pad, 18), Margin = Padding.Empty, BackColor = Theme.Bg,
    };

    private static Label Hint(string text, int top = 6) => new()
    {
        Text = text.ToUpperInvariant(), Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true,
        MaximumSize = new Size(Inner, 0), Margin = new Padding(0, top, 0, 0),
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
            AutoSize = true, Location = new Point(Pad - 3, 8),
        });
        root.Controls.Add(header);
        root.Controls.Add(new HazardStripe { Width = W, Margin = Padding.Empty });

        var tabs = new HdTabs("General", "Controls", "Voice", "Stratagems") { Width = W, Margin = Padding.Empty };
        root.Controls.Add(tabs);

        var pages = new[] { BuildGeneral(), BuildControls(), BuildVoice(), BuildStratagems() };
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
        pages[0].Visible = true;
        tabs.SelectedChanged += i =>
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
        foreach (var t in new HdToggle[] { _enabledBox, _autoBox, _focusBox, _startupBox })
        {
            t.Margin = new Padding(2, 1, 0, 1);
            page.Controls.Add(t);
        }

        // Overlay & sound: each toggle with its explanation / controls indented beneath it (aligned with the toggle text).
        const int Indent = 30;
        page.Controls.Add(new SectionHeader("Overlay & sound") { Width = Inner, Margin = new Padding(0, 16, 0, 4) });
        _overlayBox.Margin = new Padding(2, 1, 0, 0);
        page.Controls.Add(_overlayBox);
        var overlayHint = Hint("Lists your scanned slots in the corner while the game is focused. Ctrl+V hides it until your next scan. Needs borderless or windowed mode.", 0);
        overlayHint.Margin = new Padding(Indent, 0, 0, 8);
        overlayHint.MaximumSize = new Size(Inner - Indent, 0);
        page.Controls.Add(overlayHint);

        _soundBox.Margin = new Padding(2, 1, 0, 2);
        page.Controls.Add(_soundBox);
        var soundRow = Row();
        soundRow.Margin = new Padding(Indent, 0, 0, 0);
        var testOk = new HdButton("▶ Preview success") { Margin = new Padding(0, 0, 8, 0) };
        var testFail = new HdButton("▶ Preview fail") { Margin = Padding.Empty };
        testOk.Click += (_, _) => Sounds.Success();
        testFail.Click += (_, _) => Sounds.Failure();
        soundRow.Controls.Add(testOk);
        soundRow.Controls.Add(testFail);
        page.Controls.Add(soundRow);
        _soundBox.CheckedChanged += (_, _) => Change(() => _settings.SoundCues = _soundBox.Checked);

        page.Controls.Add(new SectionHeader("Input") { Width = Inner, Margin = new Padding(0, 16, 0, 4) });
        var dirRow = Row();
        dirRow.Controls.Add(Theme.MutedLabel("DIRECTIONS", 110));
        _wasdRadio.Margin = new Padding(0, 0, 18, 0);
        _arrowRadio.Margin = new Padding(0);
        dirRow.Controls.Add(_wasdRadio);
        dirRow.Controls.Add(_arrowRadio);
        page.Controls.Add(dirRow);

        var timingRow = Row(4);
        timingRow.Controls.Add(Theme.MutedLabel("KEY HOLD", 110));
        Theme.StyleNumber(_holdNum);
        timingRow.Controls.Add(_holdNum);
        timingRow.Controls.Add(Theme.MutedLabel("MS", 40));
        timingRow.Controls.Add(Theme.MutedLabel("KEY GAP", 80));
        Theme.StyleNumber(_gapNum);
        timingRow.Controls.Add(_gapNum);
        timingRow.Controls.Add(Theme.MutedLabel("MS", 40));
        page.Controls.Add(timingRow);
        page.Controls.Add(Hint("Raise these if the game drops inputs or picks the wrong stratagem."));

        _enabledBox.CheckedChanged += (_, _) => { if (!_loading) HotkeysEnabledChanged?.Invoke(_enabledBox.Checked); };
        _autoBox.CheckedChanged += (_, _) => Change(() => _settings.AutoDetect = _autoBox.Checked);
        _focusBox.CheckedChanged += (_, _) => Change(() => _settings.OnlyWhenGameFocused = _focusBox.Checked);
        _overlayBox.CheckedChanged += (_, _) => Change(() => _settings.ShowOverlay = _overlayBox.Checked);
        _wasdRadio.CheckedChanged += (_, _) => Change(() => _settings.UseArrowKeys = _arrowRadio.Checked);
        _arrowRadio.CheckedChanged += (_, _) => Change(() => _settings.UseArrowKeys = _arrowRadio.Checked);
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
                Status?.Invoke("Couldn't change startup setting: " + ex.Message, Theme.Danger);
            }
        };
        return page;
    }

    private Control BuildControls()
    {
        var page = Page();
        page.Controls.Add(new SectionHeader("One-touch call-ins") { Width = Inner, Margin = new Padding(0, 0, 0, 4) });
        page.Controls.Add(Hint("One press enters the whole code — no need to hold Ctrl. Use the numpad, mouse side buttons, or a USB foot pedal.", 0));

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 10, 0, 0) };
        for (int i = 0; i < 9; i++)
        {
            int col = i < 5 ? 0 : 2, row = i < 5 ? i : i - 5;
            grid.Controls.Add(GridLabel($"SLOT {i + 1}"), col, row);
            _slotBinds[i].Margin = new Padding(0, 2, col == 0 ? 40 : 0, 2);
            grid.Controls.Add(_slotBinds[i], col + 1, row);
            int idx = i;
            Wire(_slotBinds[i], id => { _settings.SlotBindings[idx] = id; });
        }
        grid.Controls.Add(GridLabel("SCAN"), 2, 4);
        _scanBind.Margin = new Padding(0, 2, 0, 2);
        grid.Controls.Add(_scanBind, 3, 4);
        Wire(_scanBind, id => _settings.ScanBinding = id);
        page.Controls.Add(grid);
        page.Controls.Add(Hint("Click a box, then press a key or mouse button. Esc cancels, Backspace clears. Numpad needs Num Lock on."));

        var delayRow = Row(12);
        delayRow.Controls.Add(Theme.MutedLabel("MENU OPEN DELAY", 140));
        Theme.StyleNumber(_menuDelayNum);
        delayRow.Controls.Add(_menuDelayNum);
        delayRow.Controls.Add(Theme.MutedLabel("MS", 40));
        page.Controls.Add(delayRow);
        page.Controls.Add(Hint("Time between the app pressing Ctrl and entering the code. Raise it if one-touch misses the first arrow."));

        page.Controls.Add(new SectionHeader("Always available") { Width = Inner, Margin = new Padding(0, 16, 0, 4) });
        page.Controls.Add(Hint("Hold Ctrl + 1–9  \u2014  call in", 0));
        page.Controls.Add(Hint("Hold Ctrl + C  \u2014  scan your loadout", 2));
        page.Controls.Add(Hint("Hold Ctrl + V  \u2014  hide / show the overlay", 2));

        _menuDelayNum.ValueChanged += (_, _) => Change(() => _settings.MenuOpenDelayMs = (int)_menuDelayNum.Value);
        return page;
    }

    private Control BuildVoice()
    {
        var page = Page();
        page.Controls.Add(new SectionHeader("Voice call-ins") { Width = Inner, Margin = new Padding(0, 0, 0, 4) });
        page.Controls.Add(Hint("Say “call in” followed by a stratagem from your loadout (“call in orbital laser”, “call in resupply”) or a slot (“call in slot three”). Without “call in”, nothing happens. Recognition runs offline in Windows.", 0));

        var modeRow = Row(10);
        modeRow.Controls.Add(Theme.MutedLabel("MODE", 110));
        _voiceOff.Margin = new Padding(0, 0, 14, 0);
        _voicePtt.Margin = new Padding(0, 0, 14, 0);
        _voiceAlways.Margin = new Padding(0);
        modeRow.Controls.Add(_voiceOff);
        modeRow.Controls.Add(_voicePtt);
        modeRow.Controls.Add(_voiceAlways);
        page.Controls.Add(modeRow);

        var pttRow = Row(6);
        pttRow.Controls.Add(Theme.MutedLabel("PUSH-TO-TALK", 110));
        _pttBind.Margin = new Padding(0);
        pttRow.Controls.Add(_pttBind);
        page.Controls.Add(pttRow);
        Wire(_pttBind, id => _settings.PttBinding = id);
        page.Controls.Add(Hint("The push-to-talk key still reaches the game, so it can double as your in-game voice chat key."));

        var confRow = Row(10);
        confRow.Controls.Add(Theme.MutedLabel("MIN. CONFIDENCE", 110));
        Theme.StyleNumber(_confNum);
        confRow.Controls.Add(_confNum);
        confRow.Controls.Add(Theme.MutedLabel("%", 40));
        page.Controls.Add(confRow);
        page.Controls.Add(Hint("Raise it if voice triggers by mistake; lower it if it ignores you."));

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
            Status?.Invoke("Opened stratagems.json — save it, then press Reload list", null);
        };
        var reloadBtn = new HdButton("Reload list") { Margin = new Padding(0) };
        reloadBtn.Click += (_, _) => ReloadListRequested?.Invoke();
        btnRow.Controls.Add(editBtn);
        btnRow.Controls.Add(reloadBtn);
        page.Controls.Add(btnRow);
        page.Controls.Add(Hint("Codes use U, D, L, R for up, down, left, right.", 8));
        return page;
    }

    // ---------------- Binding capture ----------------

    private void Wire(HdBindBox box, Action<string?> save)
    {
        box.Click += (_, _) =>
        {
            if (_capturingBox == box) { CancelCapture(); return; }
            CancelCapture();
            _capturingBox = box;
            box.Capturing = true;
            CaptureRequested?.Invoke(id => BeginInvoke(() =>
            {
                if (_capturingBox != box) return;
                _capturingBox = null;
                box.Capturing = false;
                if (id == Bindings.Escape) return;                       // cancel
                string? value = id is Bindings.Backspace or Bindings.Delete ? null : id;
                if (value != null && !Bindings.IsBindable(value))
                {
                    Status?.Invoke("That key can't be bound", Theme.Danger);
                    return;
                }
                box.Binding = value;
                Change(() => save(value));
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
