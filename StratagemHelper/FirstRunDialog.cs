namespace StratagemHelper;

/// <summary>
/// Shown once, the first time the app opens: suggests arrow keys for stratagem input, offers to start with
/// Windows and gives a short quick start.
/// DialogResult.Yes = start with Windows. Enter = yes, Esc = not now.
/// </summary>
internal sealed class FirstRunDialog : Form
{
    private readonly HdToggle _arrowBox = new("I use the arrow keys for stratagems");

    /// <summary>The player says they've moved stratagem inputs to the arrow keys in the game.</summary>
    public bool UseArrowKeys => _arrowBox.Checked;

    public FirstRunDialog(Icon icon, bool useArrowKeys, Settings settings)
    {
        string menu = Bindings.Name(settings.MenuKey);
        _arrowBox.Checked = useArrowKeys;
        Theme.PrepareForm(this);
        const int W = 500, Pad = 22, Inner = W - Pad * 2;

        Text = "Welcome to Stratagem Helper";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Icon = icon;

        var root = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = Theme.Bg, Margin = Padding.Empty, Padding = Padding.Empty,
        };

        var header = new Panel { Size = new Size(W, 52), BackColor = Theme.Bg, Margin = Padding.Empty };
        header.Controls.Add(new Label
        {
            Text = "WELCOME, HELLDIVER", Font = Theme.Title, ForeColor = Theme.Yellow,
            AutoSize = true, Location = new Point(Pad - 3, 8), AccessibleName = "Welcome, Helldiver",
        });
        root.Controls.Add(header);
        root.Controls.Add(new HazardStripe { Width = W, Margin = Padding.Empty });

        var body = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(Pad, 16, Pad, 20), Margin = Padding.Empty, BackColor = Theme.Bg,
        };

        body.Controls.Add(new Label
        {
            Text = "Tip: in the game's controls, set the stratagem directions to the arrow keys. " +
                   $"Then you can keep moving with {Bindings.WasdName} while you call in.",
            AutoSize = true, MaximumSize = new Size(Inner, 0), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 2),
        });
        _arrowBox.Margin = new Padding(2, 0, 0, 0);
        _arrowBox.AccessibleName = "I use the arrow keys for stratagems";
        body.Controls.Add(_arrowBox);
        body.Controls.Add(new Label
        {
            Text = "Tick this if you've changed it, so the app sends arrows too. Other keys can be set in Settings.",
            AutoSize = true, MaximumSize = new Size(Inner, 0), Font = Theme.Small, ForeColor = Theme.Muted,
            Margin = new Padding(0, 0, 0, 16),
        });

        body.Controls.Add(new SectionHeader("Start with Windows?") { Width = Inner, Margin = new Padding(0, 0, 0, 6) });
        body.Controls.Add(new Label
        {
            Text = "Want Stratagem Helper to start with Windows? It'll sit quietly in the system tray, " +
                   "ready whenever you play.",
            AutoSize = true, MaximumSize = new Size(Inner, 0), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 6),
        });
        body.Controls.Add(new Label
        {
            Text = "You can change this any time in Settings.",
            AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 14),
        });

        body.Controls.Add(new SectionHeader("Quick start") { Width = Inner, Margin = new Padding(0, 0, 0, 6) });
        foreach (var line in new[]
        {
            $"1.  In a mission, hold {menu} and press {Bindings.Name(settings.ScanHotkey)} to grab your loadout.",
            $"2.  Call in with {menu} + 1–9, or just Numpad 1–9.",
            "3.  Or turn on voice and say “call in” and a stratagem.",
        })
            body.Controls.Add(new Label { Text = line, AutoSize = true, MaximumSize = new Size(Inner, 0), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 3) });

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 18, 0, 0), WrapContents = false };
        var yes = new HdButton("Yes, start with Windows", primary: true) { Margin = new Padding(0, 0, 10, 0), DialogResult = DialogResult.Yes };
        var no = new HdButton("Not now") { Margin = Padding.Empty, DialogResult = DialogResult.No };
        buttons.Controls.Add(yes);
        buttons.Controls.Add(no);
        body.Controls.Add(buttons);

        root.Controls.Add(body);
        Controls.Add(root);
        AcceptButton = yes;
        CancelButton = no;
        ActiveControl = yes;

        Theme.ApplyTextScale(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(this);
    }
}
