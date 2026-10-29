using System.Net.Http;
using System.Text;
using System.Text.Json;
using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace PeakMeter;

/// <summary>
/// The public ranking (off unless the player turns on "Join the ranking"). After a dungeon boss kill the meter sends
/// the player's own values only: name, server, class, combat power, DPS, healing and stagger per second, fight time
/// and party size. Never anyone else's: they did not agree. The server (tools/ranking-worker) keeps each player's best
/// and answers with the player's places, which the meter shows.
/// </summary>
public static class Ranking
{
    public const string Url = "https://peakmeter-ranking.peakmeter.workers.dev";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>The ranking page, opened at the player's region (and a boss and value, when given).</summary>
    public static string PageUrl(string region, int boss = 0, string metric = "dps") =>
        $"{Url}/ranking?region={Uri.EscapeDataString(region)}" + (boss > 0 ? $"&boss={boss}&metric={metric}" : "");

    /// <summary>The download page, to share PeakMeter.</summary>
    public const string HomeUrl = Url + "/";

    /// <summary>Where the player stands after a kill, for one value (DPS, or healing for a cleric).</summary>
    public sealed record Place(string BossName, int Boss, string Metric, long Value, long Best, bool Improved,
        int Rank, int Total, int? ClassRank, int? ClassTotal, int WeekRank, int WeekTotal);

    /// <summary>The values to send for this fight, or null when it does not count (see the checks).</summary>
    public static Dictionary<string, object>? Entry(FightRecord record, GameData data, string region)
    {
        var s = record.Summary;
        // Only full dungeon boss kills, seen from the start: anything else would not compare.
        if (s.Reason != EncounterEndReason.Kill || s.Boss is not { MaxHpKnown: true } boss || boss.MaxHp <= 0) return null;
        if (boss.Uncounted > boss.MaxHp * 0.02 || !data.IsDungeonZone(s.Zone) || s.CombatMs < 15_000) return null;
        if (!data.Npcs.TryGetValue(boss.NpcCode, out var def) || !def.IsBoss || def.IsDummy) return null;
        if (s.MostDamageUnsent) return null;
        var self = s.Combatants.FirstOrDefault(c => c.IsSelf && !c.IsUnknownSummons);
        if (self is null || self.Name.Length is 0 or > 24 || self.Damage <= 0) return null;

        var sec = s.CombatMs / 1000.0;
        return new Dictionary<string, object>
        {
            ["region"] = region,
            ["boss"] = boss.NpcCode,
            ["bossName"] = boss.Name,
            ["zone"] = s.Zone ?? "",
            ["bossMaxHp"] = boss.MaxHp,
            ["name"] = self.Name,
            ["server"] = self.ServerId,
            ["serverName"] = data.ServerName(self.ServerId),
            ["cls"] = self.Class.ToString(),
            ["cp"] = self.CombatPower,
            ["party"] = s.PlayerCount,
            ["durMs"] = s.CombatMs,
            ["damage"] = self.Damage,
            ["dps"] = Math.Round(self.Damage / sec, 1),
            ["hps"] = Math.Round(self.Healing / sec, 1),
            ["stagger"] = Math.Round(self.Stagger / sec, 1),
        };
    }

    /// <summary>Sends the fight in the background when it counts; any failure is only logged.</summary>
    /// <param name="placed">Called (on a worker thread) with the player's place once the server answered.</param>
    public static void Submit(FightRecord record, GameData data, string region, Action<string> log, Action<Place>? placed = null)
    {
        if (Entry(record, data, region) is not { } entry) return;
        var self = record.Summary.Combatants.First(c => c.IsSelf && !c.IsUnknownSummons);
        _ = Task.Run(async () =>
        {
            try
            {
                using var body = new StringContent(JsonSerializer.Serialize(entry), Encoding.UTF8, "application/json");
                using var r = await Http.PostAsync($"{Url}/api/submit", body);
                if (!r.IsSuccessStatusCode)
                {
                    log($"Ranking: not taken ({(int)r.StatusCode})");
                    return;
                }
                log("Ranking: kill sent");
                // Healers are ranked by their healing, everyone else by damage.
                var metric = self.Class == GameClass.Cleric && self.Healing > self.Damage / 2 ? "hps" : "dps";
                if (placed is not null && ParsePlace(await r.Content.ReadAsStringAsync(), metric, (string)entry["bossName"], (int)entry["boss"]) is { } p)
                    placed(p);
            }
            catch
            {
                log("Ranking: server not reachable");
            }
        });
    }

    /// <summary>Reads one value's place from the server's answer; null when it holds none (older server, value not sent).</summary>
    public static Place? ParsePlace(string json, string metric, string bossName, int boss)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("places", out var places)) return null;
            if (!places.TryGetProperty(metric, out var m) && !places.TryGetProperty(metric = "dps", out m)) return null;
            static int I(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
            static long L(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : 0;
            static int? N(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
            var rank = I(m, "place");
            if (rank <= 0) return null;
            return new Place(bossName, boss, metric, L(m, "value"), L(m, "best"),
                m.TryGetProperty("improved", out var imp) && imp.ValueKind == JsonValueKind.True,
                rank, I(m, "total"), N(m, "clsPlace"), N(m, "clsTotal"), I(m, "weekPlace"), I(m, "weekTotal"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The card after a kill: "Place 4 of 37" and the details.</summary>
    public static (string Title, string Detail) Describe(Place p, GameClass cls)
    {
        var what = p.Metric == "hps" ? L.S("Heilung", "Healing") : "DPS";
        var title = L.S($"Ranking: Platz {p.Rank} von {p.Total}", $"Ranking: place {p.Rank} of {p.Total}");
        var parts = new List<string> { $"{p.BossName} · {what} {Format.Compact(p.Value)}" };
        if (p.Improved && p.Value >= p.Best) parts.Add(L.S("Neuer Bestwert!", "New personal best!"));
        else if (p.Best > 0) parts.Add(L.S($"Bestwert {Format.Compact(p.Best)}", $"Best {Format.Compact(p.Best)}"));
        if (p.WeekRank > 0) parts.Add(L.S($"Woche: Platz {p.WeekRank}", $"Week: place {p.WeekRank}"));
        if (p.ClassRank is { } cr && p.ClassTotal is > 1) parts.Add($"{ClassStyle.Name(cls)}: " + L.S($"Platz {cr}", $"place {cr}"));
        return (title, string.Join(" · ", parts));
    }
}
