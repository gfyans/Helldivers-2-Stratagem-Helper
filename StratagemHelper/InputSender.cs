using System.Runtime.InteropServices;
using static StratagemHelper.Native;

namespace StratagemHelper;

/// <summary>Sends stratagem direction sequences as hardware scan codes.</summary>
internal static class InputSender
{
    private static readonly object Busy = new();

    private const ushort SC_LCTRL = 0x1D;

    /// <summary>Holds or releases left Ctrl (opens / closes the stratagem menu).</summary>
    public static void PressCtrl(bool down) => SendKey(SC_LCTRL, false, keyUp: !down);

    /// <summary>
    /// Enters a code. With <paramref name="ctrlMenuDelayMs"/> >= 0 the app holds Ctrl itself (one-touch / voice):
    /// Ctrl down, wait for the menu, code, Ctrl up. Returns false if another sequence was already running.
    /// <paramref name="directionKeys"/> are the game's Up, Down, Left, Right keys as binding ids (see <see cref="Bindings"/>).
    /// </summary>
    public static bool SendSequence(string code, string[] directionKeys, int holdMs, int gapMs, int ctrlMenuDelayMs = -1)
    {
        var keys = new (ushort scan, bool extended)[4];
        for (int i = 0; i < 4; i++)
        {
            var id = directionKeys.ElementAtOrDefault(i) ?? Bindings.WasdDirections[i];
            if (!Bindings.TryGetScan(id, out keys[i].scan, out keys[i].extended))
                Bindings.TryGetScan(Bindings.WasdDirections[i], out keys[i].scan, out keys[i].extended);
        }

        // If a sequence is already running (e.g. two hotkeys pressed quickly), drop this one
        // rather than interleaving keystrokes.
        if (!Monitor.TryEnter(Busy)) return false;
        bool ctrl = ctrlMenuDelayMs >= 0;
        try
        {
            timeBeginPeriod(1);
            if (ctrl)
            {
                PressCtrl(true);
                Thread.Sleep(ctrlMenuDelayMs);
            }
            foreach (char c in code.ToUpperInvariant())
            {
                int dir = c switch { 'U' => 0, 'D' => 1, 'L' => 2, 'R' => 3, _ => -1 };
                if (dir < 0) continue;
                var (sc, extended) = keys[dir];
                SendKey(sc, extended, keyUp: false);
                Thread.Sleep(holdMs);
                SendKey(sc, extended, keyUp: true);
                Thread.Sleep(gapMs);
            }
        }
        finally
        {
            if (ctrl) PressCtrl(false);
            timeEndPeriod(1);
            Monitor.Exit(Busy);
        }
        return true;
    }

    private static void SendKey(ushort scanCode, bool extended, bool keyUp)
    {
        uint flags = KEYEVENTF_SCANCODE;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (keyUp) flags |= KEYEVENTF_KEYUP;

        var inputs = new[]
        {
            new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wScan = scanCode, dwFlags = flags } }
            }
        };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }
}
