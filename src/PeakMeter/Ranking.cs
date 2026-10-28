using System.Net.Http;
using System.Text;
using System.Text.Json;
using AionMeter.Core.Combat;
using AionMeter.Core.Game;

namespace PeakMeter;

/// <summary>
/// The public ranking (off unless the player turns on "Join the ranking"). After a dungeon boss kill the meter sends
/// the player's own values only: name, server, class, combat power, DPS, healing and stagger per second, fight time
/// and party size. Never anyone else's: they did not agree. The server (tools/ranking-worker) keeps each player's best.
/// </summary>
public static class Ranking
{
    public const string Url = "https://peakmeter-ranking.peakmeter.workers.dev";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>The ranking page, opened at the player's region.</summary>
    public static string PageUrl(string region) => $"{Url}/?region={Uri.EscapeDataString(region)}";

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
    public static void Submit(FightRecord record, GameData data, string region, Action<string> log)
    {
        if (Entry(record, data, region) is not { } entry) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var body = new StringContent(JsonSerializer.Serialize(entry), Encoding.UTF8, "application/json");
                using var r = await Http.PostAsync($"{Url}/api/submit", body);
                log(r.IsSuccessStatusCode ? "Ranking: kill sent" : $"Ranking: not taken ({(int)r.StatusCode})");
            }
            catch
            {
                log("Ranking: server not reachable");
            }
        });
    }
}
