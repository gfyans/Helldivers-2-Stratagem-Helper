using System.Runtime.InteropServices;

namespace StratagemHelper;

/// <summary>
/// Small always-on-top, click-through panel listing the scanned loadout ("1 REINFORCE", "2 SOS BEACON" ...),
/// shown in the top-left corner while Helldivers 2 is the active window. It's a separate window drawn over
/// the game (like Discord's or Steam's overlays), never injected into it; needs borderless/windowed mode.
/// When the stratagem menu is open (Ctrl held) it moves right so it doesn't cover the menu.
/// </summary>
internal sealed class LoadoutOverlay : Form
{
    private List<(int slot, string name)> _rows = new();
    private Rectangle _screen;
    private bool _menuOpen;
    private Font? _nameFont, _badgeFont;
    private int _row, _badge, _pad;

    // Positions as fractions of screen height (the HUD scales with height).
    // Top sits just below the reinforcements icon in the HUD's top-left corner (icon ends ~0.075 x screen height).
    private const double PosLeft = 0.012, PosTop = 0.088, PosLeftMenuOpen = 0.32;

    public LoadoutOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Bg;
        Opacity = 0.88;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                      WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>Sets the slots to list (empty slots are skipped).</summary>
    public void SetRows(IReadOnlyList<string?> slotNames)
    {
        _rows = slotNames.Select((n, i) => (slot: i + 1, name: n))
                         .Where(r => !string.IsNullOrEmpty(r.name))
                         .Select(r => (r.slot, MenuReader.StripDesignation(r.name!).ToUpperInvariant()))
                         .ToList();
        Relayout();
    }

    public bool HasRows => _rows.Count > 0;

    /// <summary>How see-through the panel is: 20 (faint) to 100 (solid).</summary>
    public void SetOpacity(int percent) => Opacity = Math.Clamp(percent, 20, 100) / 100.0;

    public void SetMenuOpen(bool open)
    {
        if (_menuOpen == open) return;
        _menuOpen = open;
        Relayout();
    }

    /// <summary>Shows the panel on the given screen (physical pixels), or hides it.</summary>
    public void ShowOn(Rectangle? screen)
    {
        if (screen is not Rectangle s || _rows.Count == 0)
        {
            if (Visible) Hide();
            return;
        }
        if (s != _screen) { _screen = s; Relayout(); }
        if (!Visible) Show();
    }

    private void Relayout()
    {
        if (_screen.IsEmpty) return;
        double h = _screen.Height;
        int row = Math.Max(18, (int)(h * 0.019));
        if (row != _row)
        {
            _row = row;
            _badge = row - 4;
            _pad = Math.Max(4, row / 4);
            _nameFont?.Dispose();
            _badgeFont?.Dispose();
            _nameFont = new Font(Theme.Heading.FontFamily, row * 0.58f, FontStyle.Regular, GraphicsUnit.Pixel);
            _badgeFont = new Font(Theme.Badge.FontFamily, _badge * 0.78f, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        int nameW = _rows.Count == 0 ? 0 : _rows.Max(r => TextRenderer.MeasureText(r.name, _nameFont).Width);
        int w = _pad + 3 + _pad + _badge + _pad + nameW + _pad * 2;
        int hgt = _pad + _rows.Count * _row + _pad;
        int x = _screen.X + (int)(h * (_menuOpen ? PosLeftMenuOpen : PosLeft));
        int y = _screen.Y + (int)(h * PosTop);
        Bounds = new Rectangle(x, y, w, hgt);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e) => Draw(e.Graphics, Width, Height);

    /// <summary>Renders the panel as it would appear on <paramref name="screen"/> (used for README images / tests).</summary>
    public (Bitmap image, Rectangle bounds) Render(Rectangle screen, bool menuOpen)
    {
        _screen = screen;
        _menuOpen = menuOpen;
        Relayout();
        var bmp = new Bitmap(Width, Height);
        using (var g = Graphics.FromImage(bmp)) Draw(g, Width, Height);
        return (bmp, Bounds);
    }

    private void Draw(Graphics g, int width, int height)
    {
        g.Clear(Theme.Bg);
        if (_nameFont == null || _badgeFont == null) return;

        using var accent = new SolidBrush(Theme.Yellow);
        g.FillRectangle(accent, _pad, _pad, 3, height - _pad * 2);   // yellow edge, like the app's section headers

        int x = _pad + 3 + _pad;
        for (int i = 0; i < _rows.Count; i++)
        {
            var (slot, name) = _rows[i];
            int y = _pad + i * _row;
            var badge = new Rectangle(x, y + (_row - _badge) / 2, _badge, _badge);
            g.FillRectangle(accent, badge);
            TextRenderer.DrawText(g, slot.ToString(), _badgeFont, badge, Theme.Bg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var text = new Rectangle(badge.Right + _pad, y, width - badge.Right - _pad, _row);
            TextRenderer.DrawText(g, name, _nameFont, text, Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }

    // ---------------- Where is the game? ----------------

    /// <summary>The monitor (physical pixels) of the foreground window if it's Helldivers 2, else null.</summary>
    public static Rectangle? GameScreen()
    {
        var hwnd = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            if (!KeyboardHook.GameProcessNames.Any(n => string.Equals(n, p.ProcessName, StringComparison.OrdinalIgnoreCase)))
                return null;
        }
        catch { return null; }
        return MenuReader.MonitorOf(hwnd);
    }
}
