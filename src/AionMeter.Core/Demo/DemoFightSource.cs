using AionMeter.Core.Events;

namespace AionMeter.Core.Demo;

/// <summary>
/// Synthesises a 5-player boss fight in real time so the overlay can be developed and checked without the game.
/// Uses actor ids and skill codes that do not collide with real ones.
/// </summary>
public sealed class DemoFightSource : IDisposable
{
    private sealed record Member(uint Id, string Name, GameClass Class, double Power, int[] Skills, bool Self = false);

    private const uint BossId = 9_000_001;
    private const uint SummonId = 9_000_101;

    private readonly Action<GameEvent> _sink;
    private readonly CancellationTokenSource _cts = new();
    private readonly Random _rng = new();
    private Task? _task;

    public DemoFightSource(Action<GameEvent> sink) => _sink = sink;

    public bool IsRunning => _task is { IsCompleted: false };

    public void Start(TimeSpan duration)
    {
        if (IsRunning) return;
        _task = Task.Run(() => Run(duration, _cts.Token));
    }

    private async Task Run(TimeSpan duration, CancellationToken ct)
    {
        Member[] party =
        [
            new(9_100_001, "Ilvane", GameClass.Elementalist, 0.95, [16010000, 16020000, 16030000, 16040000, 16050000], Self: true),
            new(9_100_002, "Borgrim", GameClass.Gladiator, 1.1, [11010000, 11020000, 11030000, 11040000]),
            new(9_100_003, "Talwyn", GameClass.Ranger, 1.0, [14010000, 14020000, 14030000, 14040000]),
            new(9_100_004, "Celesta", GameClass.Cleric, 0.75, [17010000, 17020000, 17030000]),
            new(9_100_005, "Hymnar", GameClass.Chanter, 0.55, [18010000, 18020000, 18030000]),
        ];

        var now = Now();
        _sink(new ZoneChangedEvent(now, 600001, "Krao Cave", true));
        _sink(new SelfIdentifiedEvent(now, party[0].Id, party[0].Name, 2001, party[0].Class));
        foreach (var m in party.Skip(1)) _sink(new PlayerSeenEvent(now, m.Id, m.Name, 2001, m.Class));
        _sink(new SummonSeenEvent(now, SummonId, party[0].Id, null, 9_900_001));
        _sink(DemoPetList(now));

        var totalPower = party.Sum(p => p.Power);
        var targetDps = 2_600_000.0;                    // party DPS, in line with current Global hard-mode numbers
        long maxHp = (long)(targetDps * duration.TotalSeconds);
        long hp = maxHp;
        _sink(new NpcSeenEvent(now, BossId, 0, maxHp, "Ultimate Berk (demo)"));
        _sink(new BattleStateEvent(now, BossId, true));
        _sink(new NpcHpEvent(now, BossId, hp, maxHp));

        var lastHpPush = now;
        var start = now;
        const uint gaugeMax = 1000;
        uint gauge = gaugeMax;
        var died = false;
        var revived = false;
        var buffsOn = false;
        var debuffOn = false;
        while (!ct.IsCancellationRequested && hp > 0)
        {
            await Task.Delay(_rng.Next(30, 90), ct).ConfigureAwait(false);
            now = Now();
            var m = party[_rng.Next(party.Length)];
            var skill = m.Skills[Math.Min(m.Skills.Length - 1, (int)Math.Floor(Math.Pow(_rng.NextDouble(), 1.6) * m.Skills.Length))];

            var flags = HitFlags.None;
            if (_rng.NextDouble() < 0.45) flags |= HitFlags.Critical;
            if (_rng.NextDouble() < 0.25) flags |= HitFlags.Back;
            else if (_rng.NextDouble() < 0.5) flags |= HitFlags.Front;
            if (_rng.NextDouble() < 0.3) flags |= HitFlags.Perfect;
            if (_rng.NextDouble() < 0.12) flags |= HitFlags.Heavy;
            var dot = _rng.NextDouble() < 0.12;
            if (dot) flags = HitFlags.Dot;

            // Each event averages out to the member's share of the target party DPS (≈ 16 events/s overall).
            var mean = targetDps / 16.0 * party.Length * m.Power / totalPower;
            var dmg = (long)(mean * (0.5 + _rng.NextDouble()) * ((flags & HitFlags.Critical) != 0 ? 1.5 : 1.0) * (dot ? 0.35 : 1.0));
            var hits = !dot && _rng.NextDouble() < 0.08 ? 3 : 1;
            var source = m.Self && _rng.NextDouble() < 0.15 ? SummonId : m.Id;
            if (source == SummonId) skill = 16020000;

            var progress = 1.0 - (double)hp / maxHp;
            // Buffs: the Chanter's party buff from 20 % on, a boss debuff from 50 %, each lifts the damage a little.
            if (!buffsOn && progress > 0.2)
            {
                buffsOn = true;
                var n = 0;
                foreach (var p in party) _sink(new BuffAppliedEvent(now, p.Id, 900 + n++, 127800001, 25_000, party[4].Id, 0, false, true));
                _sink(new BuffAppliedEvent(now, party[0].Id, 950, 163700001, 40_000, party[0].Id, 0, false, true));
            }
            if (!debuffOn && progress > 0.5)
            {
                debuffOn = true;
                _sink(new BuffAppliedEvent(now, BossId, 960, 12000532, 30_000, party[1].Id, 0, false, true));
            }
            if (buffsOn && now - start < 45_000) dmg = (long)(dmg * 1.18);
            if (debuffOn) dmg = (long)(dmg * 1.1);

            dmg = Math.Min(dmg, hp);
            hp -= dmg;
            _sink(new DamageEvent(now, source, BossId, skill, dmg, flags, hits));

            // Stagger: the Gladiator's and the Elementalist's first skills take some off the gauge.
            if (source == m.Id && !dot && (skill == 11010000 || skill == 16010000) && gauge > 0)
            {
                _sink(new StaggerMarkEvent(now, m.Id, BossId, skill));
                var take = (uint)Math.Min(gauge, _rng.Next(25, 45));
                gauge -= take;
                _sink(new StaggerGaugeEvent(now, BossId, gaugeMax, gauge, 2));
                if (gauge == 0)
                {
                    _sink(new StaggerBreakEvent(now, BossId));
                    gauge = gaugeMax;
                    _sink(new StaggerGaugeEvent(now, BossId, gaugeMax, gauge, 3));
                }
            }

            // The Chanter dies once at 40 % and is back on her feet a few seconds later.
            if (!died && progress > 0.4)
            {
                died = true;
                // What took her down (for the death recap): a cleave and a slam just before.
                _sink(new DamageEvent(now - 2_400, BossId, party[4].Id, 99_000_001, 21_000, HitFlags.None));
                _sink(new DamageEvent(now - 1_100, BossId, party[4].Id, 99_000_002, 14_500, HitFlags.None));
                _sink(new DamageEvent(now - 300, BossId, party[4].Id, 99_000_002, 16_500, HitFlags.None));
                _sink(new NpcHpEvent(now, party[4].Id, 0, 52_000, IsNpc: false));
            }
            else if (died && !revived && progress > 0.47)
            {
                revived = true;
                _sink(new NpcHpEvent(now, party[4].Id, 26_000, 52_000, IsNpc: false));
            }

            // Boss swings back at the party now and then.
            if (_rng.NextDouble() < 0.1)
                _sink(new DamageEvent(now, BossId, party[_rng.Next(2)].Id, 1, _rng.Next(800, 3000), HitFlags.None));

            if (now - lastHpPush >= 250 || hp == 0)
            {
                _sink(new NpcHpEvent(now, BossId, hp, maxHp));
                lastHpPush = now;
            }
        }
        if (hp <= 0)
        {
            _sink(new BattleStateEvent(Now(), BossId, false));
            // The kill gives a pet soul; then off to Verteron, where pet monsters come into view.
            await Task.Delay(1500, ct).ConfigureAwait(false);
            _sink(new PetSoulEvent(Now(), [(1005, 1)]));
            await Task.Delay(20_000, ct).ConfigureAwait(false);
            now = Now();
            _sink(new ZoneChangedEvent(now, 1010, "Verteron", false));
            uint id = 9_200_001;
            foreach (var (code, count) in new[] { (2100819, 3), (2100009, 2), (2101080, 1), (2100078, 2) })
                for (var i = 0; i < count; i++) _sink(new NpcSeenEvent(now, id++, code, 40_000));
        }
    }

    /// <summary>A made-up pet collection: most pets at level 3, the rest spread over levels 0-2.</summary>
    private static PetListEvent DemoPetList(long now)
    {
        var levels = new List<(int, int)>();
        var points = new List<(int, int)>();
        for (var pet = 1002; pet <= 1300; pet++)
        {
            var level = (pet * 7 % 10) switch { < 6 => 3, 6 or 7 => 2, 8 => 1, _ => 0 };
            if (level > 0) levels.Add((pet, level));
            if (level < 3) points.Add((pet, pet * 13 % (level switch { 0 => 5, 1 => 25, _ => 75 })));
        }
        void Set(int pet, int level, int p)
        {
            levels.RemoveAll(x => x.Item1 == pet);
            points.RemoveAll(x => x.Item1 == pet);
            if (level > 0) levels.Add((pet, level));
            if (level < 3) points.Add((pet, p));
        }
        Set(1005, 2, 57);   // Klaw
        Set(1126, 1, 12);   // Tog
        Set(1066, 2, 40);   // Papis
        Set(1170, 0, 3);    // Kerubian
        Set(1162, 3, 0);    // Kerubar
        return new PetListEvent(now, levels, points);
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public void Dispose()
    {
        _cts.Cancel();
        try { _task?.Wait(500); } catch (AggregateException) { }
        _cts.Dispose();
    }
}
