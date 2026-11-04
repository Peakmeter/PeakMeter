using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace PeakMeter;

/// <summary>
/// Legions on the website (profiles and legion pages). The game shows every player's legion above their head; the meter
/// reads it from the player records. Your own character's legion goes out with "Join the ranking"; the legions of the
/// players around you only with "Share legions of players nearby" (both off unless the player turns them on).
/// A player goes out again only when their legion changed or after 30 days, so the website's free database stays far
/// below its daily limit.
/// </summary>
public sealed class LegionShare : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly TimeSpan Gather = TimeSpan.FromMinutes(3); // at most 20 uploads an hour (the website takes 30)
    private static readonly TimeSpan Again = TimeSpan.FromDays(30);
    private const int MaxBatch = 100;
    private const int MaxRemembered = 20_000;

    private readonly Settings _settings;
    private readonly GameData _data;
    private readonly Action<string> _log;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, object>> _pending = new();
    private Dictionary<string, Sent> _sent = new();
    private DateTime _pausedUntil;
    private bool _loaded;

    private sealed record Sent(string Legion, DateTime At);

    private static string FilePath => Path.Combine(Settings.Dir, "legions-sent.json");

    public LegionShare(Settings settings, GameData data, Action<string> log)
    {
        _settings = settings;
        _data = data;
        _log = log;
        _timer = new Timer(_ => Send(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Add(PlayerLegionEvent e)
    {
        if (!(e.IsSelf ? _settings.RankingOptIn : _settings.RankingOptIn && _settings.LegionShareOthers)) return;
        if (e.Name.Length is 0 or > 24 || e.ServerId is < 1000 or >= 3000) return;
        var key = e.ServerId + "|" + e.Name.ToLowerInvariant();
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            Load();
            if (now < _pausedUntil) return;
            if (_sent.TryGetValue(key, out var s) && s.Legion == e.Legion && now - s.At < Again) return;
            if (_pending.ContainsKey(key)) return;
            var entry = new Dictionary<string, object>
            {
                ["name"] = e.Name,
                ["server"] = e.ServerId,
                ["serverName"] = _data.ServerName(e.ServerId),
                ["legion"] = e.Legion,
            };
            if (e.Class != GameClass.Unknown) entry["cls"] = e.Class.ToString().ToLowerInvariant();
            _pending[key] = entry;
            if (_pending.Count == 1) _timer.Change(Gather, Timeout.InfiniteTimeSpan);
        }
    }

    private void Send()
    {
        List<KeyValuePair<string, Dictionary<string, object>>> batch;
        lock (_gate)
        {
            if (!_settings.RankingOptIn) { _pending.Clear(); return; }
            batch = _pending.Take(MaxBatch).ToList();
            foreach (var kv in batch) _pending.Remove(kv.Key);
            if (_pending.Count > 0) _timer.Change(Gather, Timeout.InfiniteTimeSpan);
        }
        if (batch.Count == 0) return;
        var body = new Dictionary<string, object> { ["region"] = _settings.Region, ["players"] = batch.Select(kv => kv.Value).ToList() };
        _ = Task.Run(async () =>
        {
            try
            {
                using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var r = await Http.PostAsync($"{Ranking.Url}/api/legion", content);
                if ((int)r.StatusCode == 429)
                {
                    // The website's daily share is used up (try again tomorrow) or too many from here this hour.
                    var full = (await r.Content.ReadAsStringAsync()).Contains("\"full\":true");
                    lock (_gate) { _pausedUntil = full ? DateTime.UtcNow.Date.AddDays(1) : DateTime.UtcNow.AddHours(1); _pending.Clear(); }
                    _log(full ? "Legions: website full for today" : "Legions: paused for an hour");
                    return;
                }
                if (!r.IsSuccessStatusCode) { _log($"Legions not taken ({(int)r.StatusCode})"); return; }
                var now = DateTime.UtcNow;
                lock (_gate)
                {
                    foreach (var kv in batch) _sent[kv.Key] = new Sent((string)kv.Value["legion"], now);
                    Save();
                }
                _log($"Legions: {batch.Count} players");
            }
            catch { }
        });
    }

    private void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(FilePath))
                _sent = JsonSerializer.Deserialize<Dictionary<string, Sent>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { _sent = new(); }
    }

    private void Save()
    {
        try
        {
            var cutoff = DateTime.UtcNow - Again;
            var keep = _sent.Where(kv => kv.Value.At > cutoff).OrderByDescending(kv => kv.Value.At).Take(MaxRemembered);
            _sent = keep.ToDictionary(kv => kv.Key, kv => kv.Value);
            Directory.CreateDirectory(Settings.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_sent));
        }
        catch { }
    }

    public void Dispose() => _timer.Dispose();
}
