using System.Globalization;
using System.Text.Json;

namespace StratagemHelper;

/// <summary>Keypresses saved per day, stored in stats.json next to the exe.</summary>
internal sealed class Stats
{
    public sealed class Day
    {
        public int CallIns { get; set; }
        public int KeysSaved { get; set; }
    }

    public Dictionary<string, Day> Days { get; set; } = new();

    // Reused: a new options object each time makes System.Text.Json rebuild its serializer on every call-in.
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static string PathName => System.IO.Path.Combine(Storage.Folder, "stats.json");
    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static Stats Load()
    {
        try
        {
            if (File.Exists(PathName))
                return JsonSerializer.Deserialize<Stats>(File.ReadAllText(PathName)) ?? new Stats();
        }
        catch { }
        return new Stats();
    }

    private void Save()
    {
        try { File.WriteAllText(PathName, JsonSerializer.Serialize(this, Json)); } catch { }
    }

    /// <summary>How a call-in was triggered, which decides how many keypresses it saved.</summary>
    public enum Via { CtrlNumber, OneTouch, Voice }

    /// <summary>
    /// Manual entry = hold Ctrl + each arrow. Ctrl+N replaces the arrows with one key (code - 1 saved);
    /// one-touch replaces Ctrl + arrows with one key (code saved); voice needs no keys at all (code + 1 saved).
    /// </summary>
    public static int KeysSavedFor(string code, Via via) => via switch
    {
        Via.CtrlNumber => Math.Max(0, code.Length - 1),
        Via.OneTouch => code.Length,
        _ => code.Length + 1,
    };

    public int Record(string code, Via via)
    {
        int saved = KeysSavedFor(code, via);
        lock (this)
        {
            var key = Key(DateTime.Now);
            if (!Days.TryGetValue(key, out var d)) Days[key] = d = new Day();
            d.CallIns++;
            d.KeysSaved += saved;
            Save();
        }
        return saved;
    }

    public (int callIns, int keys) Sum(int? lastDays)
    {
        lock (this)
        {
            var since = lastDays is int n ? DateTime.Today.AddDays(-(n - 1)) : DateTime.MinValue;
            int c = 0, k = 0;
            foreach (var (dateKey, d) in Days)
                if (DateTime.TryParseExact(dateKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) && dt >= since)
                {
                    c += d.CallIns;
                    k += d.KeysSaved;
                }
            return (c, k);
        }
    }
}
