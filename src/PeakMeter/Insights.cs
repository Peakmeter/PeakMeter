using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;

namespace PeakMeter;

/// <summary>What a fight says about one player beyond the numbers: idle time and why they died.</summary>
public static class Insights
{
    /// <summary>A pause between two of your own hits longer than this counts as idle (a normal rotation never waits this long).</summary>
    public const int IdleGapMs = 2_500;

    /// <summary>
    /// Idle time: the sum of the pauses longer than <see cref="IdleGapMs"/> between your own hits (damage over time does
    /// not count as acting), from your first hit to your last. Deaths, running and phases without a target count too:
    /// it is the time you dealt nothing.
    /// </summary>
    public static (long IdleMs, long ActiveMs) Idle(IReadOnlyList<CastRecord> casts)
    {
        long idle = 0, first = -1, last = -1;
        foreach (var c in casts)
        {
            if ((c.Flags & HitFlags.Dot) != 0) continue;
            if (first < 0) first = c.OffsetMs;
            else if (c.OffsetMs - last > IdleGapMs) idle += c.OffsetMs - last;
            last = Math.Max(last, c.OffsetMs);
        }
        return first < 0 ? (0, 0) : (idle, Math.Max(0, last - first));
    }

    /// <summary>"Idle 12 s (8 %)", or "" when too short to say.</summary>
    public static string IdleText(IReadOnlyList<CastRecord> casts)
    {
        var (idle, span) = Idle(casts);
        if (span < 10_000) return "";
        return L.S("Leerlauf", "Idle") + $" {idle / 1000} s ({(double)idle / span * 100:0}%)";
    }

    /// <summary>
    /// One line per death: "☠ 2:14 · 185k in 8 s: Fire Burst 85k (Boss), Smash 40k". The hardest hits first; when no
    /// hit was seen (e.g. a fall, or the hits did not reach this client) only the time.
    /// </summary>
    public static IReadOnlyList<string> DeathLines(IReadOnlyList<DeathRecap>? recaps, int max = 3)
    {
        if (recaps is not { Count: > 0 }) return [];
        var lines = new List<string>();
        foreach (var d in recaps.TakeLast(max))
        {
            var head = $"☠ {Format.ClockShort(d.OffsetMs)}";
            if (d.Hits.Count == 0)
            {
                lines.Add(head + " · " + L.S("keine Treffer gesehen", "no hits seen"));
                continue;
            }
            var span = Math.Max(1, (d.OffsetMs - d.Hits[0].OffsetMs + 999) / 1000);
            var top = d.Hits
                .GroupBy(h => (h.Source, h.Skill))
                .Select(g => (g.Key.Source, g.Key.Skill, Damage: g.Sum(h => h.Damage), Count: g.Count()))
                .OrderByDescending(g => g.Damage)
                .Take(3)
                .Select(g => (g.Skill.Length > 0 ? $"{g.Skill} " : "") + Format.Compact(g.Damage) + (g.Count > 1 ? $" ×{g.Count}" : "") + $" ({g.Source})");
            lines.Add($"{head} · {Format.Compact(d.Total)} " + L.S($"in {span} s", $"in {span} s") + ": " + string.Join(", ", top));
        }
        return lines;
    }
}
