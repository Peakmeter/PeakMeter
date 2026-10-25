using AionMeter.Core.Combat;
using AionMeter.Core.Storage;

namespace PeakMeter;

/// <summary>Your last DPS and the best kill time per boss, read from the saved fights (cached until a fight is saved).</summary>
public sealed class Records
{
    private readonly HistoryStore _history;
    private Dictionary<int, List<HistoryEntry>>? _byBoss;

    public Records(HistoryStore history, Meter meter)
    {
        _history = history;
        meter.FightSaved += () => _byBoss = null;
    }

    /// <param name="exceptStart">The fight on screen: once saved it must not be compared with itself.</param>
    public (double LastSelfDps, long BestKillMs)? For(int bossCode, DateTimeOffset? exceptStart = null)
    {
        if (bossCode == 0) return null;
        _byBoss ??= Build();
        if (!_byBoss.TryGetValue(bossCode, out var fights)) return null;
        double last = 0;
        long best = 0;
        foreach (var e in fights) // newest first
        {
            if (exceptStart is { } x && Math.Abs((e.StartedAt - x).TotalSeconds) < 2) continue;
            if (last <= 0 && e.SelfDps > 0) last = e.SelfDps;
            if (e.Reason == EncounterEndReason.Kill && e.CombatMs > 0 && (best == 0 || e.CombatMs < best)) best = e.CombatMs;
        }
        return last > 0 || best > 0 ? (last, best) : null;
    }

    /// <summary>The fastest kill of each boss, to mark it in the logs.</summary>
    public long BestKillMs(int bossCode) => For(bossCode)?.BestKillMs ?? 0;

    private Dictionary<int, List<HistoryEntry>> Build()
    {
        var map = new Dictionary<int, List<HistoryEntry>>();
        try
        {
            foreach (var e in _history.List().OrderByDescending(e => e.StartedAt))
            {
                if (e.BossCode == 0) continue;
                if (!map.TryGetValue(e.BossCode, out var list)) map[e.BossCode] = list = [];
                list.Add(e);
            }
        }
        catch { }
        return map;
    }
}
