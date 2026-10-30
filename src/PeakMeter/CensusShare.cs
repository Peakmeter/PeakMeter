using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace PeakMeter;

/// <summary>
/// The census on the website: which classes play on which server, with what combat power. After each fight the meter
/// sends the players it saw — never a name: only a one-way fingerprint (so nobody counts twice), class, server, combat
/// power and gear score. Follows "Count me anonymously"; off in the demo. Each player goes out at most once a day.
/// </summary>
public sealed class CensusShare : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly TimeSpan Gather = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Again = TimeSpan.FromHours(24);

    private readonly Settings _settings;
    private readonly GameData _data;
    private readonly Action<string> _log;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, object>> _pending = new();
    private readonly Dictionary<string, DateTime> _sent = new();

    public CensusShare(Settings settings, GameData data, Action<string> log)
    {
        _settings = settings;
        _data = data;
        _log = log;
        _timer = new Timer(_ => Send(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>A finished fight: note everyone with a known class and server, and send them a little later.</summary>
    public void Add(FightRecord record)
    {
        if (!_settings.CountMe) return;
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            foreach (var c in record.Summary.Combatants)
            {
                if (Entry(c) is not { } e) continue;
                var key = (string)e["k"];
                if (_sent.TryGetValue(key, out var at) && now - at < Again) continue;
                _pending[key] = e;
            }
            if (_pending.Count > 0) _timer.Change(Gather, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>What goes out about one player, or null when there is too little to count (no class, no server).</summary>
    public Dictionary<string, object>? Entry(CombatantSnapshot c)
    {
        if (c.IsUnknownSummons || c.Class == GameClass.Unknown || c.ServerId <= 0 || c.Name.Length is 0 or > 24) return null;
        return new Dictionary<string, object>
        {
            ["k"] = Fingerprint(c.Name, c.ServerId),
            ["cls"] = c.Class.ToString().ToLowerInvariant(),
            ["server"] = c.ServerId,
            ["serverName"] = _data.ServerName(c.ServerId),
            ["cp"] = c.CombatPower,
            ["gs"] = c.GearScore,
        };
    }

    /// <summary>One-way: the same character always gives the same 16 characters, and they cannot be turned back into a name.</summary>
    public static string Fingerprint(string name, int server) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("peakmeter-census|" + name.ToLowerInvariant() + "|" + server)))[..16].ToLowerInvariant();

    private void Send()
    {
        List<Dictionary<string, object>> batch;
        lock (_gate)
        {
            if (!_settings.CountMe) { _pending.Clear(); return; }
            batch = _pending.Values.Take(50).ToList();
            foreach (var e in batch) _pending.Remove((string)e["k"]);
            if (_pending.Count > 0) _timer.Change(Gather, Timeout.InfiniteTimeSpan);
        }
        if (batch.Count == 0) return;
        var body = new Dictionary<string, object> { ["region"] = _settings.Region, ["players"] = batch };
        _ = Task.Run(async () =>
        {
            try
            {
                using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var r = await Http.PostAsync($"{Ranking.Url}/api/census", content);
                if (!r.IsSuccessStatusCode) { _log($"Census not taken ({(int)r.StatusCode})"); return; }
                var now = DateTime.UtcNow;
                lock (_gate) foreach (var e in batch) _sent[(string)e["k"]] = now;
                _log($"Census: {batch.Count} players");
            }
            catch { }
        });
    }

    public void Dispose() => _timer.Dispose();
}
