using System.Runtime.InteropServices;
using static StratagemHelper.Native;

namespace StratagemHelper;

/// <summary>Sends stratagem direction sequences as hardware scan codes.</summary>
internal static class InputSender
{
    private static readonly object Busy = new();

    // Scan codes (set 1)
    private const ushort SC_W = 0x11, SC_A = 0x1E, SC_S = 0x1F, SC_D = 0x20;
    private const ushort SC_UP = 0x48, SC_LEFT = 0x4B, SC_RIGHT = 0x4D, SC_DOWN = 0x50;

    private const ushort SC_LCTRL = 0x1D;

    /// <summary>Holds or releases left Ctrl (opens / closes the stratagem menu).</summary>
    public static void PressCtrl(bool down) => SendKey(SC_LCTRL, false, keyUp: !down);

    /// <summary>
    /// Enters a code. With <paramref name="ctrlMenuDelayMs"/> >= 0 the app holds Ctrl itself (one-touch / voice):
    /// Ctrl down, wait for the menu, code, Ctrl up. Returns false if another sequence was already running.
    /// </summary>
    public static bool SendSequence(string code, bool useArrows, int holdMs, int gapMs, int ctrlMenuDelayMs = -1)
    {
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
                ushort sc;
                bool extended = useArrows;
                switch (c)
                {
                    case 'U': sc = useArrows ? SC_UP : SC_W; break;
                    case 'D': sc = useArrows ? SC_DOWN : SC_S; break;
                    case 'L': sc = useArrows ? SC_LEFT : SC_A; break;
                    case 'R': sc = useArrows ? SC_RIGHT : SC_D; break;
                    default: continue;
                }
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
