using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace StratagemHelper;

/// <summary>Dark "military terminal" look: gunmetal panels, hazard yellow accents, condensed caps.</summary>
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(14, 15, 16);
    public static readonly Color Panel = Color.FromArgb(24, 26, 28);
    public static readonly Color PanelAlt = Color.FromArgb(32, 35, 38);
    public static readonly Color Border = Color.FromArgb(58, 62, 66);
    public static readonly Color Yellow = Color.FromArgb(255, 230, 0);
    public static readonly Color YellowDim = Color.FromArgb(150, 136, 20);
    public static readonly Color Text = Color.FromArgb(228, 228, 224);
    public static readonly Color Muted = Color.FromArgb(138, 143, 148);
    public static readonly Color Ok = Color.FromArgb(120, 210, 95);
    public static readonly Color Amber = Color.FromArgb(255, 160, 30);
    public static readonly Color Danger = Color.FromArgb(232, 72, 52);

    private static string Family(params string[] candidates)
    {
        using var fonts = new InstalledFontCollection();
        var installed = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.FirstOrDefault(installed.Contains) ?? "Segoe UI";
    }

    private static readonly string Condensed = Family("Bahnschrift Condensed", "Bahnschrift SemiCondensed", "Bahnschrift", "Segoe UI");
    private static readonly string Regular = Family("Bahnschrift", "Segoe UI");

    public static readonly Font Title = new(Condensed, 20f, FontStyle.Bold);
    public static readonly Font Heading = new(Condensed, 11f, FontStyle.Bold);
    public static readonly Font Body = new(Regular, 10f);
    public static readonly Font Small = new(Regular, 8.5f);
    public static readonly Font Badge = new(Condensed, 13f, FontStyle.Bold);
    public static readonly Font Arrows = new("Segoe UI Symbol", 12f, FontStyle.Bold);

    // ---------------- Window chrome ----------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar (Windows 10 20H1+) and matching caption colour (Windows 11).</summary>
    public static void ApplyDarkTitleBar(Form f)
    {
        int on = 1;
        DwmSetWindowAttribute(f.Handle, 20, ref on, 4);                 // DWMWA_USE_IMMERSIVE_DARK_MODE
        int caption = ColorTranslator.ToWin32(Bg);
        DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);            // DWMWA_CAPTION_COLOR
        int border = ColorTranslator.ToWin32(YellowDim);
        DwmSetWindowAttribute(f.Handle, 34, ref border, 4);             // DWMWA_BORDER_COLOR
    }

    /// <summary>App / tray icon drawn at runtime: yellow up-chevron on black.</summary>
    public static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(20, 20, 20));
            g.FillRectangle(bg, 1, 1, 30, 30);
            using var pen = new Pen(Yellow, 2);
            g.DrawRectangle(pen, 2, 2, 27, 27);
            using var y = new SolidBrush(Yellow);
            g.FillPolygon(y, new[] { new Point(16, 6), new Point(26, 17), new Point(20, 17), new Point(20, 26), new Point(12, 26), new Point(12, 17), new Point(6, 17) });
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public static Label MutedLabel(string text, int width) => new()
    {
        Text = text, Font = Small, ForeColor = Muted, AutoSize = false,
        Size = new Size(width, 26), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0),
    };

    public static void StyleNumber(NumericUpDown n)
    {
        n.BackColor = PanelAlt;
        n.ForeColor = Text;
        n.BorderStyle = BorderStyle.FixedSingle;
        n.Font = Body;
        n.TextAlign = HorizontalAlignment.Center;
        n.Margin = new Padding(0, 1, 6, 0);
    }

    public static void FillHazard(Graphics g, Rectangle r, int stripe = 10)
    {
        using var bg = new SolidBrush(Yellow);
        g.FillRectangle(bg, r);
        using var dark = new SolidBrush(Bg);
        var old = g.Clip;
        g.SetClip(r);
        for (int x = r.Left - r.Height; x < r.Right + r.Height; x += stripe * 2)
            g.FillPolygon(dark, new[]
            {
                new Point(x, r.Bottom), new Point(x + stripe, r.Bottom),
                new Point(x + stripe + r.Height, r.Top), new Point(x + r.Height, r.Top),
            });
        g.Clip = old;
    }
}

/// <summary>Thin diagonal hazard-stripe bar.</summary>
internal sealed class HazardStripe : Control
{
    public HazardStripe()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Height = 6;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillHazard(e.Graphics, ClientRectangle, 8);
    }
}

/// <summary>Section heading: "// SETTINGS" with a rule to the right.</summary>
internal sealed class SectionHeader : Control
{
    public SectionHeader(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Text = text.ToUpperInvariant();
        Font = Theme.Heading;
        Height = 26;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        using var y = new SolidBrush(Theme.Yellow);
        g.FillRectangle(y, 0, 7, 4, 12);
        var size = g.MeasureString(Text, Font);
        using var t = new SolidBrush(Theme.Text);
        g.DrawString(Text, Font, t, 10, (Height - size.Height) / 2);
        using var pen = new Pen(Theme.Border);
        int x = (int)(14 + size.Width);
        g.DrawLine(pen, x, Height / 2, Width, Height / 2);
    }
}

/// <summary>Square keycap badge with a number, e.g. the "1" in Ctrl+1.</summary>
internal sealed class KeyBadge : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active { get; set; } = true;
    public KeyBadge(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Text = text;
        Font = Theme.Badge;
        Size = new Size(30, 30);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var fill = new SolidBrush(Active ? Theme.Yellow : Theme.PanelAlt);
        g.FillRectangle(fill, r);
        if (!Active) { using var p = new Pen(Theme.Border); g.DrawRectangle(p, r); }
        TextRenderer.DrawText(g, Text, Font, r, Active ? Theme.Bg : Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>A stratagem code drawn as solid arrow shapes.</summary>
internal sealed class ArrowCode : Control
{
    private string _code = "";

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Code
    {
        get => _code;
        set { _code = value ?? ""; Invalidate(); }
    }

    public ArrowCode()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Size = new Size(200, 24);
    }

    // Up-pointing arrow on a 0..1 grid: head + stem.
    private static readonly PointF[] Up =
    {
        new(0.5f, 0f), new(1f, 0.5f), new(0.68f, 0.5f), new(0.68f, 1f),
        new(0.32f, 1f), new(0.32f, 0.5f), new(0f, 0.5f),
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        float s = Height - 8, gap = 6, x = 0, y = (Height - s) / 2;
        using var brush = new SolidBrush(Theme.Yellow);
        foreach (char c in _code.ToUpperInvariant())
        {
            float angle = c switch { 'U' => 0, 'R' => 90, 'D' => 180, 'L' => 270, _ => -1 };
            if (angle < 0) continue;
            var state = g.Save();
            g.TranslateTransform(x + s / 2, y + s / 2);
            g.RotateTransform(angle);
            g.FillPolygon(brush, Up.Select(p => new PointF((p.X - 0.5f) * s, (p.Y - 0.5f) * s)).ToArray());
            g.Restore(state);
            x += s + gap;
        }
    }
}

/// <summary>Dark owner-drawn dropdown.</summary>
internal sealed class HdComboBox : ComboBox
{
    public HdComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.PanelAlt;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ItemHeight = 24;
        MaxDropDownItems = 16;
    }

    private const int WM_PAINT = 0x000F;

    /// <summary>Paint over the system dropdown button and border so they match the theme.</summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_PAINT) return;

        using var g = Graphics.FromHwnd(Handle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int bw = SystemInformation.VerticalScrollBarWidth;
        var btn = new Rectangle(Width - bw - 1, 1, bw, Height - 2);
        using (var bg = new SolidBrush(Theme.PanelAlt)) g.FillRectangle(bg, btn);

        // Chevron
        int cx = btn.Left + btn.Width / 2, cy = Height / 2;
        using (var pen = new Pen(Focused || DroppedDown ? Theme.Yellow : Theme.Muted, 2f))
            g.DrawLines(pen, new[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });

        using var border = new Pen(Focused || DroppedDown ? Theme.Yellow : Theme.Border);
        g.SmoothingMode = SmoothingMode.None;
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
        bool selected = !edit && (e.State & DrawItemState.Selected) != 0;
        string text = Items[e.Index]?.ToString() ?? "";
        bool none = text.StartsWith("(");

        using var bg = new SolidBrush(selected ? Theme.Yellow : Theme.PanelAlt);
        e.Graphics.FillRectangle(bg, e.Bounds);
        var color = selected ? Theme.Bg : none ? Theme.Muted : Theme.Text;
        var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, none ? "— EMPTY —" : text.ToUpperInvariant(), Font, r, color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Square checkbox / radio: yellow-filled box with a dark tick when on.</summary>
internal class HdToggle : CheckBox
{
    public HdToggle(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        AutoSize = false;
        Font = Theme.Body;
        Text = text.ToUpperInvariant();
        Height = 26;
        Cursor = Cursors.Hand;
        FitWidth();
    }

    private void FitWidth() => Width = TextRenderer.MeasureText(Text, Font).Width + 34;
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitWidth(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        var box = new Rectangle(1, (Height - 16) / 2, 16, 16);
        if (Checked)
        {
            using var y = new SolidBrush(Theme.Yellow);
            g.FillRectangle(y, box);
            using var tick = new Pen(Theme.Bg, 2.4f);
            g.DrawLines(tick, new[] { new Point(box.X + 3, box.Y + 8), new Point(box.X + 7, box.Y + 12), new Point(box.X + 13, box.Y + 4) });
        }
        else
        {
            using var p = new Pen(Focused || ClientRectangle.Contains(PointToClient(MousePosition)) ? Theme.Yellow : Theme.Border, 1.5f);
            g.DrawRectangle(p, box);
        }
        var tr = new Rectangle(box.Right + 10, 0, Width - box.Right - 10, Height);
        TextRenderer.DrawText(g, Text, Font, tr, Checked ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
}

/// <summary>Mutually exclusive option within its parent, styled like HdToggle.</summary>
internal sealed class HdRadio : HdToggle
{
    public HdRadio(string text) : base(text) { AutoCheck = false; }
    protected override void OnClick(EventArgs e)
    {
        if (!Checked)
        {
            foreach (var sib in Parent?.Controls.OfType<HdRadio>() ?? Enumerable.Empty<HdRadio>())
                if (sib != this) sib.Checked = false;
            Checked = true;
        }
        base.OnClick(e);
    }
}

/// <summary>Click, then press a key / mouse button to bind it. Shows the current binding.</summary>
internal sealed class HdBindBox : Control
{
    private string? _binding;
    private bool _capturing;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? Binding { get => _binding; set { _binding = value; Invalidate(); } }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Capturing { get => _capturing; set { _capturing = value; Invalidate(); } }

    public HdBindBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Size = new Size(150, 28);
        Font = Theme.Body;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var bg = new SolidBrush(_capturing ? Theme.Yellow : Theme.PanelAlt)) g.FillRectangle(bg, r);
        bool hover = ClientRectangle.Contains(PointToClient(MousePosition));
        using (var p = new Pen(_capturing || hover ? Theme.Yellow : Theme.Border)) g.DrawRectangle(p, r);
        string text = _capturing ? "PRESS A KEY…" : Bindings.Display(_binding);
        var color = _capturing ? Theme.Bg : string.IsNullOrEmpty(_binding) ? Theme.Muted : Theme.Text;
        TextRenderer.DrawText(g, text, Font, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
}

/// <summary>Row of tabs: caps labels, yellow underline on the active one.</summary>
internal sealed class HdTabs : Control
{
    private readonly string[] _tabs;
    private int _selected;
    public event Action<int>? SelectedChanged;

    public HdTabs(params string[] tabs)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        _tabs = tabs.Select(t => t.ToUpperInvariant()).ToArray();
        Font = Theme.Heading;
        Height = 38;
        Cursor = Cursors.Hand;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Invalidate(); SelectedChanged?.Invoke(value); } }
    }

    private Rectangle TabRect(int i)
    {
        int w = Width / _tabs.Length;
        return new Rectangle(i * w, 0, w, Height);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        for (int i = 0; i < _tabs.Length; i++)
            if (TabRect(i).Contains(e.Location)) Selected = i;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Panel);
        using var line = new Pen(Theme.Border);
        g.DrawLine(line, 0, Height - 1, Width, Height - 1);
        for (int i = 0; i < _tabs.Length; i++)
        {
            var r = TabRect(i);
            bool sel = i == _selected;
            TextRenderer.DrawText(g, _tabs[i], Font, r, sel ? Theme.Yellow : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (sel)
            {
                using var y = new SolidBrush(Theme.Yellow);
                g.FillRectangle(y, r.X + 10, Height - 4, r.Width - 20, 4);
            }
        }
    }
}

/// <summary>Outlined yellow button that fills on hover.</summary>
internal sealed class HdButton : Button
{
    public HdButton(string text)
    {
        Text = text.ToUpperInvariant();
        Font = Theme.Heading;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderColor = Theme.Yellow;
        FlatAppearance.BorderSize = 1;
        FlatAppearance.MouseOverBackColor = Theme.Yellow;
        FlatAppearance.MouseDownBackColor = Theme.YellowDim;
        BackColor = Theme.Bg;
        ForeColor = Theme.Yellow;
        AutoSize = true;
        Padding = new Padding(10, 3, 10, 3);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); ForeColor = Theme.Bg; }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); ForeColor = Theme.Yellow; }
}
