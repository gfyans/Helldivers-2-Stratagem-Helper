using System.Globalization;

namespace StratagemHelper;

public sealed class MainForm : Form
{
    private const string None = "(none)";

    private List<Stratagem> _stratagems;
    private Dictionary<string, Stratagem> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Settings _settings;
    private readonly Stats _stats = Stats.Load();
    private readonly KeyboardHook _hook;
    private readonly Voice _voice = new();
    private readonly LoadoutOverlay _overlay = new();

    // Snapshot of slot -> code, read from worker threads.
    private volatile string?[] _slotCodes = new string?[10];

    // Menu auto-detection
    private volatile Task? _detectTask;

    // In-game loadout panel
    private readonly System.Windows.Forms.Timer _overlayTimer = new() { Interval = 400 };
    private bool _overlayHidden; // Ctrl+V: temporary, cleared by the next successful scan

    private readonly HdComboBox[] _combos = new HdComboBox[9];
    private readonly ArrowCode[] _codeLabels = new ArrowCode[9];
    private readonly KeyBadge[] _badges = new KeyBadge[9];
    private readonly Label[] _statNumbers = new Label[3];
    private readonly Label[] _statCaptions = new Label[3];
    private readonly Label _statusDot = new() { AutoSize = true, Text = "■", ForeColor = Theme.Amber, Font = Theme.Body };
    private readonly Label _stateLabel = new() { AutoSize = true, ForeColor = Theme.Text, Text = "", Font = Theme.Heading };
    private readonly Label _status = new() { AutoSize = false, AutoEllipsis = true, ForeColor = Theme.Muted, Text = "", Font = Theme.Small, TextAlign = ContentAlignment.MiddleRight };
    private readonly System.Windows.Forms.Timer _gameTimer = new() { Interval = 2000 };
    private bool _gameRunning;
    private readonly NotifyIcon _tray;
    private readonly Icon _icon = Theme.MakeIcon();
    private SettingsForm? _settingsForm;

    // Updates
    private readonly HdButton _updateBtn = new("Update", primary: true) { Visible = false };
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 15_000 }; // first check soon after start, then every 12 h
    private Updater.Release? _available;
    private string? _downloaded;
    private bool _checking;
    private string _updateInfo = "";
    private readonly bool _justUpdated;
    private string _voiceInfo = "VOICE IS OFF";

    private bool _startHidden;
    private bool _exiting;
    private bool _loading;

    /// <summary>Started by Windows with --tray: stay hidden (tray icon only) until the user opens it.</summary>
    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden)
        {
            _startHidden = false;
            if (!IsHandleCreated) CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    public MainForm(bool startInTray = false, bool justUpdated = false)
    {
        Theme.PrepareForm(this);
        _startHidden = startInTray;
        _justUpdated = justUpdated;
        Text = "Stratagem Helper";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(0);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Icon = _icon;

        _stratagems = Storage.LoadStratagems();
        RebuildIndex();
        _settings = Storage.LoadSettings();
        Startup.RefreshPath(); // keep the startup entry pointing here if the app moved

        _hook = new KeyboardHook
        {
            // While a menu read is in progress, claim every digit: the slot may be about to change.
            ShouldHandle = slot => _slotCodes[slot] != null || _detectTask is { IsCompleted: false },
            SlotTriggered = OnSlotTriggered,
            DetectRequested = OnDetectRequested,
            OverlayToggleRequested = ToggleOverlay,
            CtrlDown = OnCtrlDown,
            CtrlUp = OnCtrlUp,
        };
        _voice.Heard += OnVoiceHeard;

        BuildUi();
        ApplySlotsToUi();
        RefreshSlotCodes();
        ApplyBindings();
        UpdateStats();

        var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()), ForeColor = Theme.Text, BackColor = Theme.Panel, ShowImageMargin = false };
        menu.Items.Add("SHOW", null, (_, _) => RestoreFromTray());
        menu.Items.Add("SETTINGS", null, (_, _) => { RestoreFromTray(); OpenSettings(); });
        menu.Items.Add("EXIT", null, (_, _) => { _exiting = true; _tray!.Visible = false; Application.Exit(); });
        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "Stratagem Helper",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        CheckGame();
        _gameTimer.Start();
        _overlayTimer.Tick += (_, _) => UpdateOverlay();
        _overlay.SetOpacity(_settings.OverlayOpacity);
        _overlayTimer.Start();
        _updateTimer.Tick += async (_, _) => { _updateTimer.Interval = 12 * 60 * 60 * 1000; await CheckForUpdatesAsync(manual: false); };
        if (_settings.AutoUpdate) _updateTimer.Start();
        _updateInfo = "Not checked yet.";
        if (_justUpdated) SetStatus($"Updated to version {Updater.Current}");
        ApplyVoiceMode();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        Theme.ApplyTextScale(this);
    }

    private void RebuildIndex()
    {
        _byName = _stratagems
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private void BuildUi()
    {
        const int W = 600;      // window content width
        const int Pad = 18;     // side padding
        const int Inner = W - Pad * 2;

        var root = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = Theme.Bg, Margin = Padding.Empty, Padding = Padding.Empty,
        };

        // ---- Header ----
        var header = new Panel { Size = new Size(W, 78), BackColor = Theme.Bg, Margin = Padding.Empty };
        header.Controls.Add(new Label
        {
            Text = "STRATAGEM HELPER", Font = Theme.Title, ForeColor = Theme.Yellow,
            AutoSize = true, Location = new Point(Pad - 3, 10),
        });
        header.Controls.Add(new Label
        {
            Text = "SCAN  ·  CTRL + C      CALL IN  ·  CTRL + 1–9      OVERLAY  ·  CTRL + V",
            Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(Pad, 50),
        });
        var settingsBtn = new HdButton("⚙  Settings");
        settingsBtn.Click += (_, _) => OpenSettings();
        header.Controls.Add(settingsBtn);
        _updateBtn.Click += (_, _) => OnUpdateButton();
        header.Controls.Add(_updateBtn);
        header.Layout += (_, _) =>
        {
            // Positions follow the header's actual (scaled) size, so they hold at any text size / display scaling.
            float k = header.Height / 78f;
            settingsBtn.Location = new Point(header.ClientSize.Width - (int)(Pad * k) - settingsBtn.Width, (int)(16 * k));
            _updateBtn.Location = new Point(settingsBtn.Left - (int)(8 * k) - _updateBtn.Width, settingsBtn.Top);
        };
        root.Controls.Add(header);
        root.Controls.Add(new HazardStripe { Width = W, Margin = Padding.Empty });

        // ---- Loadout ----
        var body = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(Pad, 14, Pad, 14), Margin = Padding.Empty, BackColor = Theme.Bg,
        };

        body.Controls.Add(new SectionHeader("Loadout") { Width = Inner, Margin = new Padding(0, 0, 0, 6) });

        for (int i = 0; i < 9; i++)
        {
            var row = new TableLayoutPanel
            {
                Size = new Size(Inner, 40), BackColor = Theme.Panel, ColumnCount = 3, RowCount = 1,
                Margin = new Padding(0, 0, 0, 3), Padding = new Padding(5, 4, 8, 4),
                // An explicit name stops Windows labelling the row with whatever text came before it.
                AccessibleName = $"Slot {i + 1}", AccessibleRole = AccessibleRole.Grouping,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _badges[i] = new KeyBadge((i + 1).ToString()) { Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            row.Controls.Add(_badges[i], 0, 0);

            var combo = new HdComboBox { Width = 290, Anchor = AnchorStyles.Left, Margin = new Padding(0), AccessibleName = $"Slot {i + 1} stratagem" };
            int idx = i;
            combo.SelectedIndexChanged += (_, _) => OnSlotChanged(idx);
            _combos[i] = combo;
            row.Controls.Add(combo, 1, 0);

            _codeLabels[i] = new ArrowCode
            {
                Size = new Size(200, 22), Anchor = AnchorStyles.Left,
                BackColor = Theme.Panel, Margin = new Padding(8, 0, 0, 0),
            };
            row.Controls.Add(_codeLabels[i], 2, 0);
            body.Controls.Add(row);
        }
        FillComboItems();

        // ---- Strain saved ----
        body.Controls.Add(new SectionHeader("Keypresses saved") { Width = Inner, Margin = new Padding(0, 14, 0, 6) });
        var tiles = new TableLayoutPanel { Size = new Size(Inner, 78), ColumnCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
        for (int i = 0; i < 3; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        string[] titles = { "TODAY", "LAST 7 DAYS", "ALL TIME" };
        for (int i = 0; i < 3; i++)
        {
            var tile = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = new Padding(i == 0 ? 0 : 3, 0, i == 2 ? 0 : 3, 0) };
            tile.Controls.Add(new Label { Text = titles[i], Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(10, 6), BackColor = Theme.Panel });
            _statNumbers[i] = new Label
            {
                Text = "0", Font = new Font(Theme.Title.FontFamily, 15f * Theme.TextScale, FontStyle.Bold), ForeColor = Theme.Yellow,
                AutoSize = true, Location = new Point(8, 22), BackColor = Theme.Panel,
            };
            _statCaptions[i] = new Label { Text = "", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(10, 54), BackColor = Theme.Panel };
            tile.Controls.Add(_statNumbers[i]);
            tile.Controls.Add(_statCaptions[i]);
            tiles.Controls.Add(tile, i, 0);
        }
        body.Controls.Add(tiles);
        root.Controls.Add(body);

        // ---- Status bar ----
        var footer = new Panel { Size = new Size(W, 30), BackColor = Theme.Panel, Margin = Padding.Empty };
        footer.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };
        _statusDot.BackColor = Theme.Panel;
        _statusDot.Location = new Point(Pad - 2, 5);
        _stateLabel.BackColor = Theme.Panel;
        _stateLabel.Location = new Point(Pad + 16, 6);
        _status.BackColor = Theme.Panel;
        _status.Bounds = new Rectangle(Pad + 230, 5, W - Pad * 2 - 230, 20);
        footer.Controls.Add(_statusDot);
        footer.Controls.Add(_stateLabel);
        footer.Controls.Add(_status);
        root.Controls.Add(footer);

        _gameTimer.Tick += (_, _) => CheckGame();

        Controls.Add(root);
    }

    // ---------------- Settings window ----------------

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings, _hook.Enabled, _stratagems.Count, _icon);
        _settingsForm.SetVoiceInfo(_voiceInfo);
        _settingsForm.SettingsChanged += () =>
        {
            UpdateOverlay();
            ApplyBindings();
            ApplyVoiceMode();
            _overlay.SetOpacity(_settings.OverlayOpacity);
            if (_settings.AutoUpdate && !_updateTimer.Enabled) _updateTimer.Start();
            if (!_settings.AutoUpdate) _updateTimer.Stop();
            Storage.SaveSettings(_settings);
        };
        _settingsForm.HotkeysEnabledChanged += on =>
        {
            _hook.Enabled = on;
            UpdateState();
            SetStatus(on ? "Hotkeys on" : "Hotkeys off");
        };
        _settingsForm.ReloadListRequested += ReloadStratagemList;
        _settingsForm.Status += (text, color) => SetStatus(text, color);
        _settingsForm.SetUpdateInfo(_updateInfo);
        _settingsForm.CheckUpdatesRequested += async () => await CheckForUpdatesAsync(manual: true);
        _settingsForm.RestartRequested += RestartApp;
        _settingsForm.CaptureRequested += done => _hook.Capture = done;
        _settingsForm.CaptureCancelled += () => _hook.Capture = null;
        _settingsForm.FormClosed += (_, _) => _hook.Capture = null;
        _settingsForm.Show(this);
    }

    private void ReloadStratagemList()
    {
        _stratagems = Storage.LoadStratagems();
        RebuildIndex();
        FillComboItems();
        ApplySlotsToUi();
        RefreshSlotCodes();
        _settingsForm?.SetStratagemCount(_stratagems.Count);
        SetStatus("Stratagem list reloaded");
    }

    // ---------------- Updates ----------------

    /// <summary>
    /// Checks GitHub for a newer release. The release exe downloads + verifies it, then installs straight away
    /// if Helldivers 2 isn't running, or as soon as it closes. Source builds just show the update button.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_checking || _downloaded != null) { if (manual && _downloaded != null) InstallUpdate(); return; }
        _checking = true;
        try
        {
            SetUpdateInfo("Checking…");
            var r = await Updater.CheckAsync();
            if (r == null)
            {
                SetUpdateInfo("You're on the latest version.");
                if (manual) SetStatus("You're on the latest version");
                return;
            }

            _available = r;
            if (!Updater.CanSelfUpdate)
            {
                SetUpdateInfo($"Version {r.Version} is out. You can grab it from GitHub.");
                ShowUpdateButton($"⬆ v{r.Version} available");
                SetStatus($"Version {r.Version} is out");
                return;
            }

            SetUpdateInfo($"Downloading version {r.Version}…");
            _downloaded = await Updater.DownloadAsync(r);
            ShowUpdateButton($"⬆ Update to v{r.Version}");
            if (_gameRunning)
            {
                SetUpdateInfo($"Version {r.Version} is ready. It'll install when you close Helldivers 2.");
                SetStatus($"Update ready. It'll install when you close the game");
            }
            else InstallUpdate();
        }
        catch
        {
            SetUpdateInfo("Couldn't check for updates just now.");
            if (manual) SetStatus("Couldn't check for updates just now", Theme.Danger);
        }
        finally { _checking = false; }
    }

    private void OnUpdateButton()
    {
        if (_downloaded != null) InstallUpdate();
        else Updater.OpenReleasesPage(_available?.PageUrl);
    }

    /// <summary>Swaps in the downloaded exe and restarts (back to the tray if the window was hidden).</summary>
    private void InstallUpdate()
    {
        if (_downloaded == null) return;
        try
        {
            Updater.InstallAndRestart(_downloaded, startHidden: !Visible);
            _exiting = true;
            _tray.Visible = false;
            Application.Exit();
        }
        catch
        {
            _downloaded = null;
            SetUpdateInfo("The update didn't install. You can download it from GitHub instead.");
            SetStatus("The update didn't install", Theme.Danger);
            ShowUpdateButton($"⬆ v{_available?.Version} on GitHub");
        }
    }

    /// <summary>Restarts the app (used to apply a new text size). Comes back to the same place it was.</summary>
    private void RestartApp()
    {
        Storage.SaveSettings(_settings);
        Program.Restart(startHidden: !Visible);
        _exiting = true;
        _tray.Visible = false;
        Application.Exit();
    }

    private void ShowUpdateButton(string text)
    {
        _updateBtn.Text = text.ToUpperInvariant();
        _updateBtn.Visible = true;
        _updateBtn.Parent?.PerformLayout();
    }

    private void SetUpdateInfo(string text)
    {
        _updateInfo = text;
        _settingsForm?.SetUpdateInfo(text);
    }

    // ---------------- Loadout slots ----------------

    private void FillComboItems()
    {
        var names = new object[] { None }.Concat(_stratagems.Select(s => s.Name).OrderBy(n => n)).ToArray();
        foreach (var c in _combos)
        {
            c.BeginUpdate();
            c.Items.Clear();
            c.Items.AddRange(names);
            c.EndUpdate();
        }
    }

    private void ApplySlotsToUi()
    {
        _loading = true;
        for (int i = 0; i < 9; i++)
        {
            var name = _settings.Slots[i];
            int idx = name != null && _byName.TryGetValue(name, out var s) ? _combos[i].Items.IndexOf(s.Name) : 0;
            _combos[i].SelectedIndex = idx < 0 ? 0 : idx;
            UpdateCodeLabel(i);
        }
        _loading = false;
    }

    private void OnSlotChanged(int i)
    {
        if (_loading) return;
        var sel = _combos[i].SelectedItem as string;
        _settings.Slots[i] = sel == None ? null! : sel!;
        UpdateCodeLabel(i);
        RefreshSlotCodes();
        Storage.SaveSettings(_settings);
    }

    private void UpdateCodeLabel(int i)
    {
        var name = _settings.Slots[i];
        bool has = name != null && _byName.TryGetValue(name, out var s) && !string.IsNullOrEmpty(s.Code);
        _codeLabels[i].Code = has ? _byName[name!].Code : "";
        _badges[i].Active = has;
        _badges[i].Invalidate();
    }

    private void RefreshSlotCodes()
    {
        var codes = new string?[10];
        for (int i = 0; i < 9; i++)
        {
            var name = _settings.Slots[i];
            if (name != null && _byName.TryGetValue(name, out var s) && !string.IsNullOrWhiteSpace(s.Code))
                codes[i + 1] = s.Code;
        }
        _slotCodes = codes;
        _overlay.SetRows(_settings.Slots);
        _voice.SetPhrases(Voice.PhrasesFor(_settings.Slots));
        if (_voice.Running) UpdateVoiceInfo(null);
    }

    // ---------------- Call-ins ----------------

    /// <summary>Ctrl+1-9: the user is already holding Ctrl.</summary>
    private void OnSlotTriggered(int slot)
    {
        WaitForScan();
        var code = _slotCodes[slot];
        if (code == null) return;
        if (InputSender.SendSequence(code, _settings.UseArrowKeys, _settings.KeyHoldMs, _settings.KeyGapMs))
            Called(slot, code, Stats.Via.CtrlNumber);
    }

    /// <summary>One-touch key or voice: the app presses Ctrl itself (unless the user is already holding it).</summary>
    private void OneTouch(int slot, Stats.Via via)
    {
        WaitForScan();
        var code = _slotCodes[slot];
        if (code == null)
        {
            BeginInvoke(() => SetStatus($"Slot {slot} is empty", Theme.Danger));
            return;
        }
        int delay = CtrlHeld() ? -1 : _settings.MenuOpenDelayMs;
        if (InputSender.SendSequence(code, _settings.UseArrowKeys, _settings.KeyHoldMs, _settings.KeyGapMs, delay))
            Called(slot, code, via);
    }

    private void Called(int slot, string code, Stats.Via via)
    {
        int saved = _stats.Record(code, via);
        BeginInvoke(() =>
        {
            SetStatus($"Called in {MenuReader.StripDesignation(_settings.Slots[slot - 1] ?? "")}");
            UpdateStats();
        });
    }

    private void WaitForScan()
    {
        // If the menu is being read right now, wait for it so we use the current loadout.
        var pending = _detectTask;
        if (pending is { IsCompleted: false })
        {
            try { pending.Wait(2500); } catch { }
        }
    }

    /// <summary>Builds the one-touch binding table for the hook from settings.</summary>
    private void ApplyBindings()
    {
        var map = new Dictionary<string, Action>();
        for (int i = 0; i < 9; i++)
        {
            var id = _settings.SlotBindings.ElementAtOrDefault(i);
            int slot = i + 1;
            if (!string.IsNullOrEmpty(id)) map[id] = () => Task.Run(() => OneTouch(slot, Stats.Via.OneTouch));
        }
        if (!string.IsNullOrEmpty(_settings.ScanBinding)) map[_settings.ScanBinding] = StartOneTouchScan;
        _hook.Bindings = map;
        _hook.PttBinding = _settings.VoiceMode == 1 ? _settings.PttBinding : null;
    }

    // ---------------- Voice ----------------

    private void ApplyVoiceMode()
    {
        _hook.PttBinding = _settings.VoiceMode == 1 ? _settings.PttBinding : null;
        if (_settings.VoiceMode == 0)
        {
            _voice.Stop();
            UpdateVoiceInfo("Voice is off.");
            return;
        }
        if (_voice.Running) { UpdateVoiceInfo(null); return; }
        var error = _voice.Start(Voice.PhrasesFor(_settings.Slots));
        UpdateVoiceInfo(error);
        if (error != null) SetStatus(error, Theme.Danger);
    }

    private void UpdateVoiceInfo(string? message)
    {
        if (message == null)
        {
            var names = _settings.Slots.Where(n => !string.IsNullOrEmpty(n)).Select(n => Voice.Spoken(n!)).Distinct();
            string mode = _settings.VoiceMode == 1
                ? (string.IsNullOrEmpty(_settings.PttBinding) ? "Pick a talk key above to start." : $"Hold {Bindings.Display(_settings.PttBinding)} and speak.")
                : "Listening all the time.";
            message = $"{mode} You can say “call in” and then: {string.Join(", ", names)}, or a slot number.";
        }
        _voiceInfo = message;
        _settingsForm?.SetVoiceInfo(message);
    }

    private void OnVoiceHeard(int slot, string phrase, float confidence)
    {
        int pct = (int)(confidence * 100);
        if (pct < _settings.VoiceConfidence)
        {
            BeginInvoke(() => SetStatus($"Didn't quite catch that (sounded like “{phrase}”)"));
            return;
        }
        // Push-to-talk: accept speech while the key is held or just after release (recognition finishes after you stop talking).
        if (_settings.VoiceMode == 1 && !_hook.PttHeld && (DateTime.UtcNow - _hook.PttReleasedUtc).TotalSeconds > 1.5)
            return;
        if (!_hook.Enabled || !_hook.GameIsFocusedNow()) return;
        Task.Run(() => OneTouch(slot, Stats.Via.Voice));
    }

    // ---------------- Loadout overlay ----------------

    // The game's menu opens in the same corner: move the panel out of its way while Ctrl is held.
    private void OnCtrlDown() => _overlay.SetMenuOpen(true);
    private void OnCtrlUp() => _overlay.SetMenuOpen(false);

    /// <summary>Ctrl+V in game: hide / show the panel until the next scan. The Settings checkbox turns the feature off entirely.</summary>
    private void ToggleOverlay()
    {
        if (!_settings.ShowOverlay)
        {
            SetStatus("The overlay is turned off in Settings", Theme.Danger);
            return;
        }
        _overlayHidden = !_overlayHidden;
        UpdateOverlay();
        SetStatus(_overlayHidden ? "Overlay hidden. It'll come back after your next scan" : "Overlay's back");
    }

    /// <summary>Show the panel only while Helldivers 2 is the active window (checked every 400 ms).</summary>
    private void UpdateOverlay()
    {
        bool want = _settings.ShowOverlay && !_overlayHidden && _hook.Enabled && _overlay.HasRows;
        _overlay.ShowOn(want ? LoadoutOverlay.GameScreen() : null);
    }

    // ---------------- Menu scanning ----------------

    private void OnDetectRequested()
    {
        if (_detectTask is { IsCompleted: false }) return;
        _detectTask = Task.Run(DetectAsync);
    }

    /// <summary>One-touch scan: the app holds Ctrl to open the menu, reads it, then lets go.</summary>
    private void StartOneTouchScan()
    {
        if (_detectTask is { IsCompleted: false }) return;
        _detectTask = Task.Run(async () =>
        {
            bool pressed = !CtrlHeld();
            if (pressed) InputSender.PressCtrl(true);
            try { await DetectAsync(); }
            finally { if (pressed) InputSender.PressCtrl(false); }
        });
    }

    private static bool CtrlHeld() => (Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0;

    private async Task DetectAsync()
    {
        try
        {
            await Task.Delay(250); // in case the scan was triggered before the menu finished drawing
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (!CtrlHeld()) break; // menu closed

                var cap = MenuReader.CaptureMenuRegion();
                if (cap == null) return;
                List<DetectedRow> rows;
                using (cap.Value.image)
                    rows = await MenuReader.ReadAsync(cap.Value.image, cap.Value.screenHeight);

                // Only trust a read that looks like the real menu: a few rows, mostly recognised.
                int known = rows.Count(r => r.ScreenCode != null || MenuReader.Match(r.Name, _stratagems) != null);
                if (rows.Count >= 3 && known >= rows.Count - 1)
                {
                    Invoke(() => ApplyDetected(rows));
                    if (_settings.SoundCues) Sounds.Success();
                    return;
                }
                await Task.Delay(200);
            }
            if (_settings.SoundCues) Sounds.Failure();
            BeginInvoke(() => SetStatus("Couldn't read the menu. Hold Ctrl until it opens, then press C", Theme.Danger));
        }
        catch (Exception ex)
        {
            if (_settings.SoundCues) Sounds.Failure();
            BeginInvoke(() => SetStatus("Something went wrong reading the menu: " + ex.Message, Theme.Danger));
        }
    }

    private void ApplyDetected(List<DetectedRow> rows)
    {
        bool listChanged = false;
        var newSlots = new string?[9];

        for (int i = 0; i < Math.Min(9, rows.Count); i++)
        {
            var row = rows[i];
            var s = MenuReader.Match(row.Name, _stratagems);

            if (s == null && row.ScreenCode != null)
            {
                // A stratagem we don't know yet: learn it from the arrows on screen.
                s = new Stratagem { Name = TitleCase(row.Name), Code = row.ScreenCode };
                _stratagems.Add(s);
                listChanged = true;
            }
            else if (s != null && row.ScreenCode != null && row.ScreenCode != s.Code)
            {
                // The game shows a different code (e.g. changed in a patch): trust the screen.
                s.Code = row.ScreenCode;
                listChanged = true;
            }
            newSlots[i] = s?.Name;
        }

        if (listChanged)
        {
            Storage.SaveStratagems(_stratagems);
            RebuildIndex();
            FillComboItems();
            _settingsForm?.SetStratagemCount(_stratagems.Count);
        }

        bool slotsChanged = listChanged || !newSlots.SequenceEqual(_settings.Slots);
        if (slotsChanged)
        {
            _settings.Slots = newSlots!;
            ApplySlotsToUi();
            RefreshSlotCodes();
            Storage.SaveSettings(_settings);
        }
        SetStatus($"Got your loadout: {rows.Count} stratagems");

        // A fresh scan always brings back an overlay hidden with Ctrl+V.
        _overlayHidden = false;
        UpdateOverlay();
    }

    private static string TitleCase(string s) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    // ---------------- Status ----------------

    private void UpdateStats()
    {
        var sums = new[] { _stats.Sum(1), _stats.Sum(7), _stats.Sum(null) };
        for (int i = 0; i < 3; i++)
        {
            _statNumbers[i].Text = sums[i].keys.ToString("N0", CultureInfo.CurrentCulture);
            _statCaptions[i].Text = $"{sums[i].callIns:N0} CALL-INS";
        }
    }

    /// <summary>Latest event, shown on the right of the status bar (red for problems) and read out by screen readers.</summary>
    private void SetStatus(string text, Color? color = null)
    {
        text = text.TrimEnd('.');
        _status.Text = text;
        _status.AccessibleName = text;
        _status.ForeColor = color == Theme.Danger ? Theme.Danger : Theme.Muted;
        Announce(text);
    }

    /// <summary>Tells screen readers (Narrator, NVDA, JAWS) about a change without moving focus.</summary>
    private void Announce(string text)
    {
        try
        {
            if (_status.IsHandleCreated)
                _status.AccessibilityObject.RaiseAutomationNotification(
                    System.Windows.Forms.Automation.AutomationNotificationKind.Other,
                    System.Windows.Forms.Automation.AutomationNotificationProcessing.MostRecent, text);
        }
        catch { /* older Windows without UIA notifications */ }
    }

    private void CheckGame()
    {
        bool running = KeyboardHook.GameProcessNames.Any(n =>
        {
            var procs = System.Diagnostics.Process.GetProcessesByName(n);
            foreach (var p in procs) p.Dispose();
            return procs.Length > 0;
        });
        if (running != _gameRunning || _stateLabel.Text.Length == 0)
        {
            _gameRunning = running;
            UpdateState();
            if (!running && _downloaded != null) InstallUpdate(); // game closed: safe to restart now
        }
    }

    /// <summary>Red = hotkeys off, amber = waiting for the game, green = ready.</summary>
    private void UpdateState()
    {
        var (color, text) = !_hook.Enabled ? (Theme.Danger, "DISABLED")
                          : !_gameRunning ? (Theme.Amber, "WAITING FOR HELLDIVERS 2")
                          : (Theme.Ok, "READY");
        bool changed = _stateLabel.Text.Length > 0 && _stateLabel.Text != text;
        _statusDot.ForeColor = color;
        _stateLabel.Text = text;
        string spoken = !_hook.Enabled ? "Hotkeys are off" : !_gameRunning ? "Waiting for Helldivers 2" : "Ready";
        _stateLabel.AccessibleName = "Status: " + spoken;
        _statusDot.AccessibleName = spoken;
        if (changed) Announce(spoken);
        if (_tray != null) _tray.Text = "Stratagem Helper — " + spoken;
    }

    // ---------------- Window ----------------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(this);
    }

    /// <summary>First time the window opens: offer "start with Windows" once (skipped if it's already on).</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_settings.FirstRunDone) return;
        _settings.FirstRunDone = true;
        Storage.SaveSettings(_settings);
        if (Startup.IsEnabled()) return;

        BeginInvoke(() =>
        {
            using var dlg = new FirstRunDialog(_icon);
            if (dlg.ShowDialog(this) != DialogResult.Yes) return;
            try
            {
                Startup.SetEnabled(true);
                SetStatus("Will start with Windows");
            }
            catch
            {
                SetStatus("Couldn't turn on start with Windows", Theme.Danger);
            }
        });
    }

    /// <summary>The close button hides to the tray; use the tray menu's Exit to quit.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _settingsForm?.Close();
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Panel;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Yellow;
        public override Color MenuItemSelected => Theme.PanelAlt;
        public override Color ImageMarginGradientBegin => Theme.Panel;
        public override Color ImageMarginGradientMiddle => Theme.Panel;
        public override Color ImageMarginGradientEnd => Theme.Panel;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _overlayTimer.Stop();
        _voice.Dispose();
        _overlay.Dispose();
        _hook.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosed(e);
    }
}
