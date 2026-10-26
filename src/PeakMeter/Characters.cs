using System.IO;
using System.Text.Json;
using AionMeter.Core.Events;

namespace PeakMeter;

/// <summary>One of your characters, as PeakMeter last saw it.</summary>
public sealed class CharacterInfo
{
    public string Name { get; set; } = "";
    public int ServerId { get; set; }
    public GameClass Class { get; set; }
    public DateTimeOffset LastSeen { get; set; }

    /// <summary>Odyle energy when the game last told it: regenerating pool and bonus from items.</summary>
    public int OdylePool { get; set; } = -1;
    public int OdyleBonus { get; set; }
    public DateTimeOffset? OdyleAt { get; set; }

    /// <summary>Ticket counts (nightmare, ascension, daily, shugokey, invasionkey) and when they were last known.</summary>
    public Dictionary<string, int> Tickets { get; set; } = new();
    public DateTimeOffset? TicketsAt { get; set; }

    public string Key => $"{ServerId}|{Name}";
}

/// <summary>
/// Your characters across sessions (%AppData%\PeakMeter\characters.json): who you played when, and each one's Odyle
/// energy, so the tickets window can estimate what regenerated since — like the game does while you are offline.
/// </summary>
public sealed class Characters
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(Settings.Dir, "characters.json");
    private readonly object _gate = new();
    private Dictionary<string, CharacterInfo> _all = new();

    public Characters()
    {
        try
        {
            if (File.Exists(_path))
                _all = (JsonSerializer.Deserialize<List<CharacterInfo>>(File.ReadAllText(_path), Json) ?? [])
                    .Where(c => c.Name.Length > 0).GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First());
            // v1.24.0 could take a stranger for you (a misread owner record): the same name then turned up on
            // server after server. Nobody has one name as their own character on three servers – drop those.
            var bogus = _all.Values.GroupBy(c => c.Name).Where(g => g.Select(c => c.ServerId).Distinct().Count() >= 3).Select(g => g.Key).ToHashSet();
            if (bogus.Count > 0)
            {
                foreach (var key in _all.Where(kv => bogus.Contains(kv.Value.Name)).Select(kv => kv.Key).ToList()) _all.Remove(key);
                Save();
            }
        }
        catch { }
    }

    public event Action? Changed;

    public IReadOnlyList<CharacterInfo> All()
    {
        lock (_gate) return _all.Values.OrderByDescending(c => c.LastSeen).ToList();
    }

    public CharacterInfo? Get(string name, int server)
    {
        lock (_gate) return _all.GetValueOrDefault($"{server}|{name}");
    }

    /// <summary>The character you are playing right now (called every few seconds while logged in).</summary>
    public void Seen(string name, int server, GameClass cls)
    {
        if (name.Length == 0) return;
        lock (_gate)
        {
            if (!_all.TryGetValue($"{server}|{name}", out var c))
                _all[$"{server}|{name}"] = c = new CharacterInfo { Name = name, ServerId = server };
            if (cls != GameClass.Unknown) c.Class = cls;
            c.LastSeen = DateTimeOffset.Now;
        }
        SaveSoon();
    }

    public void Odyle(string name, int server, int pool, int bonus)
    {
        if (name.Length == 0) return;
        lock (_gate)
        {
            if (!_all.TryGetValue($"{server}|{name}", out var c))
                _all[$"{server}|{name}"] = c = new CharacterInfo { Name = name, ServerId = server };
            c.OdylePool = pool;
            c.OdyleBonus = bonus;
            c.OdyleAt = DateTimeOffset.Now;
            c.LastSeen = DateTimeOffset.Now;
        }
        Save();
    }

    public void SetTickets(string name, int server, Dictionary<string, int> tickets)
    {
        if (name.Length == 0) return;
        lock (_gate)
        {
            if (!_all.TryGetValue($"{server}|{name}", out var c))
                _all[$"{server}|{name}"] = c = new CharacterInfo { Name = name, ServerId = server };
            c.Tickets = new Dictionary<string, int>(tickets);
            c.TicketsAt = DateTimeOffset.Now;
            c.LastSeen = DateTimeOffset.Now;
        }
        Save();
    }

    public void Remove(string key)
    {
        lock (_gate) _all.Remove(key);
        Save();
    }

    private long _lastSave;

    private void SaveSoon()
    {
        if (Environment.TickCount64 - _lastSave < 60_000) return;
        Save();
    }

    public void Save()
    {
        _lastSave = Environment.TickCount64;
        try
        {
            string text;
            lock (_gate) text = JsonSerializer.Serialize(_all.Values.ToList(), Json);
            Directory.CreateDirectory(Settings.Dir);
            File.WriteAllText(_path, text);
        }
        catch { }
        Changed?.Invoke();
    }

    /// <summary>
    /// Odyle now, estimated from the last known value: +regen every 3 hours since then, up to the cap
    /// (energy keeps regenerating while you are offline). Returns the total and how much of it is estimated.
    /// </summary>
    public static (int Total, int Gained, bool Full) Estimate(CharacterInfo c, int cap, int regen, DateTimeOffset now)
    {
        if (c.OdylePool < 0 || c.OdyleAt is not { } at) return (-1, 0, false);
        var ticks = now > at ? (now - at).Ticks / AionMeterTicks.Three : 0;
        var pool = c.OdylePool >= cap ? c.OdylePool : (int)Math.Min(cap, c.OdylePool + ticks * regen);
        return (pool + c.OdyleBonus, pool - c.OdylePool, pool >= cap);
    }

    private static class AionMeterTicks
    {
        public static readonly long Three = TimeSpan.FromHours(3).Ticks;
    }
}
