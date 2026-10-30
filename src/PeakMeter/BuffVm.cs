using System.Windows.Media;
using AionMeter.Core;
using AionMeter.Core.Combat;

namespace PeakMeter;

/// <summary>
/// One buff of a saved fight, said in plain words: the verdict ("+12 % Schaden"), how long it was up and on whom,
/// and the raw DPS with / without in the tooltip.
/// </summary>
public sealed class BuffVm
{
    private static readonly Brush Good = Frozen(0x5F, 0xD3, 0x8D);
    private static readonly Brush Neutral = Frozen(0x9A, 0xA4, 0xB2);
    private static readonly Brush GoodBar = Frozen(0x5F, 0xD3, 0x8D, 0x88);
    private static readonly Brush NeutralBar = Frozen(0x9A, 0xA4, 0xB2, 0x55);

    /// <summary>Below this the difference is noise, not the buff.</summary>
    private const double Noise = 0.03;

    public ImageSource? Icon { get; init; }
    public string Name { get; init; } = "";
    public string Verdict { get; init; } = "";
    public Brush VerdictBrush { get; init; } = Neutral;
    public string Detail { get; init; } = "";
    public string Tip { get; init; } = "";
    public double BarWidth { get; init; }
    public Brush BarBrush { get; init; } = NeutralBar;

    public static BuffVm From(BuffSummary b, double width)
    {
        double? gain = b is { DpsWith: { } w, DpsWithout: { } wo } && wo > 0 ? w / wo - 1 : null;
        var up = $"{b.Uptime * 100:0}%";

        var verdict = gain switch
        {
            null when b.Uptime >= 0.95 => L.S("immer aktiv", "always up"),
            null => L.S("nicht messbar", "can't tell"),
            >= Noise => L.S($"+{gain * 100:0}% Schaden", $"+{gain * 100:0}% damage"),
            > -Noise => L.S("kaum Unterschied", "barely any difference"),
            _ => L.S("kein Vorteil messbar", "no gain seen"),
        };
        var helped = gain >= Noise;

        var parts = new List<string>();
        if (b.OnTarget) parts.Add(L.S("Debuff auf dem Boss", "Debuff on the boss"));
        parts.Add(L.S($"{up} des Kampfes aktiv", $"up {up} of the fight"));
        if (!b.OnTarget && b.PlayersInFight > 1)
            parts.Add(b.Players >= b.PlayersInFight
                ? L.S($"bei allen {b.PlayersInFight} Spielern", $"on all {b.PlayersInFight} players")
                : b.Players == 1
                    ? L.S("bei 1 Spieler", "on 1 player")
                    : L.S($"bei {b.Players} von {b.PlayersInFight} Spielern", $"on {b.Players} of {b.PlayersInFight} players"));
        if (b.CasterName is { Length: > 0 } by) parts.Add(L.S($"von {by}", $"by {by}"));

        return new BuffVm
        {
            Icon = b.Icon is { } icon ? SkillIcons.ByName(icon) : null,
            Name = b.Name,
            Verdict = verdict,
            VerdictBrush = helped ? Good : Neutral,
            Detail = string.Join(" · ", parts),
            Tip = Tooltip(b, gain),
            BarWidth = Math.Round(Math.Max(0, width) * Math.Clamp(b.Uptime, 0, 1)),
            BarBrush = helped ? GoodBar : NeutralBar,
        };
    }

    private static string Tooltip(BuffSummary b, double? gain)
    {
        var who = b.OnTarget ? L.S("Gruppen-DPS", "Group DPS") : L.S("DPS der Spieler mit dem Buff", "DPS of the players who had it");
        var numbers = b is { DpsWith: { } w, DpsWithout: { } wo }
            ? L.S($"{who}\n  mit:   {Format.Compact(w)}\n  ohne: {Format.Compact(wo)}", $"{who}\n  with:      {Format.Compact(w)}\n  without: {Format.Compact(wo)}")
            : b.Uptime >= 0.95
                ? L.S("War fast den ganzen Kampf aktiv, daher gibt es keine Zeit ohne ihn zum Vergleichen.",
                      "Was up almost the whole fight, so there's no time without it to compare.")
                : L.S("Zu kurz aktiv (oder zu kurz weg), um es zu vergleichen.", "Up (or down) too briefly to compare.");
        var how = b.OnTarget
            ? L.S("Verglichen wird der Schaden der Gruppe, solange der Debuff auf dem Boss war, mit der Zeit ohne ihn.",
                  "Compares the group's damage while the debuff was on the boss with the time without it.")
            : L.S("Verglichen wird der Schaden der Spieler, solange sie den Buff hatten, mit der Zeit ohne ihn.",
                  "Compares the players' damage while they had the buff with the time without it.");
        var note = gain is < -Noise
            ? L.S("\n\nWeniger Schaden mit Buff heißt nicht, dass er schadet: oft wird er in schweren Phasen genutzt (Laufen, Ausweichen, Boss weg).",
                  "\n\nLess damage with the buff doesn't mean it hurts: it's often used in hard phases (moving, dodging, boss away).")
            : L.S("\n\nNur ein Richtwert, kein exakter Wert.", "\n\nA rough guide, not an exact number.");
        return $"{numbers}\n\n{how}{note}";
    }

    private static Brush Frozen(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
