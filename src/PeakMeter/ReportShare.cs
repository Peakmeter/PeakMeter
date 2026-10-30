using System.Net.Http;
using System.Text;
using System.Text.Json;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace PeakMeter;

/// <summary>
/// "Share as link": one saved fight becomes a page on the PeakMeter website (/r/&lt;id&gt;) with every player, their
/// skills, the damage over time and the death recaps — like a killmail. Only on the player's click. With "Hide names"
/// on, other players show as their class, as they do in the meter.
/// </summary>
public static class ReportShare
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private const int MaxPlayers = 24, MaxSkills = 16, MaxPoints = 240;

    /// <summary>Sends the fight and returns its link, or null with the reason in <paramref name="error"/>.</summary>
    public static async Task<(string? Url, string? Error)> Share(FightRecord record, GameData data, Settings settings)
    {
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(Payload(record, data, settings)), Encoding.UTF8, "application/json");
            using var r = await Http.PostAsync($"{Ranking.Url}/api/report", content);
            var body = await r.Content.ReadAsStringAsync();
            if (!r.IsSuccessStatusCode)
                return (null, (int)r.StatusCode == 429 ? L.S("Zu viele Links in kurzer Zeit – bitte später", "Too many links in a short time – try later")
                                                      : L.S("Die Webseite hat den Kampf nicht angenommen", "The website did not take the fight"));
            using var doc = JsonDocument.Parse(body);
            return (doc.RootElement.GetProperty("url").GetString(), null);
        }
        catch
        {
            return (null, L.S("Webseite nicht erreichbar", "Website not reachable"));
        }
    }

    public static Dictionary<string, object?> Payload(FightRecord record, GameData data, Settings settings)
    {
        var s = record.Summary;
        var details = record.Details.ToDictionary(d => d.ActorId);
        var players = s.Combatants.Where(c => !c.IsUnknownSummons && (c.Damage > 0 || c.Healing > 0 || c.DamageTaken > 0))
            .OrderByDescending(c => c.Damage).Take(MaxPlayers).ToList();
        var seconds = Math.Max(1, s.CombatMs / 1000.0);
        // A name to show: hidden players become their class, as in the meter.
        string Name(CombatantSnapshot c) => settings.HideNames && !c.IsSelf ? ClassStyle.Name(c.Class) : c.Name;
        var names = players.ToDictionary(c => c.Name, Name);

        return new Dictionary<string, object?>
        {
            ["v"] = 1,
            ["region"] = settings.Region,
            ["lang"] = L.Lang,
            ["title"] = s.Title,
            ["zone"] = s.Zone,
            ["reason"] = s.Reason switch { EncounterEndReason.Kill => "kill", EncounterEndReason.Wipe => "wipe", _ => "other" },
            ["at"] = s.StartedAt.ToUnixTimeMilliseconds(),
            ["ms"] = s.CombatMs,
            ["total"] = s.TotalDamage,
            ["partyDps"] = Math.Round(s.PartyDps),
            ["partial"] = s.MostDamageUnsent,
            ["boss"] = s.Boss is { } b ? new Dictionary<string, object> { ["code"] = b.NpcCode, ["name"] = b.Name, ["maxHp"] = b.MaxHp, ["hp"] = b.Hp, ["known"] = b.MaxHpKnown } : null,
            ["players"] = players.Select(c =>
            {
                details.TryGetValue(c.ActorId, out var d);
                var p = new Dictionary<string, object?>
                {
                    ["n"] = Name(c),
                    ["c"] = c.Class == GameClass.Unknown ? null : c.Class.ToString().ToLowerInvariant(),
                    ["s"] = settings.HideNames && !c.IsSelf ? null : data.ServerName(c.ServerId),
                    ["self"] = c.IsSelf,
                    ["dmg"] = c.Damage,
                    ["dps"] = Math.Round(c.Dps),
                    ["adps"] = Math.Round(c.ActiveDps),
                    ["share"] = Math.Round(c.Share, 4),
                    ["hits"] = c.Hits,
                    ["crit"] = Math.Round(c.CritRate, 3),
                    ["max"] = c.MaxHit,
                    ["heal"] = c.Healing,
                    ["hps"] = Math.Round(c.Healing / seconds),
                    ["stag"] = c.Stagger,
                    ["taken"] = c.DamageTaken,
                    ["deaths"] = c.Deaths,
                    ["cp"] = c.CombatPower,
                    ["gs"] = c.GearScore,
                };
                if (d is not null)
                {
                    p["skills"] = d.Skills.OrderByDescending(k => k.Damage).Take(MaxSkills).Select(k => new Dictionary<string, object?>
                    {
                        ["n"] = k.Name, ["i"] = SkillIcons.IconName(k.SkillCode), ["h"] = k.Hits + k.DotTicks, ["d"] = k.Damage,
                        ["share"] = Math.Round(k.Share, 4), ["crit"] = k.CritRate is { } cr ? Math.Round(cr, 3) : null, ["max"] = k.Max,
                    }).ToList();
                    var (step, points) = Squeeze(d.PerSecond);
                    p["step"] = step;
                    p["ps"] = points;
                    p["deathList"] = (d.DeathRecaps ?? []).Take(5).Select(x => new Dictionary<string, object>
                    {
                        ["t"] = x.OffsetMs,
                        ["hits"] = x.Hits.TakeLast(8).Select(h => new Dictionary<string, object>
                        {
                            ["t"] = h.OffsetMs, ["src"] = names.GetValueOrDefault(h.Source, h.Source), ["sk"] = h.Skill, ["d"] = h.Damage,
                        }).ToList(),
                    }).ToList();
                }
                return p;
            }).ToList(),
        };
    }

    /// <summary>Damage per second, summed into buckets of whole seconds so a long fight stays at most 240 points.</summary>
    private static (int Step, List<long> Points) Squeeze(IReadOnlyList<long> perSecond)
    {
        var step = Math.Max(1, (int)Math.Ceiling(perSecond.Count / (double)MaxPoints));
        var list = new List<long>((perSecond.Count + step - 1) / step);
        for (var i = 0; i < perSecond.Count; i += step)
        {
            long sum = 0;
            for (var j = i; j < Math.Min(i + step, perSecond.Count); j++) sum += perSecond[j];
            list.Add(sum);
        }
        return (step, list);
    }
}
