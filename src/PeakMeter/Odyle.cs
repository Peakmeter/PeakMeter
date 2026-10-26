namespace PeakMeter;

/// <summary>
/// Odyle energy in two pools, as the game keeps it: the regenerating pool (+15 every 3 hours up to 560, 840 with Quai)
/// and bonus energy from items on top, which does not regenerate and is not capped. The game shows their sum.
/// The server sends both whenever they change (opcode 0C61); between those messages PeakMeter counts the regeneration
/// forward itself. Without game data the player can enter the value by hand.
/// </summary>
public sealed class Odyle(Settings s)
{
    public static readonly TimeSpan Tick = TimeSpan.FromHours(3);
    public const int Claim = 40;

    public bool Known => s.OdyleValue >= 0 && s.OdyleSetAt is not null;
    public int Cap => s.OdyleQuai ? 840 : 560;
    public int Regen => Math.Clamp(s.OdyleRegen, 1, 200);
    public int Bonus => Known ? Math.Max(0, s.OdyleBonus) : 0;
    public DateTimeOffset? FromGameAt => s.OdyleFromGameAt;

    /// <summary>Raised (UI thread) when the game sent a new value.</summary>
    public event Action? Updated;

    private long Ticks(DateTimeOffset now) =>
        s.OdyleSetAt is { } at && now > at ? (now - at).Ticks / Tick.Ticks : 0;

    /// <summary>The regenerating pool right now.</summary>
    public int Pool(DateTimeOffset now)
    {
        if (!Known) return 0;
        var v = s.OdyleValue;
        return v >= Cap ? v : (int)Math.Min(Cap, v + Ticks(now) * Regen);
    }

    /// <summary>What the game shows: pool + bonus.</summary>
    public int Current(DateTimeOffset now) => Pool(now) + Bonus;

    /// <summary>The pool is at its cap: regeneration is being wasted.</summary>
    public bool Full(DateTimeOffset now) => Known && Pool(now) >= Cap;

    public DateTimeOffset? NextTick(DateTimeOffset now) =>
        Known && !Full(now) ? s.OdyleSetAt!.Value + Tick * (Ticks(now) + 1) : null;

    public DateTimeOffset? FullAt(DateTimeOffset now)
    {
        if (!Known || Full(now)) return null;
        var needed = (long)Math.Ceiling((Cap - (double)s.OdyleValue) / Regen);
        return s.OdyleSetAt!.Value + Tick * needed;
    }

    /// <summary>Straight from the server: both pools exactly.</summary>
    public void SetFromGame(int pool, int bonus)
    {
        var changed = s.OdyleValue != pool || s.OdyleBonus != bonus || s.OdyleFromGameAt is null;
        s.OdyleValue = Math.Max(0, pool);
        s.OdyleBonus = Math.Max(0, bonus);
        s.OdyleSetAt = DateTimeOffset.Now;
        s.OdyleFromGameAt = DateTimeOffset.Now;
        if (pool < Cap) s.OdyleFullNotified = false;
        s.Save();
        if (changed) Updated?.Invoke();
    }

    /// <summary>Another character logged in: its last known value (it regenerated while you were away).</summary>
    public void Restore(int pool, int bonus, DateTimeOffset at)
    {
        s.OdyleValue = Math.Max(0, pool);
        s.OdyleBonus = Math.Max(0, bonus);
        s.OdyleSetAt = at;
        s.OdyleFromGameAt = at;
        s.OdyleFullNotified = false;
        s.Save();
        Updated?.Invoke();
    }

    /// <summary>A character without a known value logged in: show nothing rather than another character's energy.</summary>
    public void Clear()
    {
        if (!Known && s.OdyleFromGameAt is null) return;
        s.OdyleValue = -1;
        s.OdyleBonus = 0;
        s.OdyleSetAt = null;
        s.OdyleFromGameAt = null;
        s.OdyleFullNotified = false;
        s.Save();
        Updated?.Invoke();
    }

    /// <summary>The total the game shows, entered by hand: anything above the cap counts as bonus.</summary>
    public void Set(int total)
    {
        total = Math.Clamp(total, 0, 9999);
        s.OdyleValue = Math.Min(total, Cap);
        s.OdyleBonus = Math.Max(0, total - Cap);
        s.OdyleSetAt = DateTimeOffset.Now;
        s.OdyleFullNotified = false;
        s.Save();
    }

    /// <summary>By hand: gains (potions) go to the bonus; spending takes the pool first. Keeps the 3-hour rhythm.</summary>
    public void Adjust(int delta)
    {
        var now = DateTimeOffset.Now;
        if (!Known)
        {
            Set(Math.Max(0, delta));
            return;
        }
        var pool = Pool(now);
        var bonus = Bonus;
        if (delta >= 0) bonus += delta;
        else
        {
            var take = Math.Min(pool, -delta);
            pool -= take;
            bonus = Math.Max(0, bonus - (-delta - take));
        }
        s.OdyleSetAt = s.OdyleSetAt!.Value + Tick * Ticks(now);
        s.OdyleValue = pool;
        s.OdyleBonus = bonus;
        if (pool < Cap) s.OdyleFullNotified = false;
        s.Save();
    }

    public void SetQuai(bool on)
    {
        if (Known) Adjust(0); // freeze the count under the old cap first
        s.OdyleQuai = on;
        s.OdyleFullNotified = false;
        s.Save();
    }

    /// <summary>True once when the pool reaches its cap (nothing regenerates past it: time to spend).</summary>
    public bool TakeFullAlert(DateTimeOffset now)
    {
        if (!Known || s.OdyleFullNotified || !Full(now)) return false;
        s.OdyleFullNotified = true;
        s.Save();
        return true;
    }
}
