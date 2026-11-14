using System.Text.Json;

namespace StratagemHelper;

/// <summary>Code uses U/D/L/R for Up/Down/Left/Right.</summary>
public sealed class Stratagem
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";

    public string Arrows => string.Concat(Code.ToUpperInvariant().Select(c => c switch
    {
        'U' => "↑", 'D' => "↓", 'L' => "←", 'R' => "→", _ => ""
    }));
}

public sealed class Settings
{
    public string[] Slots { get; set; } = new string[9];
    public bool UseArrowKeys { get; set; } = false;
    public bool OnlyWhenGameFocused { get; set; } = true;
    public bool AutoDetect { get; set; } = true;

    /// <summary>Play the scan success / failure sounds.</summary>
    public bool SoundCues { get; set; } = true;

    /// <summary>The one-time "start with Windows?" welcome has been answered.</summary>
    public bool FirstRunDone { get; set; }

    // One-touch call-ins: binding ids (see Bindings) for slots 1-9 and the scan. Default: numpad.
    public string?[] SlotBindings { get; set; } = { "K97", "K98", "K99", "K100", "K101", "K102", "K103", "K104", "K105" };
    public string? ScanBinding { get; set; } = "K96";
    public int MenuOpenDelayMs { get; set; } = 120;

    // Voice: 0 = off, 1 = push-to-talk, 2 = always listening.
    public int VoiceMode { get; set; } = 0;
    public string? PttBinding { get; set; }
    public int VoiceConfidence { get; set; } = 60;

    /// <summary>Always-visible loadout panel in the game's top-left corner.</summary>
    public bool ShowOverlay { get; set; } = true;
    public int KeyHoldMs { get; set; } = 35;
    public int KeyGapMs { get; set; } = 35;
}

internal static class Storage
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder { get; } = AppContext.BaseDirectory;
    public static string StratagemsPath => Path.Combine(Folder, "stratagems.json");
    public static string SettingsPath => Path.Combine(Folder, "settings.json");

    public static List<Stratagem> LoadStratagems()
    {
        try
        {
            if (File.Exists(StratagemsPath))
            {
                var list = JsonSerializer.Deserialize<List<Stratagem>>(File.ReadAllText(StratagemsPath));
                if (list is { Count: > 0 }) return list;
            }
        }
        catch { /* fall through to defaults */ }

        var defaults = DefaultStratagems.All.ToList();
        try { File.WriteAllText(StratagemsPath, JsonSerializer.Serialize(defaults, Json)); } catch { }
        return defaults;
    }

    public static Settings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
                if (s != null)
                {
                    var slots = new string[9];
                    Array.Copy(s.Slots ?? Array.Empty<string>(), slots, Math.Min(9, s.Slots?.Length ?? 0));
                    s.Slots = slots;
                    return s;
                }
            }
        }
        catch { }

        return new Settings
        {
            Slots = new[]
            {
                "Reinforce", "Resupply", "SOS Beacon", "Eagle 500KG Bomb", "Orbital Precision Strike",
                "Eagle Airstrike", "Orbital Laser", "Hellbomb", "SEAF Artillery"
            }
        };
    }

    public static void SaveStratagems(List<Stratagem> list)
    {
        try { File.WriteAllText(StratagemsPath, JsonSerializer.Serialize(list, Json)); } catch { }
    }

    public static void SaveSettings(Settings s)
    {
        try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, Json)); } catch { }
    }
}

internal static class DefaultStratagems
{
    private static Stratagem S(string name, string code) => new() { Name = name, Code = code };

    public static readonly Stratagem[] All =
    {
        // Mission / general
        S("Reinforce", "UDRLU"),
        S("Resupply", "DDUR"),
        S("SOS Beacon", "UDRU"),
        S("Hellbomb", "DULDURDU"),
        S("SEAF Artillery", "RUUD"),
        S("Super Earth Flag", "DUDU"),

        // Eagle
        S("Eagle Strafing Run", "URR"),
        S("Eagle Airstrike", "URDR"),
        S("Eagle Cluster Bomb", "URDDR"),
        S("Eagle Napalm Airstrike", "URDU"),
        S("Eagle Smoke Strike", "URUD"),
        S("Eagle 110mm Rocket Pods", "URUL"),
        S("Eagle 500KG Bomb", "URDDD"),

        // Orbital
        S("Orbital Precision Strike", "RRU"),
        S("Orbital Gas Strike", "RRDR"),
        S("Orbital EMS Strike", "RRLD"),
        S("Orbital Smoke Strike", "RRDU"),
        S("Orbital Airburst Strike", "RRR"),
        S("Orbital Gatling Barrage", "RDLUU"),
        S("Orbital 120mm HE Barrage", "RRDLRD"),
        S("Orbital 380mm HE Barrage", "RDUULDD"),
        S("Orbital Walking Barrage", "RDRDRD"),
        S("Orbital Napalm Barrage", "RRDLRU"),
        S("Orbital Laser", "RDURD"),
        S("Orbital Railcannon Strike", "RUDDR"),

        // Support weapons
        S("MG-43 Machine Gun", "DLDUR"),
        S("APW-1 Anti-Materiel Rifle", "DLRUD"),
        S("M-105 Stalwart", "DLDUUL"),
        S("EAT-17 Expendable Anti-Tank", "DDLUR"),
        S("GR-8 Recoilless Rifle", "DLRRL"),
        S("FLAM-40 Flamethrower", "DLUDU"),
        S("AC-8 Autocannon", "DLDUUR"),
        S("MG-206 Heavy Machine Gun", "DLUDD"),
        S("RL-77 Airburst Rocket Launcher", "DUULR"),
        S("MLS-4X Commando", "DLUDR"),
        S("RS-422 Railgun", "DRDULR"),
        S("FAF-14 Spear", "DDUDD"),
        S("GL-21 Grenade Launcher", "DLULD"),
        S("LAS-98 Laser Cannon", "DLDUL"),
        S("ARC-3 Arc Thrower", "DRDULL"),
        S("LAS-99 Quasar Cannon", "DDULR"),

        // Backpacks
        S("B-1 Supply Pack", "DLDUUD"),
        S("LIFT-850 Jump Pack", "DUUDU"),
        S("SH-20 Ballistic Shield Backpack", "DLDDUL"),
        S("SH-32 Shield Generator Pack", "DULRLR"),
        S("AX/AR-23 \"Guard Dog\"", "DULURD"),
        S("AX/LAS-5 \"Guard Dog\" Rover", "DULURR"),

        // Sentries / emplacements / mines
        S("E/MG-101 HMG Emplacement", "DULRRL"),
        S("FX-12 Shield Generator Relay", "DDLRLR"),
        S("A/ARC-3 Tesla Tower", "DURULR"),
        S("MD-6 Anti-Personnel Minefield", "DLUR"),
        S("MD-I4 Incendiary Mines", "DLLD"),
        S("MD-17 Anti-Tank Mines", "DLUU"),
        S("A/MG-43 Machine Gun Sentry", "DURRU"),
        S("A/G-16 Gatling Sentry", "DURL"),
        S("A/M-12 Mortar Sentry", "DURRD"),
        S("A/M-23 EMS Mortar Sentry", "DURDR"),
        S("A/AC-8 Autocannon Sentry", "DURULU"),
        S("A/MLS-4X Rocket Sentry", "DURRL"),

        // Vehicles
        S("EXO-45 Patriot Exosuit", "LDRULDD"),
        S("EXO-49 Emancipator Exosuit", "LDRULDU"),
    };
}
