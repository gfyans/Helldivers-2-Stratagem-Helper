using Microsoft.Win32;

namespace StratagemHelper;

/// <summary>"Start with Windows" via the current user's Run key.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "StratagemHelper";
    private static string Command => $"\"{Environment.ProcessPath}\" --tray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(RunValue, Command);
        else key.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    /// <summary>If startup is on, make sure it points at this exe (in case the app was moved).</summary>
    public static void RefreshPath()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key.GetValue(RunValue) is string current && current != Command) key.SetValue(RunValue, Command);
        }
        catch { }
    }
}
