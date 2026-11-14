// Checks for voice phrases, one-touch keystrokes, the number-label overlay and stats maths.
using System.Drawing;
using StratagemHelper;

static class FeatureTests
{
    public static int Voice()
    {
        int fail = 0;
        void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) fail++; }

        Console.WriteLine("== Spoken names");
        foreach (var n in new[] { "Eagle 500KG Bomb", "SOS Beacon", "A/M-12 Mortar Sentry", "Orbital 120mm HE Barrage", "AX/AR-23 \"Guard Dog\"", "EXO-49 Emancipator Exosuit" })
            Console.WriteLine($"  {n,-30} -> {StratagemHelper.Voice.Spoken(n)}");

        var slots = new string?[] { "Reinforce", "SOS Beacon", "Resupply", "Eagle Cluster Bomb", "AC-8 Autocannon",
                                    "A/AC-8 Autocannon Sentry", "A/M-12 Mortar Sentry", null, null };
        var phrases = StratagemHelper.Voice.PhrasesFor(slots);
        Console.WriteLine($"== {phrases.Count} phrases: {string.Join(" | ", phrases.Keys.OrderBy(k => k))}");
        Check(phrases.TryGetValue("mortar sentry", out var m) && m == 7, "\"mortar sentry\" -> slot 7");
        Check(phrases.TryGetValue("autocannon", out var a) && a == 5, "\"autocannon\" -> slot 5 (not the sentry)");
        Check(phrases.TryGetValue("S O S beacon", out var s) && s == 2, "\"S O S beacon\" -> slot 2");
        Check(phrases.TryGetValue("slot nine", out var n9) && n9 == 9, "\"slot nine\" -> slot 9");

        Console.WriteLine("== Real recogniser grammar (EmulateRecognize)");
        using var voice = new StratagemHelper.Voice();
        var err = voice.Start(phrases, listen: false);
        Check(err == null, "recogniser started" + (err != null ? ": " + err : $" ({voice.RecognizerName})"));
        if (err == null)
        {
            foreach (var (say, slot) in new[] { ("call in mortar sentry", 7), ("call in eagle cluster bomb", 4), ("call in resupply", 3),
                                                ("call in slot two", 2), ("call in autocannon sentry", 6), ("call in eagle cluster", 4) })
            {
                int got = StratagemHelper.Voice.SlotOf(voice.Emulate(say)) ?? -1;
                Check(got == slot, $"\"{say}\" -> slot {got} (expected {slot})");
            }
            // Without the "call in" keyword nothing may trigger.
            foreach (var say in new[] { "resupply", "mortar sentry", "slot two", "call in orbital laser", "in resupply" })
                Check(StratagemHelper.Voice.SlotOf(voice.Emulate(say)) == null, $"\"{say}\" -> ignored");
        }
        return fail;
    }

    public static int Stats()
    {
        int fail = 0;
        void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) fail++; }
        Console.WriteLine("== Keys saved per call-in (code DURRD, 5 arrows)");
        Check(StratagemHelper.Stats.KeysSavedFor("DURRD", StratagemHelper.Stats.Via.CtrlNumber) == 4, "Ctrl+N saves 4");
        Check(StratagemHelper.Stats.KeysSavedFor("DURRD", StratagemHelper.Stats.Via.OneTouch) == 5, "one-touch saves 5");
        Check(StratagemHelper.Stats.KeysSavedFor("DURRD", StratagemHelper.Stats.Via.Voice) == 6, "voice saves 6");
        return fail;
    }

    /// <summary>Sends a one-touch call-in into a test window and logs the key events it receives.</summary>
    public static int Keys()
    {
        var log = new List<string>();
        var form = new Form { Text = "keytest", Width = 300, Height = 120, KeyPreview = true, TopMost = true };
        form.KeyDown += (_, e) => log.Add("down " + e.KeyCode);
        form.KeyUp += (_, e) => log.Add("up " + e.KeyCode);
        form.Shown += async (_, _) =>
        {
            form.Activate();
            await Task.Delay(400);
            await Task.Run(() => InputSender.SendSequence("UDLR", useArrows: false, holdMs: 20, gapMs: 20, ctrlMenuDelayMs: 60));
            await Task.Delay(300);
            form.Close();
        };
        Application.Run(form);

        // Ctrl must wrap the whole sequence: W S A D is up, down, left, right.
        var expected = new[] { "down ControlKey", "down W", "up W", "down S", "up S", "down A", "up A", "down D", "up D", "up ControlKey" };
        Console.WriteLine("== One-touch key events: " + string.Join(", ", log));
        bool ok = log.SequenceEqual(expected);
        Console.WriteLine(ok ? "  ok   Ctrl held around U D L R as W S A D" : "  FAIL expected " + string.Join(", ", expected));
        return ok ? 0 : 1;
    }

    /// <summary>Shows the loadout panel for a screenshot: normal position, then moved aside as if the menu were open.</summary>
    public static void Overlay(int seconds)
    {
        var overlay = new LoadoutOverlay();
        overlay.SetRows(new string?[] { "Reinforce", "SOS Beacon", "Resupply", "Eagle Cluster Bomb", "EAT-17 Expendable Anti-Tank", "Nova", "A/AC-8 Autocannon Sentry", null, null });
        int ticks = 0;
        var t = new System.Windows.Forms.Timer { Interval = seconds * 500 };
        t.Tick += (_, _) => { if (++ticks == 1) overlay.SetMenuOpen(true); else Application.ExitThread(); };
        overlay.ShowOn(Screen.PrimaryScreen!.Bounds);
        t.Start();
        Application.Run();
    }
}