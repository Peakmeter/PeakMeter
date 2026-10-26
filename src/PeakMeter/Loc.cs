using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Markup;

namespace PeakMeter;

/// <summary>
/// The interface language. Every text is written in German and English right where it is used —
/// <c>L.S("Gruppe", "Party")</c> in code, <c>{local:T De=Gruppe, En=Party}</c> in XAML. Other languages come from
/// data/lang/&lt;code&gt;.json, keyed by the English text; texts with numbers inside ("Kill in ~1:23") are matched
/// against templates there ("Kill in ~{0}"). Anything not translated stays English. A switch restarts PeakMeter.
/// </summary>
public static class L
{
    /// <summary>Codes as the game data uses them, with the language's own name.</summary>
    public static readonly (string Code, string Name)[] Languages =
    [
        ("de", "Deutsch"), ("en", "English"), ("fr", "Français"), ("es", "Español"), ("pt", "Português"),
        ("ru", "Русский"), ("ko", "한국어"), ("zh-Hant", "繁體中文"), ("ja", "日本語"),
    ];

    private static string _lang = "de";
    private static Dictionary<string, string> _exact = new();
    private static List<(Regex Pattern, string Target)> _templates = [];
    private static readonly ConcurrentDictionary<string, string> Done = new();

    /// <summary>True for every language except German (English is the base for all others).</summary>
    public static bool En => _lang != "de";

    public static string Lang => _lang;

    /// <summary>Picks the language and loads its table (call once at start).</summary>
    public static void Use(string lang, string dataDir)
    {
        _lang = Languages.Any(l => l.Code == lang) ? lang : "de";
        if (_lang is "de" or "en") return;
        try
        {
            var path = Path.Combine(dataDir, "lang", _lang + ".json");
            var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
            foreach (var (en, tr) in table)
            {
                if (tr is null) continue; // "" is a real translation (a word some languages leave out)
                if (!Regex.IsMatch(en, @"\{\d+\}")) { _exact[en] = tr; continue; }
                // "Kill in ~{0}" → ^Kill in ~(.*?)$ ; the holes become groups that are put into the translation.
                var pattern = "^" + Regex.Replace(Regex.Escape(en), @"\\\{(\d+)\}", m => $"(?<h{m.Groups[1].Value}>.*?)") + "$";
                _templates.Add((new Regex(pattern, RegexOptions.Singleline | RegexOptions.Compiled), tr));
            }
            // Longer templates first: the most specific one wins.
            _templates = _templates.OrderByDescending(t => t.Pattern.ToString().Length).ToList();
        }
        catch { }
    }

    public static string S(string de, string en) => _lang switch
    {
        "de" => de,
        "en" => en,
        _ => Translate(en),
    };

    private static string Translate(string en)
    {
        if (en.Length == 0) return en;
        if (_exact.TryGetValue(en, out var hit)) return hit;
        if (Done.TryGetValue(en, out var done)) return done;
        var result = en;
        foreach (var (pattern, target) in _templates)
        {
            var m = pattern.Match(en);
            if (!m.Success) continue;
            result = Regex.Replace(target, @"\{(\d+)\}", h => m.Groups["h" + h.Groups[1].Value].Value);
            break;
        }
        if (Done.Count < 4000) Done[en] = result;
        return result;
    }

    /// <summary>Dates and weekdays in the interface language.</summary>
    public static System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(_lang switch
    {
        "de" => "de-DE",
        "fr" => "fr-FR",
        "es" => "es-ES",
        "pt" => "pt-BR",
        "ru" => "ru-RU",
        "ko" => "ko-KR",
        "zh-Hant" => "zh-TW",
        "ja" => "ja-JP",
        _ => "en-GB",
    });
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public T() { }

    public T(string de, string en)
    {
        De = de;
        En = en;
    }

    public string De { get; set; } = "";
    public string En { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.S(De, En);
}
