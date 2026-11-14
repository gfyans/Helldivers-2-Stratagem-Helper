namespace StratagemHelper;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool startInTray = args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        using var mutex = new Mutex(true, "StratagemHelper_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("Stratagem Helper is already running (check the system tray).",
                "Stratagem Helper", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(startInTray));
    }
}
