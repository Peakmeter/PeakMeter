using System.Net.Http;
using System.Text;
using System.Text.Json;
using AionMeter.Core.Game;

namespace PeakMeter;

/// <summary>
/// Shares the world boss timers of the player's server with the PeakMeter website, so everyone on that server sees
/// live countdowns there. Only the boss, the server, alive or down, and when it comes back — no player, no character.
/// Follows "Count me anonymously"; off in the demo. Changes are gathered for a few seconds and sent together.
/// </summary>
public sealed class BossShare : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly TimeSpan Gather = TimeSpan.FromSeconds(8);

    private readonly Settings _settings;
    private readonly BossTimers _bosses;
    private readonly GameData _data;
    private readonly Action<string> _log;
    private readonly Func<bool> _paused;
    private readonly Timer _timer;
    private readonly Dictionary<(int Server, int Boss), string> _sent = new();
    private readonly object _gate = new();
    private int _busy;

    public BossShare(Settings settings, BossTimers bosses, GameData data, Action<string> log, Func<bool> paused)
    {
        _settings = settings;
        _bosses = bosses;
        _data = data;
        _log = log;
        _paused = paused;
        _timer = new Timer(_ => Send(), null, Timeout.Infinite, Timeout.Infinite);
        _bosses.Changed += () => _timer.Change(Gather, Timeout.InfiniteTimeSpan);
    }

    /// <summary>What is worth sending about one timer now, or null: a boss alive, or one coming back within a day.</summary>
    public static Dictionary<string, object>? Report(BossTimer t, DateTimeOffset now)
    {
        if (t.NpcCode <= 0) return null;
        if (t.AliveNow && t.AliveAt is { } alive && now - alive < TimeSpan.FromHours(3))
        {
            var r = new Dictionary<string, object> { ["boss"] = t.NpcCode, ["state"] = "up", ["src"] = t.FromGame ? "game" : "seen" };
            if (t.AliveSince is { } since && now - since < TimeSpan.FromHours(24)) r["since"] = since.ToUnixTimeMilliseconds();
            return r;
        }
        if (t.NextSpawn is { } next && next > now - TimeSpan.FromHours(2) && next < now + TimeSpan.FromHours(24))
            return new Dictionary<string, object>
            {
                ["boss"] = t.NpcCode, ["state"] = "down", ["next"] = next.ToUnixTimeMilliseconds(), ["src"] = t.FromGame ? "game" : "kill",
            };
        return null;
    }

    private void Send()
    {
        if (!_settings.CountMe || _paused()) return;
        var server = _bosses.CurrentServer;
        if (server <= 0) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) { _timer.Change(Gather, Timeout.InfiniteTimeSpan); return; }
        var now = DateTimeOffset.UtcNow;
        var reports = new List<Dictionary<string, object>>();
        lock (_gate)
        {
            foreach (var t in _bosses.ForServer(server))
            {
                if (Report(t, now) is not { } r) continue;
                // Only what changed since the last send (the server keeps the rest).
                var sig = $"{r["state"]}|{r.GetValueOrDefault("next")}|{r.GetValueOrDefault("since")}|{r["src"]}";
                if (_sent.TryGetValue((server, t.NpcCode), out var old) && old == sig) continue;
                _sent[(server, t.NpcCode)] = sig;
                reports.Add(r);
            }
        }
        if (reports.Count == 0) { Interlocked.Exchange(ref _busy, 0); return; }
        var body = new Dictionary<string, object>
        {
            ["region"] = _settings.Region, ["server"] = server, ["serverName"] = _data.ServerName(server), ["reports"] = reports.Take(80).ToList(),
        };
        _ = Task.Run(async () =>
        {
            try
            {
                using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var r = await Http.PostAsync($"{Ranking.Url}/api/bosses/report", content);
                _log(r.IsSuccessStatusCode ? $"World bosses shared: {reports.Count}" : $"World bosses not taken ({(int)r.StatusCode})");
                if (!r.IsSuccessStatusCode) lock (_gate) _sent.Clear(); // try again with the next change
            }
            catch
            {
                lock (_gate) _sent.Clear();
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    public void Dispose() => _timer.Dispose();
}
