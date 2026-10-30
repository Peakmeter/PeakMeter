using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PeakMeter;

/// <summary>
/// Skill icons from the game's official image server (the one the AION 2 website uses). Nothing is bundled:
/// each icon is downloaded once, kept in %AppData%\PeakMeter\icons and shown from there.
/// data/skill_icons.json maps the first four digits of a skill code to the icon's file name.
/// </summary>
public static class SkillIcons
{
    private const string Cdn = "https://assets.playnccdn.com/static-aion2-gamedata/resources/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();
    private static readonly ConcurrentDictionary<string, byte> Loading = new();
    private static Dictionary<string, string> _map = new();
    private static string _dir = "";

    /// <summary>Raised (on the UI thread) when an icon finished downloading, so lists can redraw.</summary>
    public static event Action? Loaded;

    public static void Init(string dataDir)
    {
        _dir = Path.Combine(Settings.Dir, "icons");
        try
        {
            var path = Path.Combine(dataDir, "skill_icons.json");
            if (File.Exists(path)) _map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch { }
    }

    private static readonly string[] ClassCodes = ["GL", "TE", "AS", "RA", "SO", "EL", "CL", "CH", "GT"];

    /// <summary>The icon's file name on the game's image server, or null (basic attacks, unknown skills).</summary>
    public static string? IconName(int skillCode) => NameOf(skillCode);

    /// <summary>The icon's file name, or null for basic attacks and unknown skills.</summary>
    private static string? NameOf(int skillCode)
    {
        var code = skillCode.ToString().PadRight(8, '0');
        if (code.Length > 8) code = code[..8];
        // Basic attacks have no skill icon of their own.
        if (code.StartsWith("1000") || code.StartsWith("1699") || (code[..2].CompareTo("11") >= 0 && code[..2].CompareTo("18") <= 0 && code[2..6] == "0000"))
            return null;
        if (_map.TryGetValue(code[..4], out var name)) return name;
        // Not in the table: class skills follow ICON_<class>_SKILL_<nn>.
        if (int.TryParse(code[..2], out var prefix) && prefix is >= 11 and <= 19 && int.TryParse(code[2..4], out var sub))
            return $"ICON_{ClassCodes[prefix - 11]}_SKILL_{sub:000}";
        return null;
    }

    /// <summary>The icon when it is ready (else null; it loads in the background and <see cref="Loaded"/> fires).</summary>
    public static ImageSource? Get(int skillCode)
    {
        if (NameOf(skillCode) is not { } name) return EventIcons.Get("battle");
        return ByName(name);
    }

    /// <summary>Any icon of the game's image server by its file name (buff icons, for one).</summary>
    public static ImageSource? ByName(string name)
    {
        if (Cache.TryGetValue(name, out var img)) return img;
        var file = Path.Combine(_dir, name + ".png");
        if (File.Exists(file))
        {
            img = Decode(File.ReadAllBytes(file));
            Cache[name] = img;
            return img;
        }
        if (Loading.TryAdd(name, 0)) _ = Download(name, file);
        return null;
    }

    private static async Task Download(string name, string file)
    {
        ImageSource? img = null;
        try
        {
            var bytes = await Http.GetByteArrayAsync(Cdn + name + ".png");
            Directory.CreateDirectory(_dir);
            await File.WriteAllBytesAsync(file, bytes);
            img = Decode(bytes);
        }
        catch { } // offline or no such icon: the row just has none
        Cache[name] = img;
        Application.Current?.Dispatcher.BeginInvoke(() => Loaded?.Invoke());
    }

    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.DecodePixelWidth = 40; // shown at 16 px: small in memory
            b.StreamSource = new MemoryStream(bytes);
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch
        {
            return null;
        }
    }
}
