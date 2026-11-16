using static StratagemHelper.Native;

namespace StratagemHelper;

/// <summary>
/// A binding is a short id: "K{vk}" for a keyboard key, "M3" middle mouse, "M4"/"M5" mouse side buttons.
/// Foot pedals and macro pads show up as ordinary keys.
/// <para>
/// Stratagem directions are stored as physical keys instead: "S{scan code in hex}", with E0 in front for extended
/// keys ("S11" is the key where QWERTY has W, "SE048" is the Up arrow). The game is told which key positions to
/// press, so the defaults mean "the WASD positions" on every layout: ZQSD on AZERTY, for example.
/// </para>
/// </summary>
internal static class Bindings
{
    public const string Escape = "K27", Backspace = "K8", Delete = "K46";

    /// <summary>Default stratagem directions, in the order Up, Down, Left, Right.</summary>
    public static readonly string[] WasdDirections = { "S11", "S1F", "S1E", "S20" };
    public static readonly string[] ArrowDirections = { "SE048", "SE050", "SE04B", "SE04D" };
    public static readonly string[] DirectionNames = { "Up", "Down", "Left", "Right" };

    /// <summary>The game's default stratagem menu key: left Ctrl (a physical key, like the directions).</summary>
    public const string DefaultMenuKey = "S1D";
    /// <summary>Default letters pressed while holding the menu key: C scans, V hides / shows the overlay.</summary>
    public const string DefaultScanHotkey = "K67", DefaultOverlayHotkey = "K86";

    public static string Key(uint vk) => "K" + vk;

    /// <summary>Keys sent with the E0 prefix, i.e. not the numpad / left-hand key that shares their scan code.</summary>
    private static readonly HashSet<Keys> ExtendedVks = new()
    {
        Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown,
        Keys.Insert, Keys.Delete, Keys.RControlKey, Keys.RMenu, Keys.LWin, Keys.RWin, Keys.Apps, Keys.Divide,
        Keys.NumLock, Keys.PrintScreen,
    };

    /// <summary>A physical key. <paramref name="scan"/> may already carry the E0 prefix.</summary>
    public static string Scan(uint scan, bool extended)
    {
        if (extended) scan |= 0xE000;
        return "S" + scan.ToString("X2");
    }

    /// <summary>The physical key a binding presses, for sending it as a scan code.</summary>
    public static bool TryGetScan(string? id, out ushort scan, out bool extended)
    {
        scan = 0;
        extended = false;
        if (string.IsNullOrEmpty(id)) return false;
        uint full;
        if (id[0] == 'S' && uint.TryParse(id[1..], System.Globalization.NumberStyles.HexNumber, null, out full)) { }
        else if (id[0] == 'K' && uint.TryParse(id[1..], out uint vk))
        {
            full = MapVirtualKey(vk, MAPVK_VK_TO_VSC_EX);
            // Windows doesn't always add the E0 prefix: the arrows, Home, End etc. would come back as numpad keys.
            if (full is > 0 and < 0x100 && ExtendedVks.Contains((Keys)vk)) full |= 0xE000;
        }
        else return false;
        scan = (ushort)(full & 0xFF);
        extended = (full & 0xFF00) == 0xE000;
        return scan != 0;
    }

    /// <summary>
    /// What the key is called on this PC's keyboard layout, e.g. "Z" for the WASD "W" on AZERTY, or "STRG" for
    /// Ctrl on a German keyboard. Upper case, to match how keys are shown everywhere in the app.
    /// </summary>
    public static string Display(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "—";
        switch (id)
        {
            case "M3": return "MIDDLE MOUSE";
            case "M4": return "MOUSE 4";
            case "M5": return "MOUSE 5";
        }
        if (id[0] == 'K' && int.TryParse(id[1..], out int vk))
        {
            // Numpad keys look the same on every layout, and share scan codes with the navigation keys, so they're
            // named here rather than by Windows.
            var k = (Keys)vk;
            string? name = k switch
            {
                >= Keys.NumPad0 and <= Keys.NumPad9 => "NUMPAD " + (vk - (int)Keys.NumPad0),
                Keys.Multiply => "NUMPAD *",
                Keys.Add => "NUMPAD +",
                Keys.Subtract => "NUMPAD -",
                Keys.Divide => "NUMPAD /",
                Keys.Decimal => "NUMPAD .",
                _ => null,
            };
            if (name == null && TryGetScan(id, out var sc, out var ext)) name = KeyName(sc, ext);
            name ??= k switch
            {
                >= Keys.D0 and <= Keys.D9 => ((char)vk).ToString(),
                Keys.Capital => "CAPS LOCK",
                Keys.Next => "PAGE DOWN",
                Keys.Prior => "PAGE UP",
                _ => k.ToString(),
            };
            return name.ToUpperInvariant();
        }
        if (id[0] == 'S' && TryGetScan(id, out var scan, out var extended))
            return (KeyName(scan, extended) ?? id).ToUpperInvariant();
        return id.ToUpperInvariant();
    }

    /// <summary>The layout's name for a physical key, or null if Windows doesn't have one.</summary>
    public static string? KeyName(ushort scan, bool extended)
    {
        var buf = new char[64];
        int lParam = (scan << 16) | (extended ? 1 << 24 : 0);
        int n = GetKeyNameText(lParam, buf, buf.Length);
        return n > 0 ? new string(buf, 0, n) : null;
    }

    /// <summary>
    /// A key's name for use in a sentence, e.g. "Ctrl" ("Strg" on a German keyboard), "Caps lock", "Mouse 4".
    /// </summary>
    public static string Name(string? id) => TitleCase(Display(id));

    /// <summary>The four default direction keys as one word, e.g. "WASD", or "ZQSD" on AZERTY.</summary>
    public static string WasdName =>
        Display(WasdDirections[0]) + Display(WasdDirections[2]) + Display(WasdDirections[1]) + Display(WasdDirections[3]);

    private static string TitleCase(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>Keys that can't be bound: modifiers (they'd break Ctrl handling) and mouse left/right.</summary>
    public static bool IsBindable(string id)
    {
        if (id[0] != 'K' || !int.TryParse(id[1..], out int vk)) return id is "M3" or "M4" or "M5";
        var k = (Keys)vk;
        return k is not (Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
                      or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
                      or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin);
    }

    /// <summary>
    /// A key that can be pressed while holding the menu key (scan / hide overlay): a keyboard key that isn't a
    /// modifier, a top-row digit (those call in slots 1-9), or one of the capture keys (Esc / Backspace / Delete).
    /// </summary>
    public static bool IsValidHotkey(string? id) =>
        id is { Length: > 1 } && id[0] == 'K' && int.TryParse(id[1..], out int vk) && IsBindable(id)
        && vk is not (>= 0x31 and <= 0x39) && id is not (Escape or Backspace or Delete);

    /// <summary>A usable set of four direction keys (Up, Down, Left, Right).</summary>
    public static bool IsValidDirections(string[]? keys) =>
        keys is { Length: 4 } && keys.All(k => TryGetScan(k, out _, out _));
}

/// <summary>
/// The game's stratagem menu key: a physical key ("S..." id) or the middle / side mouse buttons ("M3" - "M5").
/// The app watches it (hotkeys only work while it's held) and presses it itself for one-touch and voice call-ins.
/// Immutable, so the hook thread can read it while the settings change it.
/// </summary>
internal sealed class HeldKey
{
    public static readonly HeldKey Default = Parse(Bindings.DefaultMenuKey)!;

    public string Id { get; }
    public ushort Scan { get; }
    public bool Extended { get; }
    /// <summary>0 for a keyboard key, 3 for the middle mouse button, 4 / 5 for the side buttons.</summary>
    public int MouseButton { get; }
    /// <summary>Virtual key, for asking Windows whether it's down (this includes the app's own presses).</summary>
    public int Vk { get; }

    private HeldKey(string id, ushort scan, bool extended, int mouseButton, int vk)
    {
        Id = id; Scan = scan; Extended = extended; MouseButton = mouseButton; Vk = vk;
    }

    public static HeldKey? Parse(string? id)
    {
        switch (id)
        {
            case "M3": return new(id, 0, false, 3, 0x04); // VK_MBUTTON
            case "M4": return new(id, 0, false, 4, 0x05); // VK_XBUTTON1
            case "M5": return new(id, 0, false, 5, 0x06); // VK_XBUTTON2
        }
        if (id == null || id.Length < 2 || id[0] != 'S' || !Bindings.TryGetScan(id, out var scan, out var extended)) return null;
        uint vk = MapVirtualKey(extended ? 0xE000u | scan : scan, MAPVK_VSC_TO_VK_EX);
        if (vk == 0 || (Keys)vk is Keys.LWin or Keys.RWin or Keys.Escape) return null;
        return new(id, scan, extended, 0, (int)vk);
    }

    /// <summary>Held right now, by the player or by the app.</summary>
    public bool IsDown => (GetAsyncKeyState(Vk) & 0x8000) != 0;

    /// <summary>Is this keyboard event (scan code and extended flag from the hook) this key?</summary>
    public bool IsKey(uint scan, bool extended) => MouseButton == 0 && scan == Scan && extended == Extended;
}
