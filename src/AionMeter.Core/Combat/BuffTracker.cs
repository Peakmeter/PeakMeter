using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace AionMeter.Core.Combat;

/// <summary>
/// Buffs and debuffs ("abnormals") on players and monsters, as the server adds, renews and removes them
/// (<c>2a 38</c>, <c>2b 38</c>, <c>2c 38</c>; layout from A2Tools-DPS-Meter / Daevalog, GPL-3.0). Kept as time spans,
/// so a finished fight can say how long each buff was up and how much damage was done with and without it.
/// Passives (no timer) are left out: they are on all the time and say nothing.
/// </summary>
public sealed class BuffTracker
{
    /// <summary>Spans longer than this are not fight buffs (food, account effects).</summary>
    private const long LongestMs = 2 * 60 * 60 * 1000;
    private const long KeepMs = 30 * 60 * 1000;
    private const int MaxSpans = 50_000;

    private sealed class Live
    {
        public uint Abnormal;
        public uint Caster;
        public long StartMs;
        public long EndMs;
    }

    private readonly record struct Span(uint Entity, uint Abnormal, uint Caster, long StartMs, long EndMs);

    private readonly Dictionary<(uint Entity, int Instance), Live> _live = new();
    private readonly List<Span> _closed = new();

    public void OnApplied(BuffAppliedEvent e)
    {
        if (e.LengthMs is < 0 or > LongestMs) return;
        var key = (e.EntityId, e.Instance);
        if (_live.TryGetValue(key, out var cur))
        {
            if (cur.Abnormal == e.Abnormal && !e.IsAdd)
            {
                // A renewal: the length runs from the instance's start, unless it was applied again.
                if (e.Restarted) cur.StartMs = Math.Min(cur.EndMs, e.TimeMs);
                cur.EndMs = (e.Restarted ? e.TimeMs : cur.StartMs) + e.LengthMs;
                cur.Caster = e.Caster;
                return;
            }
            Close(key, cur, e.TimeMs);
        }
        _live[key] = new Live { Abnormal = e.Abnormal, Caster = e.Caster, StartMs = e.TimeMs, EndMs = e.TimeMs + e.LengthMs };
    }

    public void OnRemoved(BuffRemovedEvent e)
    {
        foreach (var instance in e.Instances)
            if (_live.TryGetValue((e.EntityId, instance), out var cur))
                Close((e.EntityId, instance), cur, e.TimeMs);
    }

    /// <summary>A map load: every id is new afterwards.</summary>
    public void Clear(long nowMs)
    {
        foreach (var (key, cur) in _live.ToList()) Close(key, cur, nowMs);
    }

    private void Close((uint Entity, int Instance) key, Live cur, long nowMs)
    {
        _live.Remove(key);
        var end = Math.Min(cur.EndMs, nowMs);
        if (end > cur.StartMs) _closed.Add(new Span(key.Entity, cur.Abnormal, cur.Caster, cur.StartMs, end));
        if (_closed.Count > MaxSpans || (_closed.Count > 0 && nowMs - _closed[0].EndMs > KeepMs))
            _closed.RemoveAll(s => nowMs - s.EndMs > KeepMs);
        if (_closed.Count > MaxSpans) _closed.RemoveRange(0, _closed.Count - MaxSpans);
    }

    private IEnumerable<Span> SpansIn(long fromMs, long toMs)
    {
        foreach (var s in _closed)
            if (s.EndMs > fromMs && s.StartMs < toMs) yield return s;
        foreach (var ((entity, _), cur) in _live)
            if (cur.EndMs > fromMs && cur.StartMs < toMs) yield return new Span(entity, cur.Abnormal, cur.Caster, cur.StartMs, cur.EndMs);
    }

    /// <summary>
    /// Every named buff on the fight's players, and every named debuff on its main target: uptime, and damage per
    /// second with and without it (the players who had it, each within their own first-to-last hit; for the target,
    /// the whole group's damage).
    /// </summary>
    internal IReadOnlyList<BuffSummary> Summarize(Encounter enc, uint? targetId, GameData data)
    {
        var players = enc.Combatants.Values
            .Where(c => c.ActorId != Combatant.UnknownSummonsId && c.PerSecond.Count > 0)
            .ToDictionary(c => c.ActorId);
        if (players.Count == 0) return [];
        var seconds = players.Values.Max(c => c.PerSecond.Count);
        var from = enc.StartMs;
        var to = enc.StartMs + seconds * 1000L;

        // Which seconds of the fight each (entity, abnormal) was on: on at the middle of the second.
        var on = new Dictionary<(uint Entity, uint Abnormal), bool[]>();
        var casters = new Dictionary<uint, Dictionary<uint, long>>();
        foreach (var s in SpansIn(from, to))
        {
            if (!players.ContainsKey(s.Entity) && s.Entity != targetId) continue;
            if (data.AbnormalName(s.Abnormal) is null) continue;
            if (!on.TryGetValue((s.Entity, s.Abnormal), out var secs)) on[(s.Entity, s.Abnormal)] = secs = new bool[seconds];
            var first = (int)Math.Max(0, Math.Ceiling((s.StartMs - from - 500) / 1000.0));
            var last = (int)Math.Min(seconds - 1, Math.Floor((s.EndMs - from - 500) / 1000.0));
            for (var i = first; i <= last; i++) secs[i] = true;
            if (!casters.TryGetValue(s.Abnormal, out var by)) casters[s.Abnormal] = by = new();
            by[s.Caster] = by.GetValueOrDefault(s.Caster) + Math.Min(s.EndMs, to) - Math.Max(s.StartMs, from);
        }

        var group = new long[seconds];
        foreach (var c in players.Values)
            for (var i = 0; i < c.PerSecond.Count; i++) group[i] += c.PerSecond[i];

        var result = new List<BuffSummary>();
        foreach (var abnormal in on.Keys.Select(k => k.Abnormal).Distinct())
        {
            var name = data.AbnormalName(abnormal)!;
            var caster = casters.GetValueOrDefault(abnormal)?.MaxBy(kv => kv.Value).Key ?? 0;
            var casterName = players.TryGetValue(caster, out var cp) ? cp.Name : null;

            if (targetId is { } target && on.TryGetValue((target, abnormal), out var onTarget))
            {
                var (with, without, up) = Split(group, onTarget, 0, seconds - 1);
                if (up > 0)
                    result.Add(new BuffSummary(abnormal, name, data.AbnormalIcon(abnormal), true, 0, players.Count,
                        (double)up / seconds, with, without, casterName));
            }

            // Players who had it: their damage while it was on, against their damage while it was off.
            long withDmg = 0, withSecs = 0, withoutDmg = 0, withoutSecs = 0;
            var had = 0;
            double uptime = 0;
            foreach (var c in players.Values)
            {
                if (!on.TryGetValue((c.ActorId, abnormal), out var secs)) continue;
                var firstHit = (int)Math.Clamp((c.FirstHitMs - from) / 1000, 0, seconds - 1);
                var lastHit = (int)Math.Clamp((c.LastHitMs - from) / 1000, firstHit, seconds - 1);
                var perSecond = new long[seconds];
                c.PerSecond.CopyTo(0, perSecond, 0, c.PerSecond.Count);
                long w = 0, wo = 0;
                int ws = 0, wos = 0;
                for (var i = firstHit; i <= lastHit; i++)
                {
                    if (secs[i]) { w += perSecond[i]; ws++; }
                    else { wo += perSecond[i]; wos++; }
                }
                if (ws == 0) continue;
                had++;
                uptime += (double)ws / (lastHit - firstHit + 1);
                withDmg += w; withSecs += ws; withoutDmg += wo; withoutSecs += wos;
            }
            if (had == 0) continue;
            result.Add(new BuffSummary(abnormal, name, data.AbnormalIcon(abnormal), false, had, players.Count, uptime / had,
                withSecs >= 3 ? (double)withDmg / withSecs : null,
                withoutSecs >= 3 ? (double)withoutDmg / withoutSecs : null,
                casterName));
        }
        return result.OrderByDescending(b => b.Uptime * Math.Max(1, b.Players)).Take(60).ToList();
    }

    private static (double? With, double? Without, int Up) Split(long[] perSecond, bool[] on, int first, int last)
    {
        long w = 0, wo = 0;
        int ws = 0, wos = 0;
        for (var i = first; i <= last; i++)
        {
            if (on[i]) { w += perSecond[i]; ws++; }
            else { wo += perSecond[i]; wos++; }
        }
        return (ws >= 3 ? (double)w / ws : null, wos >= 3 ? (double)wo / wos : null, ws);
    }
}

/// <summary>
/// One buff in a finished fight. <paramref name="OnTarget"/>: a debuff on the fight's main target (then
/// <paramref name="DpsWith"/> / <paramref name="DpsWithout"/> are the group's DPS); otherwise a buff on
/// <paramref name="Players"/> of the fight's <paramref name="PlayersInFight"/> players (their own DPS).
/// <paramref name="Uptime"/> is 0..1.
/// </summary>
public sealed record BuffSummary(
    uint Abnormal,
    string Name,
    string? Icon,
    bool OnTarget,
    int Players,
    int PlayersInFight,
    double Uptime,
    double? DpsWith,
    double? DpsWithout,
    string? CasterName);
