using System.Media;

namespace StratagemHelper;

/// <summary>
/// Original synthesised cues in a military-terminal style (generated in code, no audio files):
/// success = three fast rising blips with a short echo ("uplink confirmed"); failure = two low falling buzzes.
/// </summary>
internal static class Sounds
{
    private const int Rate = 44100;

    private static readonly Lazy<SoundPlayer> SuccessPlayer = new(() => Player(SuccessWave()));
    private static readonly Lazy<SoundPlayer> FailurePlayer = new(() => Player(FailureWave()));

    public static void Success() => Play(SuccessPlayer);
    public static void Failure() => Play(FailurePlayer);

    private static void Play(Lazy<SoundPlayer> p)
    {
        try { p.Value.Play(); } catch { /* no audio device: stay silent */ }
    }

    // ---------------- Waveforms ----------------

    /// <summary>C6 - E6 - G6 blips with a bright, slightly square timbre, plus an echo.</summary>
    public static float[] SuccessWave()
    {
        var notes = new (double hz, int ms)[] { (1046.5, 65), (1318.5, 65), (1568.0, 120) };
        var dry = Sequence(notes, gapMs: 18, Bright);
        return Echo(dry, delayMs: 110, gain: 0.35f);
    }

    /// <summary>G3 then D3, buzzy square waves with a little wobble: unmistakably "no".</summary>
    public static float[] FailureWave()
    {
        var notes = new (double hz, int ms)[] { (196.0, 150), (146.8, 240) };
        return Sequence(notes, gapMs: 40, Buzz, gain: 0.95f); // louder: low tones sound quieter to the ear
    }

    private static float Bright(double phase, double t)
    {
        double s = Math.Sin(phase);
        return (float)(0.75 * s + 0.25 * Math.Sign(s)); // sine with some square edge
    }

    private static float Buzz(double phase, double t)
    {
        double s = Math.Sin(phase + 0.6 * Math.Sin(2 * Math.PI * 9 * t)); // vibrato-ish wobble
        return (float)(0.45 * Math.Sign(s) + 0.35 * s);
    }

    private static float[] Sequence((double hz, int ms)[] notes, int gapMs, Func<double, double, float> wave, float gain = 0.55f)
    {
        var samples = new List<float>();
        foreach (var (hz, ms) in notes)
        {
            int n = Rate * ms / 1000;
            int attack = Rate * 4 / 1000, release = Math.Min(n / 2, Rate * 30 / 1000);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                phase += 2 * Math.PI * hz / Rate;
                float env = i < attack ? (float)i / attack : i > n - release ? (float)(n - i) / release : 1f;
                samples.Add(wave(phase, t) * env * gain);
            }
            samples.AddRange(new float[Rate * gapMs / 1000]);
        }
        return samples.ToArray();
    }

    private static float[] Echo(float[] dry, int delayMs, float gain)
    {
        int d = Rate * delayMs / 1000;
        var wet = new float[dry.Length + d * 2];
        for (int i = 0; i < dry.Length; i++)
        {
            wet[i] += dry[i];
            wet[i + d] += dry[i] * gain;
            wet[i + d * 2] += dry[i] * gain * gain;
        }
        return wet;
    }

    // ---------------- WAV ----------------

    /// <summary>16-bit mono PCM WAV bytes.</summary>
    public static byte[] ToWav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataLen = samples.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataLen); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataLen);
        foreach (var s in samples) w.Write((short)(Math.Clamp(s, -1f, 1f) * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }

    private static SoundPlayer Player(float[] samples)
    {
        var p = new SoundPlayer(new MemoryStream(ToWav(samples)));
        p.Load();
        return p;
    }
}
