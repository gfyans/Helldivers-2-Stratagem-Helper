namespace StratagemHelper;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool startInTray = args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        bool justUpdated = args.Contains("--updated", StringComparer.OrdinalIgnoreCase);

        using var mutex = new Mutex(true, "StratagemHelper_SingleInstance", out bool isNew);
        if (!isNew)
        {
            // After an update the previous version is still closing: wait for it rather than refusing to start.
            bool acquired = false;
            if (justUpdated)
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
                catch (AbandonedMutexException) { acquired = true; }
            }
            if (!acquired)
            {
                MessageBox.Show("Stratagem Helper is already running (check the system tray).",
                    "Stratagem Helper", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        Updater.CleanUp();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(startInTray, justUpdated));
    }
}
