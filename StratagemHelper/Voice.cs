using System.Globalization;
using System.Speech.Recognition;
using System.Text.RegularExpressions;

namespace StratagemHelper;

/// <summary>
/// Offline voice commands using the Windows desktop speech recogniser. Listens only for a fixed grammar built
/// from the current loadout (every unambiguous way of saying each name + "slot one".."slot nine"), which keeps
/// it fast and accurate.
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

    /// <summary>
    /// One word (or number, or unit) of a name, with every way of saying it. Alts[0] is the most natural form.
    /// A unit ("millimetre") is only ever said straight after its number.
    /// </summary>
    private sealed record Token(string[] Alts, bool IsUnit);

    /// <summary>Acronyms with a known spoken form. Other short words get both spelled-out and as-a-word forms.</summary>
    private static readonly Dictionary<string, string[]> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HE"] = new[] { "H E", "high explosive" },
        ["HMG"] = new[] { "H M G", "heavy machine gun" },
        ["SEAF"] = new[] { "seaf", "S E A F" },
    };

    /// <summary>"380" -> "three eighty", "three hundred eighty", "three hundred and eighty".</summary>
    private static string[] NumberAlts(int n)
    {
        var alts = new List<string>();
        bool split = n > 100 && n < 1000 && n % 100 != 0;
        if (split) alts.Add(Ones[n / 100] + " " + (n % 100 < 10 ? "oh " + Ones[n % 100] : NumberWords(n % 100)));
        alts.Add(NumberWords(n));
        if (split) alts.Add(Ones[n / 100] + " hundred and " + NumberWords(n % 100));
        return alts.Distinct().ToArray();
    }

    private static string[] WordAlts(string w)
    {
        if (Acronyms.TryGetValue(w, out var known)) return known;
        if (w.Length is >= 2 and <= 3 && w.All(char.IsLetter))
        {
            // Learned names arrive title-cased ("He", "Sos"), so case alone can't tell an acronym from a word: offer both.
            var letters = string.Join(' ', w.ToUpperInvariant().ToCharArray());
            var word = w.ToLowerInvariant();
            return w.All(char.IsUpper) ? new[] { letters, word } : new[] { word, letters };
        }
        return new[] { w.ToLowerInvariant() };
    }

    private static List<Token> Tokens(string name)
    {
        var tokens = new List<Token>();
        foreach (var raw in MenuReader.StripDesignation(name).Replace("\"", "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Regex.Match(raw, @"^(\d+)(kg|mm)?$", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
            {
                tokens.Add(new Token(NumberAlts(n), false));
                switch (m.Groups[2].Value.ToLowerInvariant())
                {
                    case "kg": tokens.Add(new Token(new[] { "kilogram", "kilo", "K G" }, true)); break;
                    case "mm": tokens.Add(new Token(new[] { "millimetre", "millimeter", "mill", "M M" }, true)); break;
                }
                continue;
            }
            // "Anti-Tank" is said as two words.
            var parts = Regex.Split(raw, "[-/]").Select(p => Regex.Replace(p, @"[^A-Za-z0-9']", "")).Where(p => p.Length > 0).ToList();
            if (parts.Count == 1) tokens.Add(new Token(WordAlts(parts[0]), false));
            else if (parts.Count > 1) tokens.Add(new Token(new[] { string.Join(' ', parts.Select(p => p.ToLowerInvariant())) }, false));
        }
        return tokens;
    }

    /// <summary>"Eagle 500KG Bomb" -> "eagle five hundred kilogram bomb"; "SOS Beacon" -> "S O S beacon".</summary>
    public static string Spoken(string name) => string.Join(' ', Tokens(name).Select(t => t.Alts[0]));

    /// <summary>A way of saying (part of) a name. Lower Rank = a more natural thing to suggest.</summary>
    private readonly record struct Candidate(string Phrase, bool Full, (int tokens, int alts, int first) Rank);

    /// <summary>
    /// Every way of saying a name or part of it: any in-order selection of its words ("orbital barrage",
    /// "three eighty"), with each word in each of its spoken forms.
    /// </summary>
    private static IEnumerable<Candidate> Candidates(string name)
    {
        var t = Tokens(name);
        if (t.Count == 0) yield break;

        IEnumerable<int[]> Selections()
        {
            if (t.Count <= 7)
            {
                for (int mask = 1; mask < 1 << t.Count; mask++)
                    yield return Enumerable.Range(0, t.Count).Where(i => (mask & (1 << i)) != 0).ToArray();
            }
            else
            {
                // A long (probably misread) name: only runs of consecutive words, to keep the grammar small.
                for (int a = 0; a < t.Count; a++)
                    for (int b = a; b < t.Count; b++)
                        yield return Enumerable.Range(a, b - a + 1).ToArray();
            }
        }

        foreach (var sel in Selections())
        {
            if (sel.All(i => t[i].IsUnit)) continue;
            if (sel.Any(i => t[i].IsUnit && !sel.Contains(i - 1))) continue;   // "millimetre" only after its number
            bool full = sel.Length == t.Count;

            var words = new string[sel.Length];
            IEnumerable<Candidate> Expand(int k, int altSum)
            {
                if (k == sel.Length)
                {
                    yield return new Candidate(string.Join(' ', words), full, (sel.Length, altSum, sel[0]));
                    yield break;
                }
                var alts = t[sel[k]].Alts;
                for (int a = 0; a < alts.Length; a++)
                {
                    words[k] = alts[a];
                    foreach (var c in Expand(k + 1, altSum + a)) yield return c;
                }
            }
            foreach (var c in Expand(0, 0)) yield return c;
        }
    }

    /// <summary>
    /// Works out which phrases pick out exactly one slot. A full name beats part of another name, so "autocannon"
    /// means the Autocannon even when an Autocannon Sentry is also in the loadout. Any other phrase that fits
    /// more than one slot is dropped.
    /// </summary>
    private static (Dictionary<string, int> map, string?[] hints) Resolve(IReadOnlyList<string?> slotNames)
    {
        var best = new Dictionary<string, (int prio, int slot, bool clash)>(StringComparer.OrdinalIgnoreCase);
        var perSlot = new List<Candidate>[9];

        void Add(string phrase, int prio, int slot)
        {
            if (!best.TryGetValue(phrase, out var cur) || prio < cur.prio) best[phrase] = (prio, slot, false);
            else if (prio == cur.prio && slot != cur.slot) best[phrase] = cur with { clash = true };
        }

        for (int i = 0; i < slotNames.Count && i < 9; i++)
        {
            Add("slot " + Ones[i + 1], 0, i + 1);
            perSlot[i] = slotNames[i] is string name ? Candidates(name).ToList() : new();
            foreach (var c in perSlot[i]) Add(c.Phrase, c.Full ? 0 : 1, i + 1);
        }

        var map = best.Where(kv => !kv.Value.clash).ToDictionary(kv => kv.Key, kv => kv.Value.slot, StringComparer.OrdinalIgnoreCase);

        var hints = new string?[9];
        for (int i = 0; i < 9 && perSlot[i] != null; i++)
            hints[i] = perSlot[i].Where(c => map.TryGetValue(c.Phrase, out int s) && s == i + 1)
                                 .OrderBy(c => c.Rank).Select(c => c.Phrase).FirstOrDefault();
        return (map, hints);
    }

    /// <summary>Phrase -> slot for the loadout: every unambiguous way of saying each name, and "slot one".."slot nine".</summary>
    public static Dictionary<string, int> PhrasesFor(IReadOnlyList<string?> slotNames) => Resolve(slotNames).map;

    /// <summary>For each slot, the shortest natural phrase that calls it in with this loadout (null for an empty slot).</summary>
    public static string?[] HintsFor(IReadOnlyList<string?> slotNames) => Resolve(slotNames).hints;
}
