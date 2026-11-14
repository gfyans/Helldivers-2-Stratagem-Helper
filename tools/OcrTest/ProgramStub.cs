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
