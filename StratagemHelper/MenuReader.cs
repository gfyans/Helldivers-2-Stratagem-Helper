using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace StratagemHelper;

/// <summary>One row of the in-game stratagem menu, top to bottom.</summary>
public sealed record DetectedRow(string Name, string? ScreenCode);

/// <summary>
/// Reads the Helldivers 2 stratagem menu from screen pixels only (never touches the game process).
/// Layout was measured at 2560x1440; positions are expressed as fractions of the screen height
/// because the HUD is anchored top-left and scales with height.
/// </summary>
public static class MenuReader
{
    // Region that contains the stratagem list, as fractions of screen height. The list moves up when the
    // "STRATAGEMS" header is hidden, so the region starts high and the header text is filtered out.
    private const double RegionX = 0.090, RegionY = 0.025, RegionW = 0.36, RegionH = 0.50;

    // Arrow row relative to the top of the name text, as fractions of screen height.
    private const double ArrowBandTop = 0.016, ArrowBandBottom = 0.038;

    private const int OcrScale = 2;
    private static OcrEngine? _engine;

    /// <summary>The part of a screen (in pixels) that holds the menu.</summary>
    public static Rectangle MenuRegion(Rectangle screen)
    {
        double h = screen.Height;
        return new Rectangle(screen.X + (int)(h * RegionX), screen.Y + (int)(h * RegionY),
                             (int)(h * RegionW), (int)(h * RegionH));
    }

    /// <summary>Captures the menu region of the monitor the foreground window is on.</summary>
    public static (Bitmap image, int screenHeight)? CaptureMenuRegion()
    {
        // Use physical pixels regardless of the app's own DPI mode.
        var old = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        try
        {
            var hwnd = Native.GetForegroundWindow();
            var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return null;
            var screen = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
            var region = MenuRegion(screen);

            var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(region.Location, Point.Empty, region.Size);
            return (bmp, screen.Height);
        }
        finally
        {
            SetThreadDpiAwarenessContext(old);
        }
    }

    /// <summary>Monitor rectangle (physical pixels) of a window, whatever the app's own DPI mode.</summary>
    public static Rectangle? MonitorOf(IntPtr hwnd)
    {
        var old = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        try
        {
            var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return null;
            return Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
        }
        finally { SetThreadDpiAwarenessContext(old); }
    }

    /// <summary>Reads the rows from an image of the menu region.</summary>
    public static async Task<List<DetectedRow>> ReadAsync(Bitmap region, int screenHeight)
    {
        var rows = await ReadNamePositionsAsync(region);
        using var pixels = new PixelReader(region);
        return rows.Select(r => new DetectedRow(r.name, ReadArrows(pixels, r.x, r.y, screenHeight))).ToList();
    }

    private static async Task<List<(double y, double x, string name)>> ReadNamePositionsAsync(Bitmap region)
    {
        _engine ??= OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
                    ?? OcrEngine.TryCreateFromUserProfileLanguages();
        if (_engine == null) throw new InvalidOperationException("Windows text recognition (OCR) is not available.");

        using var scaled = new Bitmap(region.Width * OcrScale, region.Height * OcrScale, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(region, new Rectangle(0, 0, scaled.Width, scaled.Height));
        }

        var result = await _engine.RecognizeAsync(await ToSoftwareBitmap(scaled));

        var rows = new List<(double y, double x, string name)>();
        foreach (var line in result.Lines)
        {
            var text = line.Text.Trim();
            if (!LooksLikeName(text)) continue;
            var box = line.Words[0].BoundingRect;
            rows.Add((box.Y / OcrScale, box.X / OcrScale, text));
        }
        rows.Sort((a, b) => a.y.CompareTo(b.y));
        return rows;
    }

    /// <summary>Stratagem names are shown in capitals; "Ready", cooldown timers etc. are not.</summary>
    private static bool LooksLikeName(string text)
    {
        int letters = text.Count(char.IsLetter);
        // Menu header, possibly cut off or misread ("•RATAGEMS").
        var n = Norm(text);
        if (n.Contains("TAGEM") || Levenshtein(n, "STRATAGEMS") <= 3) return false;
        return letters >= 3 && text.Where(char.IsLetter).All(char.IsUpper);
    }

    // ---------------- Arrow reading ----------------

    private static string? ReadArrows(PixelReader px, double nameX, double nameY, int screenHeight)
    {
        var glyphs = ExtractGlyphs(px, nameX, nameY, screenHeight);
        if (glyphs == null || glyphs.Count is < 3 or > 8) return null;
        var code = new System.Text.StringBuilder();
        foreach (var g in glyphs)
        {
            char? dir = Classify(g);
            if (dir == null) return null;
            code.Append(dir.Value);
        }
        return code.ToString();
    }

    public const int GlyphSize = 12;
    private const string Dirs = "UDLR";

    /// <summary>Average "up" arrow (GlyphSize x GlyphSize, 0-9 coverage), learned from real captures.</summary>
    private const string UpTemplate = 
        "000015510000" +
        "001028820100" +
        "000178872000" +
        "001688886110" +
        "004888888400" +
        "038888888830" +
        "178989889871" +
        "466788888664" +
        "111588887211" +
        "000488887200" +
        "000488887100" +
        "000488887100";

    private static float[]? _up;

    private static char? Classify(float[] glyph)
    {
        if (UpTemplate.Length != GlyphSize * GlyphSize) return null; // not trained
        _up ??= UpTemplate.Select(c => (c - '0') / 9f).ToArray();

        char best = '?';
        double bestErr = double.MaxValue, second = double.MaxValue;
        foreach (char d in Dirs)
        {
            var t = FromUp(_up, d);
            double err = 0;
            for (int i = 0; i < t.Length; i++) err += Math.Abs(t[i] - glyph[i]);
            if (err < bestErr) { second = bestErr; bestErr = err; best = d; }
            else if (err < second) second = err;
        }
        // Reject ambiguous shapes (e.g. not actually an arrow).
        return bestErr < second * 0.8 ? best : null;
    }

    /// <summary>Turns an up-pointing glyph into one pointing in direction d.</summary>
    public static float[] FromUp(float[] up, char d)
    {
        const int N = GlyphSize;
        var r = new float[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                r[y * N + x] = d switch
                {
                    'U' => up[y * N + x],
                    'D' => up[(N - 1 - y) * N + (N - 1 - x)],
                    'L' => up[x * N + y],
                    _ => up[(N - 1 - x) * N + y], // 'R'
                };
        return r;
    }

    /// <summary>Turns a glyph pointing in direction d into an up-pointing one (inverse of FromUp).</summary>
    public static float[] ToUp(float[] g, char d)
    {
        const int N = GlyphSize;
        var r = new float[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                r[y * N + x] = d switch
                {
                    'U' => g[y * N + x],
                    'D' => g[(N - 1 - y) * N + (N - 1 - x)],
                    'L' => g[x * N + y],
                    _ => g[x * N + (N - 1 - y)], // 'R'
                };
        return r;
    }

    /// <summary>For training: the arrow glyphs under each detected name.</summary>
    public static async Task<List<(string name, List<float[]>? glyphs)>> ReadGlyphsAsync(Bitmap region, int screenHeight)
    {
        var rows = await ReadNamePositionsAsync(region);
        using var px = new PixelReader(region);
        return rows.Select(r => (r.name, ExtractGlyphs(px, r.x, r.y, screenHeight))).ToList();
    }

    private static List<float[]>? ExtractGlyphs(PixelReader px, double nameX, double nameY, int screenHeight)
    {
        double h = screenHeight;
        int y0 = (int)(nameY + h * ArrowBandTop), y1 = (int)(nameY + h * ArrowBandBottom);
        int x0 = Math.Max(0, (int)(nameX - h * 0.006));
        y0 = Math.Clamp(y0, 0, px.Height - 1);
        y1 = Math.Clamp(y1, 0, px.Height - 1);
        if (y1 - y0 < 4) return null;

        int minBlob = (int)(h * 0.006), maxBlob = (int)(h * 0.025), maxGap = (int)(h * 0.02);

        // Column projection of bright pixels -> separate arrow blobs.
        var blobs = new List<(int a, int b)>();
        int start = -1, lastBright = -1;
        for (int x = x0; x < px.Width; x++)
        {
            bool any = false;
            for (int y = y0; y <= y1 && !any; y++) any = px.Bright(x, y);
            if (any)
            {
                if (start < 0) start = x;
                lastBright = x;
            }
            else if (start >= 0 && x - lastBright >= 2)
            {
                blobs.Add((start, lastBright));
                start = -1;
            }
            if (blobs.Count > 0 && start < 0 && x - lastBright > maxGap) break; // end of the arrow row
            if (blobs.Count == 0 && start < 0 && x - x0 > h * 0.03) return null; // no arrows (timer / "Ready")
        }
        if (start >= 0) blobs.Add((start, lastBright));

        var glyphs = new List<float[]>();
        foreach (var (a, b) in blobs)
        {
            int w = b - a + 1;
            if (w < minBlob || w > maxBlob) return null;
            var g = MakeGlyph(px, a, b, y0, y1);
            if (g == null) return null;
            glyphs.Add(g);
        }
        return glyphs;
    }

    /// <summary>Samples a blob into a GlyphSize² grid of bright-pixel coverage, keeping its aspect ratio.</summary>
    private static float[]? MakeGlyph(PixelReader px, int xa, int xb, int y0, int y1)
    {
        int top = -1, bottom = -1;
        for (int y = y0; y <= y1; y++)
            for (int x = xa; x <= xb; x++)
                if (px.Bright(x, y)) { if (top < 0) top = y; bottom = y; break; }
        if (top < 0 || bottom - top < 4) return null;

        // Square box centred on the blob.
        double side = Math.Max(xb - xa + 1, bottom - top + 1);
        double cx = (xa + xb + 1) / 2.0, cy = (top + bottom + 1) / 2.0;
        double bx = cx - side / 2, by = cy - side / 2, cell = side / GlyphSize;

        var g = new float[GlyphSize * GlyphSize];
        const int sub = 4; // 4x4 samples per cell
        for (int gy = 0; gy < GlyphSize; gy++)
            for (int gx = 0; gx < GlyphSize; gx++)
            {
                int hits = 0;
                for (int sy = 0; sy < sub; sy++)
                    for (int sx = 0; sx < sub; sx++)
                    {
                        int x = (int)(bx + (gx + (sx + 0.5) / sub) * cell);
                        int y = (int)(by + (gy + (sy + 0.5) / sub) * cell);
                        if (x >= 0 && y >= 0 && x < px.Width && y < px.Height && px.Bright(x, y)) hits++;
                    }
                g[gy * GlyphSize + gx] = hits / (float)(sub * sub);
            }
        return g;
    }

    private sealed class PixelReader : IDisposable
    {
        private readonly byte[] _data;
        private readonly int _stride;
        public int Width { get; }
        public int Height { get; }

        public PixelReader(Bitmap bmp)
        {
            Width = bmp.Width; Height = bmp.Height;
            var bd = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            _stride = bd.Stride;
            _data = new byte[_stride * Height];
            Marshal.Copy(bd.Scan0, _data, 0, _data.Length);
            bmp.UnlockBits(bd);
        }

        public int Max(int x, int y)
        {
            int i = y * _stride + x * 4;
            return Math.Max(_data[i], Math.Max(_data[i + 1], _data[i + 2]));
        }

        public bool Bright(int x, int y)
        {
            int i = y * _stride + x * 4;
            return (_data[i] + _data[i + 1] + _data[i + 2]) > 3 * 170;
        }

        public void Dispose() { }
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmap(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Bmp);
        var ras = new InMemoryRandomAccessStream();
        await ras.WriteAsync(ms.ToArray().AsBuffer());
        ras.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ras);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    // ---------------- Name matching ----------------

    /// <summary>Finds the stratagem whose name best matches what the game displays (e.g. "AUTOCANNON" ↔ "AC-8 Autocannon").</summary>
    public static Stratagem? Match(string shown, IEnumerable<Stratagem> list)
    {
        string target = Norm(shown);
        Stratagem? best = null;
        int bestDist = int.MaxValue;
        foreach (var s in list)
        {
            string full = Norm(s.Name), stripped = Norm(StripDesignation(s.Name));
            if (full == target || stripped == target) return s;
            int d = Math.Min(Levenshtein(full, target), Levenshtein(stripped, target));
            if (d < bestDist) { bestDist = d; best = s; }
        }
        int allowed = Math.Max(1, target.Length / 8);
        return bestDist <= allowed ? best : null;
    }

    /// <summary>"AC-8 Autocannon" -> "Autocannon", "AX/AR-23 "Guard Dog"" -> "Guard Dog".</summary>
    public static string StripDesignation(string name)
    {
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (tokens.Count > 1 && tokens[0].Any(char.IsDigit)) tokens.RemoveAt(0);
        return string.Join(' ', tokens);
    }

    public static string Norm(string s)
    {
        var chars = s.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    // ---------------- Win32 ----------------

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
}
