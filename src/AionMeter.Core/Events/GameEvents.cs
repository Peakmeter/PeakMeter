namespace AionMeter.Core.Events;

/// <summary>Quality flags of a single hit, normalised from whatever the wire format uses.</summary>
[Flags]
public enum HitFlags : ushort
{
    None = 0,
    Critical = 1 << 0,
    Back = 1 << 1,
    Front = 1 << 2,
    Perfect = 1 << 3,
    Heavy = 1 << 4,      // "Smite" in AionFlex terms
    Multi = 1 << 5,
    Block = 1 << 6,
    Parry = 1 << 7,
    Endure = 1 << 8,     // Iron Wall
    Evade = 1 << 9,
    Dot = 1 << 10,
    Restore = 1 << 11,
    Double = 1 << 12,
}

public enum GameClass : byte
{
    Unknown = 0,
    Gladiator,
    Templar,
    Ranger,
    Assassin,
    Sorcerer,
    Elementalist,
    Cleric,
    Chanter,
    Brawler,
}

/// <summary>Everything the combat engine consumes. Time is Unix milliseconds taken from the packet capture.</summary>
public abstract record GameEvent(long TimeMs);

/// <param name="Damage">Total damage of the record, additional hits included.</param>
/// <param name="HitCount">1 + number of additional hits carried by the record.</param>
/// <param name="PowerScalar">The actor's damage multiplier in 1/100 % (players 16000–22000, mobs 10000); 0 if absent.
/// Summons inherit their owner's value, which lets unannounced summons be merged into the right player.</param>
public sealed record DamageEvent(
    long TimeMs,
    uint SourceId,
    uint TargetId,
    int SkillCode,
    long Damage,
    HitFlags Flags,
    int HitCount = 1,
    int PowerScalar = 0) : GameEvent(TimeMs);

public sealed record HealEvent(long TimeMs, uint SourceId, uint TargetId, int SkillCode, long Amount, HitFlags Flags) : GameEvent(TimeMs);

/// <summary>The local player's own character.</summary>
/// <param name="CombatPower">The character's combat power (CP) as the game shows it; 0 when the record did not carry it.</param>
public sealed record SelfIdentifiedEvent(long TimeMs, uint ActorId, string Name, int ServerId, GameClass Class, int Level = 0,
    long CombatPower = 0) : GameEvent(TimeMs);

/// <summary>One member of the party roster: the only place the server gives other players' gear score and combat power.</summary>
/// <param name="GearScore">Equipment item level ("GS"); 0 when it did not read.</param>
/// <param name="CombatPower">Combat power ("CP"), as on the character sheet.</param>
/// <param name="DbId">The character's account-wide id (its top 16 bits are its server): what the ready check names.</param>
public sealed record PartyMemberInfo(string Name, int ServerId, int Level, int GearScore, long CombatPower, GameClass Class, ulong DbId = 0);

/// <summary>
/// The party roster (<c>02 97</c>), sent whenever the party changes. Members are named, not identified by entity id:
/// the meter joins them to the players it sees by name.
/// </summary>
/// <summary>Odyle energy from the server (PeakMeter): <paramref name="Regen"/> refills by itself up to the cap,
/// <paramref name="Bonus"/> comes from items and does not; the game shows their sum.</summary>
/// <param name="Change">0 = the login list (whose character is not known yet); otherwise a live update of the
/// logged-in character (<see cref="ElsewhereChange"/> when it came in another message).</param>
/// <param name="Opcode">The message it came in.</param>
public sealed record OdyleEnergyEvent(long TimeMs, int Regen, int Bonus, int Change, byte[] Raw, ushort Opcode = 0) : GameEvent(TimeMs)
{
    /// <summary><see cref="Change"/> of an Odyle record found in another message (a reward, a dungeon exit …).</summary>
    public const int ElsewhereChange = int.MinValue;
}

/// <summary>A counter message (<c>0C 61</c>) that is not Odyle energy: kept raw until its layout is known (PeakMeter).</summary>
public sealed record ResourceRawEvent(long TimeMs, byte[] Raw) : GameEvent(TimeMs);

/// <param name="PartyKey">The party's id: the party finder also sends other parties' rosters.</param>
/// <param name="Complete">Every member the record declares was read (a partial one only updates those it read).</param>
public sealed record PartyRosterEvent(long TimeMs, IReadOnlyList<PartyMemberInfo> Members, uint PartyKey = 0, bool Complete = true) : GameEvent(TimeMs);

/// <summary>You are no longer in a party (<c>13 92 00 00</c> open world, <c>1D 97 00 00</c> dungeon / raid; layout as
/// documented by A2Tools-DPS-Meter, GPL-3.0).</summary>
public sealed record PartyLeftEvent(long TimeMs) : GameEvent(TimeMs);

/// <summary>A ready check answer in the party lobby: <c>15 97 00 00 &lt;ready&gt;</c> is your own,
/// <c>16 97 &lt;dbid u64&gt; &lt;ready&gt;</c> a party member's (found by PeakMeter, 2026-10-28).</summary>
public sealed record ReadyCheckEvent(long TimeMs, ulong DbId, bool Ready, bool IsSelf) : GameEvent(TimeMs);

/// <summary>A player's server, known by name only (summon / loot owner records).</summary>
public sealed record PlayerServerEvent(long TimeMs, string Name, int ServerId) : GameEvent(TimeMs);

public sealed record PlayerSeenEvent(long TimeMs, uint ActorId, string Name, int ServerId, GameClass Class) : GameEvent(TimeMs);

/// <param name="Name">Display name when not resolvable from the NPC tables (demo / tests).</param>
/// <param name="X">Position where it came into view (0 when unknown); Y, Z likewise.</param>
public sealed record NpcSeenEvent(long TimeMs, uint ActorId, int NpcCode, long MaxHp = 0, string? Name = null,
    float X = 0, float Y = 0, float Z = 0) : GameEvent(TimeMs);

/// <summary>
/// A pet / spirit / totem / skill entity. Owner is known by id, only by the character name it carries, or not at all
/// (both empty) — it is still never a player.
/// </summary>
public sealed record SummonSeenEvent(long TimeMs, uint ActorId, uint OwnerId, string? OwnerName, int NpcCode) : GameEvent(TimeMs);

/// <summary>HP update. <paramref name="MaxHp"/> is 0 when the packet does not carry it. <paramref name="IsNpc"/> is
/// true when the record itself says the entity is an NPC; otherwise it may be a player and only updates known NPCs.</summary>
public sealed record NpcHpEvent(long TimeMs, uint ActorId, long CurrentHp, long MaxHp, bool IsNpc = true) : GameEvent(TimeMs);

/// <summary>An entity died, or (<paramref name="AlreadyDead"/>) came into view already dead.</summary>
public sealed record DeathEvent(long TimeMs, uint ActorId, bool AlreadyDead = false) : GameEvent(TimeMs);

/// <summary>One field boss in the map's boss list: alive since <paramref name="AtMs"/>, or back at <paramref name="AtMs"/>.</summary>
/// <param name="SlotId">Map id × 100 + the boss's place in the list (the map's bosses sorted by NPC code).</param>
public sealed record FieldBossSlot(int SlotId, bool Alive, long AtMs, float X = 0, float Y = 0, float Z = 0);

/// <summary>
/// The field boss list of a map (<c>01 91</c>), sent while the in-game map shows it — the respawn timers straight from
/// the server. <paramref name="Count"/> is what the header announces; <paramref name="Slots"/> may stop short of it when
/// a slot could not be read (<see cref="Complete"/> is then false and <paramref name="Raw"/> helps work out why).
/// <paramref name="ServerId"/> is the local character's server (set by the tracker): every server runs its own timers.
/// </summary>
public sealed record FieldBossListEvent(long TimeMs, int MapId, int Count, IReadOnlyList<FieldBossSlot> Slots, byte[] Raw, int ServerId = 0)
    : GameEvent(TimeMs)
{
    public bool Complete => Slots.Count == Count;
}

/// <summary>
/// A skill use announcement (<c>02 38</c>). <paramref name="SkillCode"/> is the raw variant (e.g. 15280140): an effect
/// entity's own ticks carry its caster's variant with a different last digit, which is how it is tied to its owner.
/// </summary>
public sealed record CastEvent(long TimeMs, uint ActorId, uint TargetId, int SkillCode) : GameEvent(TimeMs);

/// <summary>
/// A hit landed (<c>03 38</c>): who hit what with which skill — no damage. On a world boss with a crowd it is all the
/// server sends about most players' hits, so it tells who is fighting even when their damage never arrives.
/// </summary>
public sealed record HitNoticeEvent(long TimeMs, uint ActorId, uint TargetId, int SkillCode) : GameEvent(TimeMs);

/// <summary>A monster's stagger gauge after a hit took stagger off it (<c>05 E0</c>): it counts down from <paramref name="Max"/>.</summary>
public sealed record StaggerGaugeEvent(long TimeMs, uint EntityId, uint Max, uint Rest, byte Status) : GameEvent(TimeMs);

/// <summary>
/// A buff or debuff put on (<c>2a 38</c>, <paramref name="IsAdd"/>) or renewed (<c>2b 38</c>) on an entity.
/// <paramref name="LengthMs"/> runs from the instance's start; -1 = never ends (a passive).
/// </summary>
public sealed record BuffAppliedEvent(long TimeMs, uint EntityId, int Instance, uint Abnormal, long LengthMs, uint Caster,
    uint Skill, bool Restarted, bool IsAdd) : GameEvent(TimeMs);

/// <summary>Buffs taken off an entity (<c>2c 38</c>), by instance.</summary>
public sealed record BuffRemovedEvent(long TimeMs, uint EntityId, int[] Instances) : GameEvent(TimeMs);

/// <summary>Every pet's level and soul points, sent at login (<c>00 90</c>). A pet missing from both lists is not collected yet.</summary>
public sealed record PetListEvent(long TimeMs, IReadOnlyList<(int Pet, int Level)> Levels, IReadOnlyList<(int Pet, int Points)> Points)
    : GameEvent(TimeMs);

/// <summary>Souls picked up, per pet (<c>0D 90</c>): a kill can feed several pets.</summary>
public sealed record PetSoulEvent(long TimeMs, IReadOnlyList<(int Pet, int Amount)> Souls) : GameEvent(TimeMs);

/// <summary>A pet reached a level (<c>14 90</c>).</summary>
public sealed record PetLevelEvent(long TimeMs, int Pet, int Level) : GameEvent(TimeMs);

/// <summary>The stagger gauge broke (<c>05 E0 … 00 03 02</c>).</summary>
public sealed record StaggerBreakEvent(long TimeMs, uint EntityId) : GameEvent(TimeMs);

/// <summary>
/// A value-less entry (switch 0) of a player's hit on a monster: the hit before it took stagger off the gauge
/// (A2Tools-DPS-Meter, <c>combat::stagger</c>).
/// </summary>
public sealed record StaggerMarkEvent(long TimeMs, uint ActorId, uint TargetId, int SkillCode) : GameEvent(TimeMs);

/// <summary>
/// A countdown of the instance the player is in (<c>00 61</c>), as the game shows it: <paramref name="Kind"/> 1 is the
/// 30 s wait after entering, 2 the dungeon's own time limit (60 min in Ferocious Horn Den); start and end are the
/// server's epoch ms. Sent on entry and again after every reload of the map (death, teleport) with the same times.
/// </summary>
public sealed record InstanceTimerEvent(long TimeMs, int MapId, int Kind, long StartMs, long EndMs) : GameEvent(TimeMs)
{
    /// <summary>Not the instance's own: a boss's time limit, from <see cref="BossTimeLimitEvent"/>.</summary>
    public const int BossLimit = 0x100;
}


/// <summary>
/// A boss with a time limit entered combat (<c>21 8D</c> with a deadline): the fight is lost at <paramref name="EndMs"/>
/// (server epoch ms; Ferocious Horn Nuakum: 5 minutes).
/// </summary>
public sealed record BossTimeLimitEvent(long TimeMs, uint ActorId, long EndMs) : GameEvent(TimeMs);

/// <summary>Server-side combat state toggle for an NPC (boss engaged / disengaged). KR only so far.</summary>
public sealed record BattleStateEvent(long TimeMs, uint ActorId, bool InCombat) : GameEvent(TimeMs);

/// <summary>A map load. <paramref name="IsTeleport"/> is true for a re-load inside the same map.</summary>
public sealed record ZoneChangedEvent(long TimeMs, int MapId, string ZoneName, bool IsDungeon, bool IsTeleport = false) : GameEvent(TimeMs);
