// Runs the app's MenuReader against saved full-screen captures.
//   dotnet run -- <capture.png> [...]          report what is detected
//   dotnet run -- --learn <capture.png> [...]  build the arrow template from rows whose code is known
using System.Drawing;
using StratagemHelper;

// Same Windows Forms setup as the app (ApplicationConfiguration.Initialize there), so windows and screenshots
// match it. Without SetCompatibleTextRenderingDefault(false), labels draw with GDI+, which spaces text wider.
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.SetHighDpiMode(HighDpiMode.SystemAware);

if (args.Contains("--voice")) return FeatureTests.Voice() + FeatureTests.Stats();
if (args.Contains("--keys")) return FeatureTests.Keys();
if (args.Contains("--overlay")) { FeatureTests.Overlay(6); return 0; }
if (args.Length == 2 && args[0] == "--sounds")
{
    // Writes the synthesised cues as WAV files and reports length / peak, without playing them.
    foreach (var (name, wave) in new[] { ("success", Sounds.SuccessWave()), ("failure", Sounds.FailureWave()) })
    {
        var path = Path.Combine(args[1], name + ".wav");
        File.WriteAllBytes(path, Sounds.ToWav(wave));
        Console.WriteLine($"{name}: {wave.Length * 1000 / 44100} ms, peak {wave.Max(Math.Abs):0.00}, {new FileInfo(path).Length / 1024} KB -> {path}");
    }
    return 0;
}
if (args.Length == 4 && args[0] == "--form")
{
    // Shows a window with a given text scale and High Contrast on/off for screenshots: --form first|settings <scale> <0|1>
    Theme.Init(float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args[3] == "1");
    Form f;
    if (args[1] == "first") f = new FirstRunDialog(Theme.MakeIcon(), false, Storage.LoadSettings());
    else
    {
        // Same loadout as the main window image (settings.json next to this exe), with voice always listening.
        var demo = Storage.LoadSettings();
        demo.VoiceMode = 2;
        demo.ShowOverlay = demo.SoundCues = true; // as a new install has them
        var sf = new SettingsForm(demo, true, 64, Theme.MakeIcon());
        var hints = Voice.HintsFor(demo.Slots).Where(h => h != null).Select(h => $"“{h}”");
        sf.SetVoiceInfo($"Listening all the time. Say “call in” and then: {string.Join(", ", hints)}. Fuller names and slot numbers work too.");
        f = sf;
    }
    f.StartPosition = FormStartPosition.CenterScreen;
    if (Environment.GetEnvironmentVariable("DUMP_LAYOUT") == "1")
        f.Shown += (_, _) => { void Dump(Control c, int d) { Console.WriteLine($"{new string(' ', d * 2)}{c.GetType().Name} {c.Bounds} min={c.MinimumSize} max={c.MaximumSize} \"{(c.Text.Length > 30 ? c.Text[..30] : c.Text)}\""); foreach (Control k in c.Controls) Dump(k, d + 1); } Console.WriteLine($"Form client {f.ClientSize} size {f.Size} min={f.MinimumSize}"); Dump(f.Controls[0], 0); };
    var shot = Environment.GetEnvironmentVariable("SHOT_OUT");
    if (shot != null)
        f.Shown += async (_, _) =>
        {
            // Optional: SHOT_TAB=<n> opens settings tab n before capturing.
            f.Activate();
            await Task.Delay(800);
            // Select the tab directly: keystrokes would go to whatever window is in front.
            int tab = int.Parse(Environment.GetEnvironmentVariable("SHOT_TAB") ?? "0");
            if (tab > 0 && typeof(SettingsForm).GetField("_tabs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(f) is HdTabs tabs)
                tabs.Selected = tab;
            FocusCues.Hide(f.Handle); // no focus rings, as if only the mouse had been used
            await Task.Delay(700);
            using var bmp = WindowShot.Capture(f.Handle);
            bmp.Save(shot);
            Console.WriteLine($"Saved {shot}");
            f.Close();
        };
    var t = new System.Windows.Forms.Timer { Interval = 8000 }; t.Tick += (_, _) => f.Close(); t.Start();
    Application.Run(f);
    return 0;
}
if (args.Length == 2 && args[0] == "--shot-main")
{
    // README image of the main window. Uses settings.json / stats.json next to this exe: --shot-main <out.png>
    Theme.Init(1f, false);
    var f = new MainForm { StartPosition = FormStartPosition.CenterScreen, TopMost = true };
    f.Shown += async (_, _) =>
    {
        await Task.Delay(1500);
        if (Environment.GetEnvironmentVariable("SHOT_HOVER") == "1") { f.Activate(); Cursor.Position = new Point(f.Right - 80, f.Top + 70); await Task.Delay(500); } // check hover colours
        if (Environment.GetEnvironmentVariable("SHOT_TAB") == "1") { f.Activate(); SendKeys.Send("{TAB}"); await Task.Delay(500); } // check keyboard focus rings
        using var bmp = WindowShot.Capture(f.Handle);
        bmp.Save(args[1]);
        Console.WriteLine($"Saved {args[1]} ({bmp.Width}x{bmp.Height})");
        var tray = typeof(MainForm).GetField("_tray", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(f) as NotifyIcon;
        if (tray != null) tray.Visible = false;
        Environment.Exit(0);
    };
    Application.Run(f);
    return 0;
}
if (args.Contains("--firstrun")) { using var d = new FirstRunDialog(Theme.MakeIcon(), false, Storage.LoadSettings()); Console.WriteLine("Result: " + d.ShowDialog()); return 0; }
if (args.Length == 4 && args[0] == "--render-overlay")
{
    // README image: the loadout panel composited beside the real (open) menu on a capture.
    // Usage: --render-overlay <capture.png> <out.png> <crop width>x<crop height>
    using var cap = new Bitmap(args[1]);
    var size = args[3].Split('x').Select(int.Parse).ToArray();
    using var ov = new LoadoutOverlay();
    ov.SetRows(new string?[] { "Reinforce", "SOS Beacon", "Resupply", "Eagle Cluster Bomb", "EAT-17 Expendable Anti-Tank", "Nova", "A/AC-8 Autocannon Sentry", null, null });
    var (img, bounds) = ov.Render(new Rectangle(0, 0, cap.Width, cap.Height), menuOpen: true);
    if (Environment.GetEnvironmentVariable("OVERLAY_RAW") is string raw) img.Save(raw); // the panel alone, as drawn ({img.Size} at {bounds})
    Console.WriteLine($"Panel {img.Width}x{img.Height} at {bounds}");
    if (typeof(LoadoutOverlay).GetField("_badgeFont", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(ov) is Font bf)
        Console.WriteLine($"Badge font {bf.Size}{bf.Unit}: ink of \"1\" = {Theme.Ink(bf, "1")}, of \"H\" = {Theme.Ink(bf)}");
    using var outBmp = new Bitmap(size[0], size[1]);
    using (var g = Graphics.FromImage(outBmp))
    {
        g.DrawImage(cap, new Rectangle(0, 0, size[0], size[1]), new Rectangle(0, 0, size[0], size[1]), GraphicsUnit.Pixel);
        var attrs = new System.Drawing.Imaging.ImageAttributes();
        attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.88f }); // same opacity as in game
        g.DrawImage(img, bounds, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);
    }
    outBmp.Save(args[2]);
    img.Dispose();
    Console.WriteLine($"Saved {args[2]}");
    return 0;
}
var db = DefaultStratagems.All;
bool learn = args.Contains("--learn");
var files = args.Where(a => a != "--learn").ToArray();

if (learn)
{
    const int N = MenuReader.GlyphSize;
    var sum = new double[N * N];
    int count = 0;
    foreach (var path in files)
    {
        using var full = new Bitmap(path);
        var region = MenuReader.MenuRegion(new Rectangle(0, 0, full.Width, full.Height));
        using var crop = full.Clone(region, full.PixelFormat);
        foreach (var (name, glyphs) in await MenuReader.ReadGlyphsAsync(crop, full.Height))
        {
            var s = MenuReader.Match(name, db);
            if (s == null || glyphs == null || glyphs.Count != s.Code.Length) continue;
            for (int i = 0; i < glyphs.Count; i++)
            {
                var up = MenuReader.ToUp(glyphs[i], s.Code[i]);
                for (int k = 0; k < up.Length; k++) sum[k] += up[k];
                count++;
            }
        }
    }
    Console.WriteLine($"Learned from {count} arrows:");
    var tpl = string.Concat(sum.Select(v => (char)('0' + (int)Math.Round(v / count * 9))));
    for (int y = 0; y < N; y++) Console.WriteLine("  " + tpl.Substring(y * N, N));
    Console.WriteLine("TEMPLATE=" + tpl);
    return 0;
}

int failures = 0;
foreach (var path in files)
{
    using var full = new Bitmap(path);
    var region = MenuReader.MenuRegion(new Rectangle(0, 0, full.Width, full.Height));
    using var crop = full.Clone(region, full.PixelFormat);

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var rows = await MenuReader.ReadAsync(crop, full.Height);
    sw.Stop();

    Console.WriteLine($"== {Path.GetFileName(path)}  ({rows.Count} rows, {sw.ElapsedMilliseconds} ms)");
    for (int i = 0; i < rows.Count; i++)
    {
        var r = rows[i];
        var match = MenuReader.Match(r.Name, db);
        string verdict =
            match == null && r.ScreenCode != null ? $"NEW - app learns it as {r.ScreenCode}" :
            match == null ? "NO MATCH" :
            r.ScreenCode == null ? "(no arrows read)" :
            r.ScreenCode == match.Code ? "arrows OK" : $"ARROWS DIFFER (db {match.Code})";
        if (verdict.StartsWith("NO") || verdict.StartsWith("ARROWS DIFFER")) failures++;
        Console.WriteLine($"  Ctrl+{i + 1}  {r.Name,-22} -> {match?.Name ?? "?",-30} screen={r.ScreenCode ?? "-",-9} {verdict}");
    }
}
Console.WriteLine(failures == 0 ? "ALL GOOD" : $"{failures} problem(s)");
return failures == 0 ? 0 : 1;
