namespace StratagemHelper;

/// <summary>
/// Shown once, the first time the app opens: offers to start with Windows (into the tray) and gives a
/// three-line quick start. DialogResult.Yes = start with Windows.
/// </summary>
internal sealed class FirstRunDialog : Form
{
    public FirstRunDialog(Icon icon)
    {
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
            AutoSize = true, Location = new Point(Pad - 3, 8),
        });
        root.Controls.Add(header);
        root.Controls.Add(new HazardStripe { Width = W, Margin = Padding.Empty });

        var body = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(Pad, 16, Pad, 20), Margin = Padding.Empty, BackColor = Theme.Bg,
        };

        body.Controls.Add(new SectionHeader("Start with Windows?") { Width = Inner, Margin = new Padding(0, 0, 0, 6) });
        body.Controls.Add(new Label
        {
            Text = "Stratagem Helper can start when you sign in to Windows and wait quietly in the system tray, " +
                   "so it's always ready when you play. It only acts while Helldivers 2 is the active window.",
            AutoSize = true, MaximumSize = new Size(Inner, 0), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 6),
        });
        body.Controls.Add(new Label
        {
            Text = "YOU CAN CHANGE THIS ANY TIME IN SETTINGS › GENERAL.",
            AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 14),
        });

        body.Controls.Add(new SectionHeader("Quick start") { Width = Inner, Margin = new Padding(0, 0, 0, 6) });
        foreach (var line in new[]
        {
            "1.  In a mission, hold Ctrl and press C to scan your loadout.",
            "2.  Call in with Ctrl + 1–9, or Numpad 1–9 on its own.",
            "3.  Or turn on voice and say “call in” + a stratagem.",
        })
            body.Controls.Add(new Label { Text = line, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 3) });

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
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(this);
    }
}
