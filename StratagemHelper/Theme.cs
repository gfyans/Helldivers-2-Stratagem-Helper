using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace StratagemHelper;

/// <summary>
/// Dark "military terminal" look: gunmetal panels, hazard yellow accents, Chakra Petch caps for headings.
/// Follows Windows High Contrast when it's on, and scales with the app's text-size setting (and Windows
/// display scaling, via each form's AutoScaleMode.Dpi).
/// </summary>
internal static class Theme
{
    public static bool HighContrast { get; private set; }
    /// <summary>App text size: 1.0 = normal, 1.25, 1.5, 2.0.</summary>
    public static float TextScale { get; private set; } = 1f;

    public static Color Bg, Panel, PanelAlt, Border, ControlBorder, Yellow, YellowDim, Text, Muted, Ok, Amber, Danger;
    /// <summary>Text/marks drawn on top of the yellow accent.</summary>
    public static Color OnAccent;
    /// <summary>Keyboard focus ring.</summary>
    public static Color Focus;

    public static Font Title = null!, Heading = null!, Body = null!, Small = null!, Badge = null!;

    static Theme() => Init(1f, SystemInformation.HighContrast);

    public static void Init(float textScale, bool highContrast)
    {
        TextScale = Math.Clamp(textScale, 1f, 2f);
        HighContrast = highContrast;

        if (highContrast)
        {
            // Use the user's chosen High Contrast colours throughout.
            Bg = Panel = PanelAlt = SystemColors.Window;
            Text = Muted = SystemColors.WindowText;
            Border = ControlBorder = SystemColors.WindowText;
            Yellow = YellowDim = SystemColors.Highlight;
            OnAccent = SystemColors.HighlightText;
            Ok = Amber = Danger = SystemColors.WindowText; // states are always spelled out in text too
            Focus = SystemColors.Highlight;
        }
        else
        {
            Bg = Color.FromArgb(14, 15, 16);
            Panel = Color.FromArgb(24, 26, 28);
            PanelAlt = Color.FromArgb(32, 35, 38);
            Border = Color.FromArgb(58, 62, 66);          // decorative lines only
            ControlBorder = Color.FromArgb(118, 124, 130); // outlines of things you can use: 4.3:1 on the background
            Yellow = Color.FromArgb(255, 230, 0);
            YellowDim = Color.FromArgb(150, 136, 20);
            Text = Color.FromArgb(228, 228, 224);
            Muted = Color.FromArgb(150, 155, 160);         // 7:1 on the background
            Ok = Color.FromArgb(120, 210, 95);
            Amber = Color.FromArgb(255, 160, 30);
            Danger = Color.FromArgb(240, 90, 70);
            OnAccent = Bg;
            Focus = Color.FromArgb(255, 255, 255);
        }

        float s = TextScale;
        Title = new Font(Display, 17f * s, FontStyle.Bold); // wider than the old condensed face: leaves room for the header's Update button
        Heading = new Font(Display, 11f * s, FontStyle.Bold);
        Body = new Font(Regular, 10f * s);
        Small = new Font(Regular, 9.5f * s);
        Badge = new Font(Display, 13f * s, FontStyle.Bold);
    }

    private static string Family(params string[] candidates)
    {
        using var fonts = new InstalledFontCollection();
        var installed = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.FirstOrDefault(installed.Contains) ?? "Segoe UI";
    }

    // Headings use Chakra Petch, the same font as the website. It ships inside the exe (Fonts\, SIL OFL).
    private static readonly PrivateFontCollection EmbeddedFonts = new();
    private static readonly FontFamily Display = LoadEmbeddedFamily("Chakra Petch", "ChakraPetch-Regular.ttf", "ChakraPetch-Bold.ttf");
    private static readonly string Regular = Family("Bahnschrift", "Segoe UI");

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

    private static FontFamily LoadEmbeddedFamily(string family, params string[] files)
    {
        try
        {
            var asm = typeof(Theme).Assembly;
            foreach (var file in files)
            {
                using var stream = asm.GetManifestResourceStream("StratagemHelper.Fonts." + file)
                    ?? throw new FileNotFoundException(file);
                var data = new byte[stream.Length];
                stream.ReadExactly(data);
                // Left allocated for the life of the process: both GDI+ and GDI read from it.
                IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                Marshal.Copy(data, 0, mem, data.Length);
                EmbeddedFonts.AddMemoryFont(mem, data.Length);              // GDI+: Font objects
                uint count = 0;
                AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref count); // GDI: TextRenderer finds it by name
            }
            return EmbeddedFonts.Families.First(f => f.Name == family);
        }
        catch
        {
            return new FontFamily(Family("Bahnschrift SemiCondensed", "Bahnschrift", "Segoe UI"));
        }
    }

    // ---------------- Scaling ----------------

    /// <summary>
    /// Call at the start of a form's constructor: scale with Windows display scaling (DPI).
    /// </summary>
    public static void PrepareForm(Form f)
    {
        f.AutoScaleDimensions = new SizeF(96F, 96F);
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.Load += (_, _) => HideFocusUntilKeyboard(f);
    }

    private const int WM_CHANGEUISTATE = 0x0127, UIS_SET = 1;

    /// <summary>
    /// Windows doesn't always start a window with focus rings hidden, so the first control would show one
    /// before you've touched the keyboard. Start hidden; Tab, the arrow keys or Alt bring them back
    /// (WinForms clears the flag itself). Skipped if "always show focus" is on in Windows.
    /// </summary>
    private static void HideFocusUntilKeyboard(Form f)
    {
        if (SystemInformation.MenuAccessKeysUnderlined) return;
        SendMessage(f.Handle, WM_CHANGEUISTATE, (IntPtr)((UISF_HIDEFOCUS << 16) | UIS_SET), IntPtr.Zero);
    }

    /// <summary>Call at the end of a form's constructor: apply the app's own text size on top.</summary>
    public static void ApplyTextScale(Form f)
    {
        // Auto-sized panels and buttons default to "grow only"; after scaling they must also shrink back to their content.
        ShrinkToFit(f);
        if (TextScale != 1f) f.Scale(new SizeF(TextScale, TextScale));
        f.Load += (_, _) => FitToScreen(f);
    }

    private static void ShrinkToFit(Control c)
    {
        if (c.AutoSize)
        {
            if (c is Panel p) p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            else if (c is Button b) b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }
        foreach (Control child in c.Controls) ShrinkToFit(child);
    }

    /// <summary>At large text sizes a window can be taller than the screen: cap it and let it scroll.</summary>
    private static void FitToScreen(Form f)
    {
        var area = Screen.FromControl(f).WorkingArea;
        if (f.Height <= area.Height) return;
        int width = f.Width + SystemInformation.VerticalScrollBarWidth;
        f.AutoSize = false;
        f.AutoScroll = true;
        f.Size = new Size(Math.Min(width, area.Width), area.Height);
        f.Top = area.Top;
    }

    // ---------------- Window chrome ----------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar (Windows 10 20H1+) and matching caption colour (Windows 11). Skipped in High Contrast.</summary>
    public static void ApplyDarkTitleBar(Form f)
    {
        if (HighContrast) return;
        int on = 1;
        DwmSetWindowAttribute(f.Handle, 20, ref on, 4);                 // DWMWA_USE_IMMERSIVE_DARK_MODE
        int caption = ColorTranslator.ToWin32(Bg);
        DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);            // DWMWA_CAPTION_COLOR
        int border = ColorTranslator.ToWin32(YellowDim);
        DwmSetWindowAttribute(f.Handle, 34, ref border, 4);             // DWMWA_BORDER_COLOR
    }

    /// <summary>App / tray icon drawn at runtime: yellow up-chevron on black (brand colours, not themed).</summary>
    public static Icon MakeIcon()
    {
        var yellow = Color.FromArgb(255, 230, 0);
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(20, 20, 20));
            g.FillRectangle(bg, 1, 1, 30, 30);
            using var pen = new Pen(yellow, 2);
            g.DrawRectangle(pen, 2, 2, 27, 27);
            using var y = new SolidBrush(yellow);
            g.FillPolygon(y, new[] { new Point(16, 6), new Point(26, 17), new Point(20, 17), new Point(20, 26), new Point(12, 26), new Point(12, 17), new Point(6, 17) });
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    /// <summary>Small caps label used beside an input, e.g. "KEY HOLD".</summary>
    public static Label MutedLabel(string text, int width) => new()
    {
        Text = text, Font = Small, ForeColor = Muted, AutoSize = false,
        Size = new Size(width, 26), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0),
    };

    public static void StyleNumber(NumericUpDown n, string accessibleName)
    {
        n.BackColor = PanelAlt;
        n.ForeColor = Text;
        n.BorderStyle = BorderStyle.FixedSingle;
        n.Font = Body;
        n.TextAlign = HorizontalAlignment.Center;
        n.Margin = new Padding(0, 1, 6, 0);
        n.AccessibleName = accessibleName;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int WM_QUERYUISTATE = 0x0129, UISF_HIDEFOCUS = 0x1;

    /// <summary>
    /// True when the control has focus and Windows says focus should be visible: after the keyboard has
    /// been used in the window, or always if "show focus" is turned on in Windows. Mouse-only use shows no ring.
    /// </summary>
    public static bool ShowsFocus(Control c) =>
        c.Focused && c.IsHandleCreated &&
        (SendMessage(c.Handle, WM_QUERYUISTATE, IntPtr.Zero, IntPtr.Zero).ToInt64() & UISF_HIDEFOCUS) == 0;

    private const TextFormatFlags LineFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
    private static readonly Dictionary<(string, float, FontStyle, string), (int Top, int Height)> InkCache = new();

    /// <summary>
    /// The rows that actually get ink when <paramref name="sample"/> is drawn with its line top at y = 0.
    /// For "H" that's the capital height. Measured once per font.
    /// </summary>
    public static (int Top, int Height) Ink(Font font, string sample = "H")
    {
        var key = (font.Name, font.Size, font.Style, sample);
        lock (InkCache)
        {
            if (InkCache.TryGetValue(key, out var ink)) return ink;
            var size = TextRenderer.MeasureText(sample, font, Size.Empty, LineFlags);
            int w = Math.Max(1, size.Width + 4), h = Math.Max(1, size.Height);
            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                TextRenderer.DrawText(g, sample, font, new Point(0, 0), Color.White, Color.Black, LineFlags);
            }
            int top = -1, bottom = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    // 30%: a softened top row of the letters is plainly visible at 40%, barely at 20%.
                    if (bmp.GetPixel(x, y).G > 76) { if (top < 0) top = y; bottom = y; break; }
            ink = top < 0 ? (0, h) : (top, bottom - top + 1);
            InkCache[key] = ink;
            return ink;
        }
    }

    /// <summary>What to line up on: the capitals, or for text with no letters (like "—") its own marks.</summary>
    private static string InkSample(string text) => text.Any(char.IsLetterOrDigit) ? "H" : text;

    /// <summary>
    /// Top row of the capitals when they're centred in <paramref name="r"/>. Exact when r's height and the
    /// capital height are both odd or both even (see <see cref="FitCaps"/>); otherwise half a pixel high.
    /// </summary>
    public static int CapsTop(Font font, Rectangle r, string text = "H") => r.Top + (r.Height - Ink(font, InkSample(text)).Height) / 2;

    /// <summary>
    /// <paramref name="height"/>, or one more, so that the space left for text (height minus
    /// <paramref name="chrome"/>) centres the capitals exactly instead of leaving them half a pixel off.
    /// </summary>
    public static int FitCaps(Font font, int height, int chrome) => height + ((height - chrome - Ink(font).Height) & 1);

    /// <summary>
    /// Draws text centred on the capitals themselves. Windows centres the whole line, including the empty
    /// space kept for accents and tails like "g", which makes all-caps text sit off-centre.
    /// </summary>
    public static void DrawCaps(Graphics g, string text, Font font, Rectangle r, Color color, TextFormatFlags flags = TextFormatFlags.HorizontalCenter)
    {
        int y = CapsTop(font, r, text) - Ink(font, InkSample(text)).Top;
        flags &= ~(TextFormatFlags.VerticalCenter | TextFormatFlags.Bottom);
        var line = new Rectangle(r.Left, y, r.Width, Math.Max(r.Height, font.Height * 2));
        TextRenderer.DrawText(g, text, font, line, color, flags | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    /// <summary>Width of an icon drawn beside capitals of height <paramref name="capHeight"/>.</summary>
    public static int IconWidth(ButtonIcon icon, int capHeight) => icon switch
    {
        ButtonIcon.Cog => (int)Math.Round(capHeight * 1.4),
        ButtonIcon.Play => (int)Math.Round(capHeight * 0.85),
        ButtonIcon.Up => capHeight,
        _ => 0,
    };

    /// <summary>
    /// Draws an icon as a shape, centred on the capitals whose rows are <paramref name="capTop"/> to
    /// capTop + capHeight. Drawn rather than taken from a font so it lines up exactly and looks the same everywhere.
    /// </summary>
    public static void DrawIcon(Graphics g, ButtonIcon icon, float x, int capTop, int capHeight, Color color)
    {
        if (icon == ButtonIcon.None) return;
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half; // whole numbers are pixel edges, so shapes meet the letters' rows exactly
        using var brush = new SolidBrush(color);
        float w = IconWidth(icon, capHeight), top = capTop, bottom = capTop + capHeight, cy = (top + bottom) / 2;
        switch (icon)
        {
            case ButtonIcon.Play:
                g.FillPolygon(brush, new[] { new PointF(x, top), new PointF(x + w, cy), new PointF(x, bottom) });
                break;
            case ButtonIcon.Up:
                g.FillPolygon(brush, new PointF[]
                {
                    new(x + w * 0.5f, top), new(x + w, cy), new(x + w * 0.68f, cy), new(x + w * 0.68f, bottom),
                    new(x + w * 0.32f, bottom), new(x + w * 0.32f, cy), new(x, cy),
                });
                break;
            case ButtonIcon.Cog:
                using (var path = CogPath(x + w / 2, cy, w / 2)) g.FillPath(brush, path);
                break;
        }
        g.Restore(state);
    }

    /// <summary>Eight-toothed cog with a round hole, radius <paramref name="r"/>.</summary>
    private static GraphicsPath CogPath(float cx, float cy, float r)
    {
        const int teeth = 8;
        float root = r * 0.7f;
        double step = 2 * Math.PI / teeth, tip = step * 0.2, foot = step * 0.3;
        PointF P(float radius, double a) => new(cx + radius * (float)Math.Cos(a), cy + radius * (float)Math.Sin(a));
        var pts = new List<PointF>();
        for (int i = 0; i < teeth; i++)
        {
            double a = i * step - Math.PI / 2; // a tooth straight up
            pts.Add(P(root, a - foot));
            pts.Add(P(r, a - tip));
            pts.Add(P(r, a + tip));
            pts.Add(P(root, a + foot));
        }
        var path = new GraphicsPath(FillMode.Alternate);
        path.AddPolygon(pts.ToArray());
        float hole = r * 0.38f; // big enough to stay open at the smallest text size
        path.AddEllipse(cx - hole, cy - hole, hole * 2, hole * 2);
        return path;
    }

    /// <summary>A clearly visible keyboard-focus ring (2 px, high contrast).</summary>
    public static void DrawFocus(Graphics g, Rectangle r, Color? color = null)
    {
        using var pen = new Pen(color ?? Focus, 2f) { DashStyle = DashStyle.Solid };
        g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
    }

    public static void FillHazard(Graphics g, Rectangle r, int stripe = 10)
    {
        using var bg = new SolidBrush(Yellow);
        g.FillRectangle(bg, r);
        if (HighContrast) return; // plain bar: stripes add visual noise in High Contrast
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

    /// <summary>"U D R" -> "up, down, right" for screen readers.</summary>
    public static string SpokenCode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return "no code";
        return string.Join(", ", code.ToUpperInvariant().Select(c => c switch
        {
            'U' => "up", 'D' => "down", 'L' => "left", 'R' => "right", _ => "",
        }).Where(s => s.Length > 0));
    }
}

/// <summary>Thin diagonal hazard-stripe bar (decorative).</summary>
internal sealed class HazardStripe : Control
{
    public HazardStripe()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Height = 6;
        AccessibleRole = AccessibleRole.Separator;
        TabStop = false;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillHazard(e.Graphics, ClientRectangle, Math.Max(4, Height + 2));
    }
}

/// <summary>
/// Base for drawn, non-interactive elements. Built on Label because Windows only reads accessible names and
/// roles from standard control types; a plain Control shows up to screen readers as an unnamed "pane".
/// </summary>
internal abstract class DrawnLabel : Label
{
    protected DrawnLabel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        AutoSize = false;
        TabStop = false;
        UseMnemonic = false;
    }
    protected override void OnPaint(PaintEventArgs e) { } // subclasses draw everything
}

/// <summary>Section heading with a yellow mark and a rule to the right.</summary>
internal sealed class SectionHeader : DrawnLabel
{
    public SectionHeader(string text)
    {
        Text = text.ToUpperInvariant();
        Font = Theme.Heading;
        Height = 26;
        AccessibleName = text; // read naturally, not as capitals
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        int bar = Math.Max(4, Height / 6);
        using var y = new SolidBrush(Theme.Yellow);
        g.FillRectangle(y, 0, Height / 4, bar, Height / 2);
        var size = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix);
        int tx = bar + Height / 4;
        // NoPrefix: draw "&" literally instead of as a keyboard-shortcut underline.
        TextRenderer.DrawText(g, Text, Font, new Point(tx, (Height - size.Height) / 2), Theme.Text, TextFormatFlags.NoPrefix);
        using var pen = new Pen(Theme.Border);
        int x = tx + size.Width + Height / 6;
        g.DrawLine(pen, x, Height / 2, Width, Height / 2);
    }
}

/// <summary>Coloured square and a caps status word, e.g. "■ READY", the square centred on the capitals.</summary>
internal sealed class StatusLight : DrawnLabel
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Light { get; set { field = value; Invalidate(); } } = Theme.Amber;

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int cap = Theme.Ink(Font).Height, capTop = Theme.CapsTop(Font, ClientRectangle);
        int square = (int)Math.Round(cap * 0.7);
        square += (cap - square) & 1; // same parity as the capitals, so it centres on them exactly
        using (var b = new SolidBrush(Light)) g.FillRectangle(b, 2, capTop + (cap - square) / 2, square, square);
        int tx = 2 + square * 2;
        Theme.DrawCaps(g, Text, Font, new Rectangle(tx, 0, Width - tx, Height), ForeColor, TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }
}

/// <summary>Square keycap badge with a number, e.g. the "1" in Ctrl+1.</summary>
internal sealed class KeyBadge : DrawnLabel
{
    private readonly string _number;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active { get; set; } = true;

    public KeyBadge(string number)
    {
        _number = number;
        Text = "Slot " + number;
        AccessibleName = Text;
        Font = Theme.Badge;
        Size = new Size(30, 30);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var fill = new SolidBrush(Active ? Theme.Yellow : Theme.PanelAlt);
        g.FillRectangle(fill, r);
        if (!Active) { using var p = new Pen(Theme.ControlBorder); g.DrawRectangle(p, r); }
        TextRenderer.DrawText(g, _number, Font, r, Active ? Theme.OnAccent : Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>A stratagem code drawn as solid arrow shapes, and read out as words ("up, down, right") by screen readers.</summary>
internal sealed class ArrowCode : DrawnLabel
{
    private string _code = "";

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Code
    {
        get => _code;
        set
        {
            _code = value ?? "";
            Text = "Code: " + Theme.SpokenCode(_code);
            AccessibleName = Text;
            Invalidate();
        }
    }

    public ArrowCode()
    {
        Size = new Size(200, 24);
        Text = AccessibleName = "Code: " + Theme.SpokenCode(null);
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
        float s = Height * 0.67f, gap = s * 0.38f, x = 0, y = (Height - s) / 2;
        using var brush = new SolidBrush(Theme.HighContrast ? Theme.Text : Theme.Yellow);
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
        ItemHeight = Font.Height + 8;
        MaxDropDownItems = 16;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ItemHeight = Font.Height + 8; // follows text size / display scaling
    }

    private const int WM_PAINT = 0x000F;

    protected override void OnChangeUICues(UICuesEventArgs e) { base.OnChangeUICues(e); Invalidate(); }

    /// <summary>Paint over the system dropdown button and border so they match the theme.</summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_PAINT || Theme.HighContrast) return; // High Contrast: leave Windows' own drawing

        using var g = Graphics.FromHwnd(Handle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int bw = SystemInformation.VerticalScrollBarWidth;
        var btn = new Rectangle(Width - bw - 1, 1, bw, Height - 2);
        using (var bg = new SolidBrush(Theme.PanelAlt)) g.FillRectangle(bg, btn);

        int cx = btn.Left + btn.Width / 2, cy = Height / 2, a = Math.Max(4, Height / 7);
        using (var pen = new Pen(Theme.ShowsFocus(this) || DroppedDown ? Theme.Yellow : Theme.Muted, 2f))
            g.DrawLines(pen, new[] { new Point(cx - a, cy - a / 2), new Point(cx, cy + a / 2), new Point(cx + a, cy - a / 2) });

        g.SmoothingMode = SmoothingMode.None;
        if (Theme.ShowsFocus(this) || DroppedDown)
        {
            using var focus = new Pen(Theme.Focus, 2f);
            g.DrawRectangle(focus, 1, 1, Width - 3, Height - 3);
        }
        else
        {
            using var border = new Pen(Theme.ControlBorder);
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }
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
        var color = selected ? Theme.OnAccent : none ? Theme.Muted : Theme.Text;
        var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, none ? "— EMPTY —" : text.ToUpperInvariant(), Font, r, color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Square checkbox: yellow-filled box with a dark tick when on. Sentence-case label, visible focus ring.</summary>
internal class HdToggle : CheckBox
{
    public HdToggle(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        AutoSize = false;
        Font = Theme.Body;
        Text = text;
        Height = 28;
        Cursor = Cursors.Hand;
        FitWidth();
    }

    private int BoxSize => Math.Max(14, (int)(Height * 0.6));
    private void FitWidth() => Width = TextRenderer.MeasureText(Text, Font).Width + BoxSize + Height / 2 + 8;
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitWidth(); }
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        FitWidth(); // size to the (already scaled) text rather than stretching the old width
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        int bs = BoxSize;
        var box = new Rectangle(2, (Height - bs) / 2, bs, bs);
        bool hover = ClientRectangle.Contains(PointToClient(MousePosition));
        if (Checked)
        {
            using var y = new SolidBrush(Theme.Yellow);
            g.FillRectangle(y, box);
            using var tick = new Pen(Theme.OnAccent, Math.Max(2f, bs / 7f));
            g.DrawLines(tick, new[]
            {
                new PointF(box.X + bs * 0.2f, box.Y + bs * 0.5f),
                new PointF(box.X + bs * 0.43f, box.Y + bs * 0.74f),
                new PointF(box.X + bs * 0.8f, box.Y + bs * 0.26f),
            });
        }
        else
        {
            using var p = new Pen(hover ? Theme.Yellow : Theme.ControlBorder, 1.5f);
            g.DrawRectangle(p, box);
        }
        var tr = new Rectangle(box.Right + Height / 3, 0, Width - box.Right - Height / 3, Height);
        TextRenderer.DrawText(g, Text, Font, tr, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        if (Theme.ShowsFocus(this)) Theme.DrawFocus(g, ClientRectangle);
    }

    protected override void OnChangeUICues(UICuesEventArgs e) { base.OnChangeUICues(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
}

/// <summary>Mutually exclusive option within its parent, styled like HdToggle. Arrow keys move between options.</summary>
internal sealed class HdRadio : HdToggle
{
    public HdRadio(string text) : base(text)
    {
        AutoCheck = false;
        AccessibleRole = AccessibleRole.RadioButton;
    }

    private IEnumerable<HdRadio> Group => Parent?.Controls.OfType<HdRadio>() ?? Enumerable.Empty<HdRadio>();

    public void Choose(bool fromKeyboard)
    {
        if (!Checked)
        {
            foreach (var sib in Group) if (sib != this) sib.Checked = false;
            Checked = true;
        }
        if (fromKeyboard) Focus();
    }

    protected override void OnClick(EventArgs e)
    {
        Choose(fromKeyboard: false);
        base.OnClick(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not (Keys.Left or Keys.Right or Keys.Up or Keys.Down)) return;
        var list = Group.ToList();
        int i = list.IndexOf(this) + (e.KeyCode is Keys.Right or Keys.Down ? 1 : -1);
        list[(i + list.Count) % list.Count].Choose(fromKeyboard: true);
        e.Handled = true;
    }
}

/// <summary>
/// Click (or Enter/Space), then press a key or mouse button to bind it. Built on Button so keyboard use and
/// screen readers work like any other button.
/// </summary>
internal sealed class HdBindBox : Button
{
    private string? _binding;
    private bool _capturing;

    /// <summary>What this box is for, e.g. "Slot 1 key" (used by screen readers).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Label { get; set { field = value; UpdateName(); } } = "Key";

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? Binding { get => _binding; set { _binding = value; UpdateName(); Invalidate(); } }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Capturing { get => _capturing; set { _capturing = value; UpdateName(); Invalidate(); } }

    public HdBindBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Size = new Size(150, 28);
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        UseMnemonic = false;
        AccessibleDescription = "Press Enter, then press the key or mouse button you want. Escape cancels, Backspace clears.";
        UpdateName();
    }

    private void UpdateName()
    {
        string value = _capturing ? "waiting for a key" : string.IsNullOrEmpty(_binding) ? "not set" : Bindings.Display(_binding).ToLowerInvariant();
        AccessibleName = $"{Label}: {value}";
        Text = _capturing ? "PRESS A KEY…" : Bindings.Display(_binding);
        if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
    }

    protected override bool ShowFocusCues => false;
    protected override void OnChangeUICues(UICuesEventArgs e) { base.OnChangeUICues(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var bg = new SolidBrush(_capturing ? Theme.Yellow : Theme.PanelAlt)) g.FillRectangle(bg, r);
        bool hover = ClientRectangle.Contains(PointToClient(MousePosition));
        using (var p = new Pen(_capturing || hover ? Theme.Yellow : Theme.ControlBorder)) g.DrawRectangle(p, r);
        var color = _capturing ? Theme.OnAccent : string.IsNullOrEmpty(_binding) ? Theme.Muted : Theme.Text;
        Theme.DrawCaps(g, Text, Font, new Rectangle(1, 1, Width - 2, Height - 2), color); // inside the 1 px border
        if (Theme.ShowsFocus(this) && !_capturing) Theme.DrawFocus(g, ClientRectangle);
    }

    // Keep the height one that centres the text exactly inside the border (it may grow by a pixel).
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if ((specified & BoundsSpecified.Height) != 0) height = Theme.FitCaps(Font, height, 2);
        base.SetBoundsCore(x, y, width, height, specified);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Height = Theme.FitCaps(Font, Height, 2);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
}

/// <summary>
/// Row of tabs: caps labels, yellow underline on the active one. Each tab is a real button (so screen readers
/// announce "General tab, selected"); only the selected one is a Tab stop, and the arrow keys move between them.
/// Ctrl+Tab is handled by the owning window.
/// </summary>
internal sealed class HdTabs : Panel
{
    private readonly TabButton[] _buttons;
    private int _selected;
    public event Action<int>? SelectedChanged;

    public HdTabs(params string[] tabs)
    {
        Height = 38;
        BackColor = Theme.Panel;
        AccessibleName = "Settings sections";
        AccessibleRole = AccessibleRole.PageTabList;
        _buttons = tabs.Select((t, i) => new TabButton(this, t, i)).ToArray();
        Controls.AddRange(_buttons);
        UpdateTabs();
    }

    public int Count => _buttons.Length;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Selected
    {
        get => _selected;
        set
        {
            value = (value % _buttons.Length + _buttons.Length) % _buttons.Length;
            if (_selected == value) return;
            bool hadFocus = _buttons.Any(b => b.Focused);
            _selected = value;
            UpdateTabs();
            SelectedChanged?.Invoke(value);
            if (hadFocus) _buttons[value].Focus();
        }
    }

    /// <summary>Focuses the selected tab.</summary>
    public new void Focus() => _buttons[_selected].Focus();

    private void UpdateTabs()
    {
        for (int i = 0; i < _buttons.Length; i++)
        {
            bool sel = i == _selected;
            _buttons[i].TabStop = sel;
            _buttons[i].AccessibleName = $"{_buttons[i].Caption} tab{(sel ? ", selected" : "")}";
            _buttons[i].Invalidate();
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_buttons == null) return;
        int w = Width / _buttons.Length;
        for (int i = 0; i < _buttons.Length; i++)
            _buttons[i].Bounds = new Rectangle(i * w, 0, i == _buttons.Length - 1 ? Width - i * w : w, Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var line = new Pen(Theme.Border);
        e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }

    private sealed class TabButton : Button
    {
        private readonly HdTabs _owner;
        private readonly int _index;
        public string Caption { get; }

        public TabButton(HdTabs owner, string caption, int index)
        {
            _owner = owner;
            _index = index;
            Caption = caption;
            Text = caption.ToUpperInvariant();
            Font = Theme.Heading;
            UseMnemonic = false;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PageTab;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override bool ShowFocusCues => false;
    protected override void OnChangeUICues(UICuesEventArgs e) { base.OnChangeUICues(e); Invalidate(); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _owner.Selected = _index;
            Focus();
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int target = e.KeyCode switch
            {
                Keys.Left => _index - 1,
                Keys.Right => _index + 1,
                Keys.Home => 0,
                Keys.End => _owner.Count - 1,
                _ => int.MinValue,
            };
            if (target == int.MinValue) return;
            _owner.Selected = target;
            _owner.Focus();
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Panel);
            bool sel = _index == _owner._selected;
            int bar = Math.Max(3, Height / 10);
            using (var line = new Pen(Theme.Border)) g.DrawLine(line, 0, Height - 1, Width, Height - 1);
            Theme.DrawCaps(g, Text, Font, ClientRectangle, sel ? Theme.Yellow : Theme.Muted);
            if (sel)
            {
                using var y = new SolidBrush(Theme.Yellow);
                g.FillRectangle(y, 10, Height - bar, Width - 20, bar);
            }
            if (Theme.ShowsFocus(this)) Theme.DrawFocus(g, new Rectangle(4, 4, Width - 8, Height - bar - 6));
        }
    }
}

/// <summary>Small shapes drawn beside button text (see <see cref="Theme.DrawIcon"/>).</summary>
internal enum ButtonIcon { None, Cog, Play, Up }

/// <summary>Outlined yellow button that fills on hover; a thick border shows keyboard focus.</summary>
internal sealed class HdButton : Button
{
    private readonly bool _primary;
    private readonly ButtonIcon _icon;

    /// <param name="primary">Solid yellow (the recommended choice) instead of outlined.</param>
    /// <param name="icon">Shape drawn before the text.</param>
    public HdButton(string text, bool primary = false, ButtonIcon icon = ButtonIcon.None)
    {
        _primary = primary;
        _icon = icon;
        Text = text.ToUpperInvariant();
        AccessibleName = text; // read naturally, not as capitals
        Font = Theme.Heading;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderColor = Theme.Yellow;
        FlatAppearance.BorderSize = 1;
        FlatAppearance.MouseOverBackColor = Theme.HighContrast ? Theme.Yellow : primary ? Color.FromArgb(255, 240, 90) : Theme.Yellow;
        FlatAppearance.MouseDownBackColor = Theme.YellowDim;
        BackColor = primary ? Theme.Yellow : Theme.Bg;
        ForeColor = primary ? Theme.OnAccent : Theme.Yellow;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink; // re-fit to the text after scaling instead of keeping the stretched size
        Padding = new Padding(10, 3, 10, 3);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    protected override bool ShowFocusCues => false; // we draw our own, clearer one
    protected override void OnChangeUICues(UICuesEventArgs e) { base.OnChangeUICues(e); Invalidate(); }

    private static int IconGap(int capHeight) => Math.Max(4, (int)Math.Round(capHeight * 0.6));

    /// <summary>Room for the icon, and a height that centres the capitals exactly (it may grow by a pixel).</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var size = base.GetPreferredSize(proposedSize);
        if (_icon != ButtonIcon.None)
        {
            int cap = Theme.Ink(Font).Height;
            size.Width += Theme.IconWidth(_icon, cap) + IconGap(cap);
        }
        size.Height = Theme.FitCaps(Font, size.Height, 2);
        return size;
    }

    private bool _hover, _pressed;

    // Drawn by hand (rather than base.OnPaint) so the capitals can be centred properly; see Theme.DrawCaps.
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var back = _pressed ? FlatAppearance.MouseDownBackColor : _hover ? FlatAppearance.MouseOverBackColor : BackColor;
        g.Clear(back);
        using (var border = new Pen(FlatAppearance.BorderColor)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        // Padding sets the width; vertically, centre inside the 1 px border (scaled padding can split unevenly).
        var r = new Rectangle(Padding.Left, 1, Width - Padding.Horizontal, Height - 2);
        if (_icon == ButtonIcon.None) Theme.DrawCaps(g, Text, Font, r, ForeColor);
        else
        {
            int cap = Theme.Ink(Font).Height, iconW = Theme.IconWidth(_icon, cap), gap = IconGap(cap);
            int textW = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            int x = r.Left + (r.Width - (iconW + gap + textW)) / 2;
            Theme.DrawIcon(g, _icon, x, Theme.CapsTop(Font, r), cap, ForeColor);
            Theme.DrawCaps(g, Text, Font, new Rectangle(x + iconW + gap, r.Top, textW + 4, r.Height), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
        // On the solid yellow primary button a light ring would vanish, so use the dark text colour there.
        if (Theme.ShowsFocus(this)) Theme.DrawFocus(g, ClientRectangle, _primary ? Theme.OnAccent : null);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; if (!_primary) ForeColor = Theme.OnAccent; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _pressed = false; if (!_primary) ForeColor = Theme.Yellow; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
}
