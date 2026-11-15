namespace StratagemHelper;

// MainForm calls Program.Restart; the test tool never restarts.
internal static class Program
{
    public static void Restart(bool startHidden) { }
}

internal static class Dwm
{
    public struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static extern int GetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
}

internal static class WindowShot
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Dwm.RECT rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    /// <summary>
    /// The window as drawn, frame included, whatever is on top of it on screen (copying the screen would
    /// pick up other windows). Trimmed to the visible frame, without Windows' invisible resize borders.
    /// </summary>
    public static System.Drawing.Bitmap Capture(IntPtr window)
    {
        GetWindowRect(window, out var w);
        Dwm.GetWindowAttribute(window, 9, out var v, 16); // DWMWA_EXTENDED_FRAME_BOUNDS
        using var full = new System.Drawing.Bitmap(w.Right - w.Left, w.Bottom - w.Top);
        using (var g = System.Drawing.Graphics.FromImage(full))
        {
            IntPtr dc = g.GetHdc();
            PrintWindow(window, dc, 2); // PW_RENDERFULLCONTENT
            g.ReleaseHdc(dc);
        }
        var shot = full.Clone(new System.Drawing.Rectangle(v.Left - w.Left, v.Top - w.Top, v.Right - v.Left, v.Bottom - v.Top), full.PixelFormat);
        // Windows 11 draws the 1 px window border itself, so PrintWindow leaves it black. Paint the colour the app
        // asks for (Theme.ApplyDarkTitleBar); in High Contrast, the system's own frame colour.
        using (var g = System.Drawing.Graphics.FromImage(shot))
        using (var pen = new System.Drawing.Pen(StratagemHelper.Theme.HighContrast ? System.Drawing.SystemColors.WindowFrame : StratagemHelper.Theme.YellowDim))
            g.DrawRectangle(pen, 0, 0, shot.Width - 1, shot.Height - 1);
        return shot;
    }
}

internal static class FocusCues
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Hides focus rings in the window, as if only the mouse had been used (WM_CHANGEUISTATE, UIS_SET, UISF_HIDEFOCUS).</summary>
    public static void Hide(IntPtr window) => SendMessage(window, 0x0127, (IntPtr)((1 << 16) | 1), IntPtr.Zero);
}
