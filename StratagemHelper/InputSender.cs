using System.Runtime.InteropServices;
using static StratagemHelper.Native;

namespace StratagemHelper;

/// <summary>Sends stratagem direction sequences as hardware scan codes.</summary>
internal static class InputSender
{
    private static readonly object Busy = new();

    /// <summary>Holds or releases the game's stratagem menu key (a key, or the middle / side mouse buttons).</summary>
    public static void PressMenu(HeldKey key, bool down)
    {
        if (key.MouseButton == 0)
        {
            SendKey(key.Scan, key.Extended, keyUp: !down);
            return;
        }
        uint flags = key.MouseButton == 3
            ? (down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP)
            : (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP);
        uint data = key.MouseButton switch { 4 => XBUTTON1, 5 => XBUTTON2, _ => 0 };
        var inputs = new[]
        {
            new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data } } }
        };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Enters a code. With <paramref name="openMenu"/> set the app holds that key itself (one-touch / voice):
    /// menu key down, wait <paramref name="menuDelayMs"/> for the menu, code, menu key up.
    /// Returns false if another sequence was already running.
    /// <paramref name="directionKeys"/> are the game's Up, Down, Left, Right keys as binding ids (see <see cref="Bindings"/>).
    /// </summary>
    public static bool SendSequence(string code, string[] directionKeys, int holdMs, int gapMs, HeldKey? openMenu = null, int menuDelayMs = 0)
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
        try
        {
            timeBeginPeriod(1);
            if (openMenu != null)
            {
                PressMenu(openMenu, true);
                Thread.Sleep(menuDelayMs);
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
            if (openMenu != null) PressMenu(openMenu, false);
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
