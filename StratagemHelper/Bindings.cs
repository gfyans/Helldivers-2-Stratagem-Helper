namespace StratagemHelper;

/// <summary>
/// A binding is a short id: "K{vk}" for a keyboard key, "M3" middle mouse, "M4"/"M5" mouse side buttons.
/// Foot pedals and macro pads show up as ordinary keys.
/// </summary>
internal static class Bindings
{
    public const string Escape = "K27", Backspace = "K8", Delete = "K46";

    public static string Key(uint vk) => "K" + vk;

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
            var k = (Keys)vk;
            string name = k switch
            {
                >= Keys.NumPad0 and <= Keys.NumPad9 => "NUMPAD " + (vk - (int)Keys.NumPad0),
                >= Keys.D0 and <= Keys.D9 => ((char)vk).ToString(),
                Keys.Multiply => "NUMPAD *",
                Keys.Add => "NUMPAD +",
                Keys.Subtract => "NUMPAD -",
                Keys.Divide => "NUMPAD /",
                Keys.Decimal => "NUMPAD .",
                Keys.Oemtilde => "`",
                Keys.Capital => "CAPS LOCK",
                Keys.Next => "PAGE DOWN",
                Keys.Prior => "PAGE UP",
                _ => k.ToString(),
            };
            return name.ToUpperInvariant();
        }
        return id.ToUpperInvariant();
    }

    /// <summary>Keys that can't be bound: modifiers (they'd break Ctrl handling) and mouse left/right.</summary>
    public static bool IsBindable(string id)
    {
        if (id[0] != 'K' || !int.TryParse(id[1..], out int vk)) return id is "M3" or "M4" or "M5";
        var k = (Keys)vk;
        return k is not (Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
                      or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
                      or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin);
    }
}
