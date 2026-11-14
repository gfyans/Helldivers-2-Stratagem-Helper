using System.Globalization;
using System.Speech.Recognition;
using System.Text.RegularExpressions;

namespace StratagemHelper;

/// <summary>
/// Offline voice commands using the Windows desktop speech recogniser. Listens only for a small grammar
/// (the current loadout's names + "slot one".."slot nine"), which keeps it fast and accurate.
/// </summary>
internal sealed class Voice : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private Dictionary<string, int>? _pending;
    private readonly object _lock = new();

    /// <summary>(slot, phrase, confidence 0-1). Raised on a recogniser thread.</summary>
    public event Action<int, string, float>? Heard;

    public string? RecognizerName { get; private set; }
    public bool Running => _engine != null;

    /// <summary>Starts listening. Returns an error message, or null on success.</summary>
    /// <param name="listen">False for tests: load the grammar but don't open the microphone.</param>
    public string? Start(Dictionary<string, int> phrases, bool listen = true)
    {
        Stop();
        try
        {
            var installed = SpeechRecognitionEngine.InstalledRecognizers();
            if (installed.Count == 0)
                return "No Windows speech recogniser is installed (Settings › Time & language › Speech).";

            var ui = CultureInfo.CurrentUICulture;
            var info = installed.FirstOrDefault(r => r.Culture.Name == ui.Name)
                       ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en")
                       ?? installed[0];

            var engine = new SpeechRecognitionEngine(info);
            if (listen) engine.SetInputToDefaultAudioDevice();
            else engine.SetInputToNull();
            engine.LoadGrammar(BuildGrammar(phrases, info.Culture));
            engine.SpeechRecognized += OnRecognized;
            engine.RecognizerUpdateReached += OnUpdateReached;
            if (listen) engine.RecognizeAsync(RecognizeMode.Multiple);

            _engine = engine;
            RecognizerName = info.Culture.DisplayName;
            return null;
        }
        catch (Exception ex)
        {
            Stop();
            return "Voice unavailable: " + ex.Message;
        }
    }

    /// <summary>Swap the phrase list (e.g. after a new scan) without stopping.</summary>
    public void SetPhrases(Dictionary<string, int> phrases)
    {
        var engine = _engine;
        if (engine == null) return;
        lock (_lock) _pending = phrases;
        engine.RequestRecognizerUpdate();
    }

    private void OnUpdateReached(object? sender, RecognizerUpdateReachedEventArgs e)
    {
        Dictionary<string, int>? p;
        lock (_lock) { p = _pending; _pending = null; }
        if (p == null || _engine == null) return;
        _engine.UnloadAllGrammars();
        _engine.LoadGrammar(BuildGrammar(p, _engine.RecognizerInfo.Culture));
    }

    private void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (SlotOf(e.Result) is int slot)
            Heard?.Invoke(slot, e.Result!.Text, e.Result.Confidence);
    }

    /// <summary>The slot a recognition result refers to, if any.</summary>
    public static int? SlotOf(RecognitionResult? r) =>
        r?.Semantics != null && r.Semantics.ContainsKey("slot") && r.Semantics["slot"].Value is int s ? s : null;

    /// <summary>For testing: run a phrase through the grammar as if it were spoken.</summary>
    public RecognitionResult? Emulate(string text) => _engine?.EmulateRecognize(text);

    /// <summary>Every command must start with this, so everyday speech ("resupply!") never triggers a call-in.</summary>
    public const string Prefix = "call in";

    private static Grammar BuildGrammar(Dictionary<string, int> phrases, CultureInfo culture)
    {
        var choices = new Choices();
        foreach (var (phrase, slot) in phrases)
            choices.Add(new SemanticResultValue(phrase, slot));
        var gb = new GrammarBuilder(Prefix) { Culture = culture };
        gb.Append(new SemanticResultKey("slot", choices));
        return new Grammar(gb) { Name = "stratagems" };
    }

    public void Stop()
    {
        var e = _engine;
        _engine = null;
        if (e == null) return;
        try { e.RecognizeAsyncCancel(); } catch { }
        e.Dispose();
    }

    public void Dispose() => Stop();

    // ---------------- Phrase building ----------------

    private static readonly string[] Ones =
        { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
          "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
    private static readonly string[] Tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

    public static string NumberWords(int n)
    {
        if (n < 20) return Ones[n];
        if (n < 100) return Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "");
        if (n < 1000) return Ones[n / 100] + " hundred" + (n % 100 > 0 ? " " + NumberWords(n % 100) : "");
        return NumberWords(n / 1000) + " thousand" + (n % 1000 > 0 ? " " + NumberWords(n % 1000) : "");
    }

    /// <summary>"Eagle 500KG Bomb" -> "eagle five hundred kilogram bomb"; "SOS Beacon" -> "S O S beacon".</summary>
    public static string Spoken(string name)
    {
        var words = new List<string>();
        foreach (var raw in MenuReader.StripDesignation(name).Replace("\"", "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Regex.Match(raw, @"^(\d+)(kg|mm)?$", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
            {
                words.Add(NumberWords(n));
                var unit = m.Groups[2].Value.ToLowerInvariant();
                if (unit == "kg") words.Add("kilogram");
                if (unit == "mm") words.Add("millimetre");
                continue;
            }
            var token = Regex.Replace(raw, @"[^A-Za-z0-9']", "");
            if (token.Length == 0) continue;
            if (token.Length <= 3 && token.All(char.IsUpper))
                words.Add(string.Join(' ', token.ToCharArray()));      // acronyms: say the letters
            else
                words.Add(token.ToLowerInvariant());
        }
        return string.Join(' ', words);
    }

    /// <summary>
    /// A shorter way to say a long name: "eagle five hundred kilogram bomb" -> "eagle five hundred",
    /// "eagle cluster bomb" -> "eagle cluster". Null when there's no sensible short form.
    /// </summary>
    public static string? ShortForm(string spoken)
    {
        var parts = spoken.Split(' ');
        int unit = Array.FindIndex(parts, p => p is "kilogram" or "millimetre");
        if (unit > 1) return string.Join(' ', parts.Take(unit));
        if (parts.Length < 3) return null;
        bool IsWord(string p) => p.Length > 1 && !Ones.Contains(p) && !Tens.Contains(p) && p != "hundred";
        return IsWord(parts[0]) && IsWord(parts[1]) ? parts[0] + " " + parts[1] : null;
    }

    /// <summary>Phrase -> slot for the loadout: full names, two-word short forms, and "slot one".."slot nine".</summary>
    public static Dictionary<string, int> PhrasesFor(IReadOnlyList<string?> slotNames)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var clashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string phrase, int slot)
        {
            if (string.IsNullOrWhiteSpace(phrase) || clashes.Contains(phrase)) return;
            if (map.TryGetValue(phrase, out int existing) && existing != slot)
            {
                map.Remove(phrase);
                clashes.Add(phrase);   // ambiguous: drop it
                return;
            }
            map[phrase] = slot;
        }

        for (int i = 0; i < slotNames.Count && i < 9; i++)
        {
            Add("slot " + Ones[i + 1], i + 1);
            if (slotNames[i] is not string name) continue;
            var full = Spoken(name);
            Add(full, i + 1);
            if (ShortForm(full) is string shortForm) Add(shortForm, i + 1);
        }
        return map;
    }
}
