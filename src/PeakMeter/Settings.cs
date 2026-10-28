using System.IO;
using System.Text.Json;

namespace PeakMeter;

/// <summary>Everything PeakMeter remembers, saved as one small JSON file in %AppData%\PeakMeter.</summary>
public sealed class Settings
{
    public static string Dir { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PeakMeter");

    /// <summary>"--profile test": a separate settings folder (and instance) for trying things out.</summary>
    public static string Profile { get; private set; } = "";

    public static void UseProfile(string name)
    {
        Profile = name;
        Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PeakMeter-" + name);
    }
    private static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 300;
    /// <summary>Background opacity (0.2–1); text stays solid.</summary>
    public double Opacity { get; set; } = 0.85;
    /// <summary>Bumped when a default changes for existing users.</summary>
    public int SettingsVersion { get; set; }
    public bool BossOnly { get; set; } = true;
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
    public bool Visible { get; set; } = true;
    public bool Compact { get; set; }
    public string Language { get; set; } = "de";
    /// <summary>The version that ran last: a different one means "just updated".</summary>
    public string? LastVersion { get; set; }
    /// <summary>Chime volume for event alerts, 0–100 (0 = silent).</summary>
    public int SoundVolume { get; set; } = 40;
    /// <summary>Save boss fights and fights longer than this many seconds to the logs.</summary>
    public int LogMinSeconds { get; set; } = 20;
    /// <summary>Minutes before a watched field boss respawns to chime.</summary>
    public int BossLeadMin { get; set; } = 2;
    /// <summary>Solo: only your own row. In a party: only its members. Off: everyone the meter saw.</summary>
    public bool PartyOnly { get; set; } = true;
    /// <summary>What the rows show: "dps" (damage), "heal" (healing) or "tank" (damage taken).</summary>
    public string View { get; set; } = "dps";
    public bool ShowPower { get; set; } = true;
    public string HotkeyToggle { get; set; } = "Ctrl+Shift+D";
    public string HotkeyReset { get; set; } = "Ctrl+Shift+R";
    public string HotkeyClickThrough { get; set; } = "Ctrl+Shift+L";
    public bool AutoUpdate { get; set; } = true;

    // ---- 1.14: settings in the style of the big meters
    /// <summary>"eu" or "na": the time zone of the event timers.</summary>
    public string Region { get; set; } = "eu";
    /// <summary>A fight ends after this many seconds without damage (no boss alive).</summary>
    public int IdleTimeoutSec { get; set; } = 10;
    /// <summary>"e" = eDPS (whole fight) or "a" = aDPS (own active time).</summary>
    public string DpsMode { get; set; } = "a";
    /// <summary>Number after each name: "cp" (combat power), "gs" (gear score) or "none".</summary>
    public string ValueChip { get; set; } = "cp";
    /// <summary>Show each player's total damage next to the DPS.</summary>
    public bool ShowTotal { get; set; }
    public bool Use24h { get; set; } = true;
    /// <summary>Other players by class name instead of their name (for streams and screenshots).</summary>
    public bool HideNames { get; set; }
    /// <summary>The own character's portrait (official NCSoft image) instead of the class emblem in the header.</summary>
    public bool ShowPortrait { get; set; }
    /// <summary>The pet line and the pet button in the meter (off once every pet is done).</summary>
    public bool ShowPets { get; set; } = true;
    /// <summary>Count this meter, anonymously, in the public "how many play with PeakMeter" number (OnlineCounter).</summary>
    public bool CountMe { get; set; } = true;
    /// <summary>The day (d-yyyyMMdd, UTC) this meter was last counted for, so a day counts once.</summary>
    public string? LastCountDay { get; set; }
    public bool CountedInstall { get; set; }
    public double UiScale { get; set; } = 1.0;
    /// <summary>Update interval in a fight, milliseconds.</summary>
    public int RefreshMs { get; set; } = 250;
    public bool SummaryMultiline { get; set; }
    /// <summary>In a fight the header hides until the mouse is over the meter.</summary>
    public bool FocusMode { get; set; }
    /// <summary>Fade the meter after a while without a fight.</summary>
    public bool FadeIdle { get; set; }
    public int FadeAfterSec { get; set; } = 60;
    /// <summary>Show the meter only while AION 2 (or PeakMeter itself) is in the foreground.</summary>
    public bool OnlyOverGame { get; set; }
    public bool RememberClickThrough { get; set; } = true;
    /// <summary>Hide the meter while AION 2 is not running; it comes back when the game starts.</summary>
    public bool HideWhenGameClosed { get; set; }
    /// <summary>Alerts slide in at the right edge of the screen.</summary>
    public bool Toasts { get; set; } = true;
    public int ToastSeconds { get; set; } = 8;
    public string HotkeyCopy { get; set; } = "";

    // Odyle energy (entered by hand: the game traffic PeakMeter reads does not carry it).
    public int OdyleValue { get; set; } = -1;
    public DateTimeOffset? OdyleSetAt { get; set; }
    public bool OdyleQuai { get; set; }
    public int OdyleRegen { get; set; } = 15;
    public bool OdyleFullNotified { get; set; }
    /// <summary>Energy from items, on top of the regenerating pool (not limited by the cap).</summary>
    public int OdyleBonus { get; set; }
    /// <summary>When the game itself last told the value (null = only entered by hand).</summary>
    public DateTimeOffset? OdyleFromGameAt { get; set; }
    /// <summary>Npcap adapter to force (VPN etc.); null = find the game's adapter automatically.</summary>
    public string? CaptureDevice { get; set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
                if (s.SettingsVersion < 2)
                {
                    // v2: background-only transparency, slightly see-through by default.
                    if (s.Opacity >= 0.99) s.Opacity = 0.85;
                    s.SettingsVersion = 2;
                }
                if (s.SettingsVersion < 3)
                {
                    // v3: DPS from your own first hit (aDPS), like the other meters; joining late no longer lowers it.
                    s.DpsMode = "a";
                    s.SettingsVersion = 3;
                }
                return s;
            }
        }
        catch (Exception) { }
        return new Settings();
    }

    private static readonly object SaveGate = new();

    public void Save()
    {
        try
        {
            lock (SaveGate)
            {
                SettingsVersion = 3;
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
            }
        }
        catch (Exception) { }
    }
}
