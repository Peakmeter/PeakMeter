using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PeakMeter;

/// <summary>
/// Gathering, counted from the game: a finished gather names the item (0x338D) and the inventory reports the item's
/// new stack (0x1B56) in the same moment, so the difference is how many came out ("Od x2"). Kept per day and item in
/// %AppData%\PeakMeter\gathering.json; with "Count me anonymously" the day's totals also go to the website (item and
/// count only).
/// </summary>
public sealed class Gathering
{
    public static string FilePath => Path.Combine(Settings.Dir, "gathering.json");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Item names (data/items.json: English from the AION 2 Hub list, German for gathering materials).</summary>
    private static Dictionary<int, (string? De, string En)> _names = new();

    public static void LoadNames(string dataDir)
    {
        try
        {
            var path = Path.Combine(dataDir, "items.json");
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var map = new Dictionary<int, (string?, string)>();
            foreach (var p in doc.RootElement.EnumerateObject())
                if (int.TryParse(p.Name, out var id) && p.Value.TryGetProperty("en", out var en))
                    map[id] = (p.Value.TryGetProperty("de", out var de) ? de.GetString() : null, en.GetString() ?? "");
            _names = map;
        }
        catch { }
    }

    public sealed class Day
    {
        public Dictionary<int, int> Items { get; set; } = new();
        public int Gathers { get; set; }
        public Dictionary<int, int> Crafted { get; set; } = new();
        public int Crafts { get; set; }
    }

    private readonly object _gate = new();
    private readonly Settings _settings;
    private readonly Action<string> _log;
    private Dictionary<string, Day> _days = new();
    private readonly Dictionary<int, (long Count, long AtMs, long Delta)> _stacks = new();
    private readonly Dictionary<int, int> _unsent = new();
    private int _unsentGathers;
    private readonly Dictionary<int, int> _unsentCrafted = new();
    private long _craftAt = long.MinValue;
    private long _lastUpload;
    // Gathering materials that just came into the inventory (item, how many, when): what a gather really gave.
    private readonly List<(int Item, int Delta, long AtMs)> _incoming = new();
    // Gathers waiting for their inventory changes: settled once the game's clock is a second past them (or after a
    // second of real time, when nothing else comes).
    private readonly List<(int Spot, long AtMs, long Wall)> _pending = new();
    private Timer? _settle;

    /// <summary>Gathering materials (ore, gems, wood, herbs, food …): 6106xxxxx. A gather names its spot's base item, but
    /// a lucky one gives a finer grade ("Feines Oreichalkos-Erz"), which only the inventory shows.</summary>
    private static bool IsGatherMaterial(int item) => item is >= 610_600_000 and < 610_700_000;

    public Gathering(Settings settings, Action<string> log)
    {
        _settings = settings;
        _log = log;
        try
        {
            if (File.Exists(FilePath)) _days = JsonSerializer.Deserialize<Dictionary<string, Day>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
    }

    /// <summary>Raised after a gather: item, how many came out, today's total of that item.</summary>
    public event Action<int, int, int>? Gathered;

    /// <summary>Raised after a craft: the item made (0 when none came out), how many, today's crafts.</summary>
    public event Action<int, int, int>? Crafted;

    /// <summary>Raised when a craft reaches a level.</summary>
    public event Action<int>? CraftLevelUp;

    public static string NameOf(int item) =>
        _names.TryGetValue(item, out var n) ? (L.Lang == "de" && n.De is { } de ? de : n.En) : L.S($"Gegenstand {item}", $"Item {item}");

    private static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    public Day TodayStats
    {
        get { lock (_gate) return _days.TryGetValue(Today, out var d) ? d : new Day(); }
    }

    /// <summary>The inventory's new stack of an item. Comes just before the gather that changed it.</summary>
    public void NoteStack(int item, long count, long timeMs, bool isNew = false)
    {
        Settle(timeMs);
        lock (_gate)
        {
            // Unknown before (first time seen since start, not a new stack): +1 for a gathering material, else nothing.
            var known = _stacks.TryGetValue(item, out var old);
            var delta = isNew ? count : known ? (count > old.Count ? count - old.Count : 0) : IsGatherMaterial(item) && count > 0 ? 1 : 0;
            _stacks[item] = (count, timeMs, delta);
            if (delta is > 0 and <= 50 && IsGatherMaterial(item) && !(timeMs - _craftAt is >= 0 and < 2_000))
            {
                _incoming.RemoveAll(x => timeMs - x.AtMs > 60_000);
                _incoming.Add((item, (int)delta, timeMs));
            }
            // The item a craft made arrives right after the craft itself.
            if (delta is > 0 and <= 100 && timeMs - _craftAt is >= 0 and < 2_000)
            {
                _craftAt = long.MinValue;
                var day = TodayDay();
                day.Crafted[item] = day.Crafted.GetValueOrDefault(item) + (int)delta;
                _unsentCrafted[item] = _unsentCrafted.GetValueOrDefault(item) + (int)delta;
                Save();
                var crafts = day.Crafts;
                ThreadPool.QueueUserWorkItem(_ => Crafted?.Invoke(item, (int)delta, crafts));
            }
        }
    }

    private Day TodayDay()
    {
        if (!_days.TryGetValue(Today, out var day)) _days[Today] = day = new Day();
        return day;
    }

    private void Save()
    {
        foreach (var old in _days.Keys.Where(k => string.CompareOrdinal(k, DateTime.Now.AddDays(-60).ToString("yyyy-MM-dd")) < 0).ToList()) _days.Remove(old);
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_days)); } catch { }
    }

    /// <summary>A finished craft. The item it made follows in the inventory; a craft without one (failed) counts too.</summary>
    public void AddCraft(long timeMs)
    {
        lock (_gate)
        {
            TodayDay().Crafts++;
            _craftAt = timeMs;
            Save();
        }
        _log("Craft finished");
        if (Environment.TickCount64 - _lastUpload > 10 * 60_000) Upload();
    }

    public void LevelUp(int level)
    {
        _log($"Craft level {level}");
        CraftLevelUp?.Invoke(level);
    }

    /// <summary>A finished gather at a spot of <paramref name="spotItem"/>. What came out is read from the inventory a
    /// moment later: the materials that went up around the same time (a finer grade, two at once …); if none showed,
    /// one of the spot's item.</summary>
    public void Add(int spotItem, long timeMs)
    {
        Settle(timeMs);
        lock (_gate)
        {
            _pending.Add((spotItem, timeMs, Environment.TickCount64));
            _settle ??= new Timer(_ => Settle(long.MaxValue, byWall: true), null, 1_000, 1_000);
        }
    }

    /// <summary>Settles the gathers the game's clock (<paramref name="nowMs"/>) or the real clock has left behind.</summary>
    public void Settle(long nowMs, bool byWall = false)
    {
        List<(int Spot, long AtMs, long Wall)> due;
        lock (_gate)
        {
            var wall = Environment.TickCount64;
            due = _pending.Where(p => byWall ? wall - p.Wall >= 1_000 : nowMs - p.AtMs >= 1_000).OrderBy(p => p.AtMs).ToList();
            foreach (var p in due) _pending.Remove(p);
        }
        foreach (var p in due) Resolve(p.Spot, p.AtMs);
    }

    private void Resolve(int spotItem, long timeMs)
    {
        var got = new List<(int Item, int Qty, int Total)>();
        lock (_gate)
        {
            var mine = _incoming.Where(x => Math.Abs(x.AtMs - timeMs) <= 2_000).ToList();
            foreach (var x in mine) _incoming.Remove(x);
            var yields = mine.Count > 0 ? mine.Select(x => (x.Item, x.Delta)).ToList() : [(spotItem, 1)];
            var day = TodayDay();
            day.Gathers++;
            _unsentGathers++;
            foreach (var (item, qty) in yields)
            {
                day.Items[item] = day.Items.GetValueOrDefault(item) + qty;
                _unsent[item] = _unsent.GetValueOrDefault(item) + qty;
                got.Add((item, qty, day.Items[item]));
            }
            Save();
        }
        foreach (var (item, qty, total) in got)
        {
            _log($"Gathered {item} x{qty} (today {total})");
            Gathered?.Invoke(item, qty, total);
        }
        if (Environment.TickCount64 - _lastUpload > 10 * 60_000) Upload();
    }

    /// <summary>What was gathered since the last upload, to the website's gathering stats (only with "Count me anonymously").</summary>
    public void Upload()
    {
        Dictionary<int, int> items, crafted;
        int gathers;
        lock (_gate)
        {
            if (!_settings.CountMe || (_unsent.Count == 0 && _unsentCrafted.Count == 0)) return;
            items = new(_unsent);
            crafted = new(_unsentCrafted);
            _unsentCrafted.Clear();
            gathers = _unsentGathers;
            _unsent.Clear();
            _unsentGathers = 0;
            _lastUpload = Environment.TickCount64;
        }
        var body = new Dictionary<string, object>
        {
            ["region"] = _settings.Region,
            ["gathers"] = gathers,
            ["crafted"] = crafted.Select(kv => new Dictionary<string, object?> { ["item"] = kv.Key, ["n"] = kv.Value, ["name"] = _names.TryGetValue(kv.Key, out var nm) ? nm.En : null }).ToList(),
            ["items"] = items.Select(kv => new Dictionary<string, object?> { ["item"] = kv.Key, ["n"] = kv.Value, ["name"] = _names.TryGetValue(kv.Key, out var nm) ? nm.En : null }).ToList(),
        };
        _ = Task.Run(async () =>
        {
            try
            {
                using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var r = await Http.PostAsync($"{Ranking.Url}/api/gather", content);
                if (!r.IsSuccessStatusCode) _log($"Gathering stats not taken ({(int)r.StatusCode})");
            }
            catch { }
        });
    }
}
