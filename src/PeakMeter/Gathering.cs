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

    /// <summary>Names of items seen so far (the game sends only codes). More are learned as they are recorded.</summary>
    private static readonly Dictionary<int, (string De, string En)> KnownNames = new()
    {
        [610610013] = ("Od", "Od"),
        [610680045] = ("Mela", "Mela"),
    };

    public sealed class Day
    {
        public Dictionary<int, int> Items { get; set; } = new();
        public int Gathers { get; set; }
    }

    private readonly object _gate = new();
    private readonly Settings _settings;
    private readonly Action<string> _log;
    private Dictionary<string, Day> _days = new();
    private readonly Dictionary<int, (long Count, long AtMs, long Delta)> _stacks = new();
    private readonly Dictionary<int, int> _unsent = new();
    private int _unsentGathers;
    private long _lastUpload;

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

    public static string NameOf(int item) =>
        KnownNames.TryGetValue(item, out var n) ? L.S(n.De, n.En) : L.S($"Gegenstand {item}", $"Item {item}");

    private static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    public Day TodayStats
    {
        get { lock (_gate) return _days.TryGetValue(Today, out var d) ? d : new Day(); }
    }

    /// <summary>The inventory's new stack of an item. Comes just before the gather that changed it.</summary>
    public void NoteStack(int item, long count, long timeMs)
    {
        lock (_gate)
        {
            var delta = _stacks.TryGetValue(item, out var old) && count > old.Count ? count - old.Count : 0;
            _stacks[item] = (count, timeMs, delta);
        }
    }

    /// <summary>A finished gather of an item.</summary>
    public void Add(int item, long timeMs)
    {
        int qty, total;
        lock (_gate)
        {
            // The stack change of the same moment tells the amount; without one (first gather of an item since start) it is 1.
            qty = _stacks.TryGetValue(item, out var s) && Math.Abs(timeMs - s.AtMs) < 2_000 && s.Delta is > 0 and <= 50 ? (int)s.Delta : 1;
            var key = Today;
            if (!_days.TryGetValue(key, out var day)) _days[key] = day = new Day();
            day.Items[item] = day.Items.GetValueOrDefault(item) + qty;
            day.Gathers++;
            total = day.Items[item];
            _unsent[item] = _unsent.GetValueOrDefault(item) + qty;
            _unsentGathers++;
            // Keep 60 days.
            foreach (var old in _days.Keys.Where(k => string.CompareOrdinal(k, DateTime.Now.AddDays(-60).ToString("yyyy-MM-dd")) < 0).ToList()) _days.Remove(old);
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_days)); } catch { }
        }
        _log($"Gathered {item} x{qty} (today {total})");
        Gathered?.Invoke(item, qty, total);
        if (Environment.TickCount64 - _lastUpload > 10 * 60_000) Upload();
    }

    /// <summary>What was gathered since the last upload, to the website's gathering stats (only with "Count me anonymously").</summary>
    public void Upload()
    {
        Dictionary<int, int> items;
        int gathers;
        lock (_gate)
        {
            if (!_settings.CountMe || _unsent.Count == 0) return;
            items = new(_unsent);
            gathers = _unsentGathers;
            _unsent.Clear();
            _unsentGathers = 0;
            _lastUpload = Environment.TickCount64;
        }
        var body = new Dictionary<string, object>
        {
            ["region"] = _settings.Region,
            ["gathers"] = gathers,
            ["items"] = items.Select(kv => new Dictionary<string, object?> { ["item"] = kv.Key, ["n"] = kv.Value, ["name"] = KnownNames.TryGetValue(kv.Key, out var nm) ? nm.De : null }).ToList(),
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
