namespace StratagemHelper;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool startInTray = args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        bool justUpdated = args.Contains("--updated", StringComparer.OrdinalIgnoreCase);
        bool restarting = justUpdated || args.Contains("--restart", StringComparer.OrdinalIgnoreCase);

        using var mutex = new Mutex(true, "StratagemHelper_SingleInstance", out bool isNew);
        if (!isNew)
        {
            // After an update or a restart the previous copy is still closing: wait for it rather than refusing to start.
            bool acquired = false;
            if (restarting)
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
                catch (AbandonedMutexException) { acquired = true; }
            }
            if (!acquired)
            {
                MessageBox.Show("Stratagem Helper is already running. Look for it in the system tray.",
                    "Stratagem Helper", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        Updater.CleanUp();
        ApplicationConfiguration.Initialize();

        // Text size and High Contrast are decided before any window is built.
        var settings = Storage.LoadSettings();
        Theme.Init(settings.TextSize / 100f, SystemInformation.HighContrast);

        Application.Run(new MainForm(startInTray, justUpdated));
    }

    /// <summary>Starts a fresh copy (which waits for this one to close). The caller then exits.</summary>
    public static void Restart(bool startHidden)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Environment.ProcessPath!, startHidden ? "--restart --tray" : "--restart") { UseShellExecute = false });
    }
}
