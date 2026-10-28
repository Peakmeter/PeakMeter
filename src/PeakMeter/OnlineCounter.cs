using System.Net.Http;

namespace PeakMeter;

/// <summary>
/// Anonymous player count. While the meter is connected to a running game, it raises a public counter once per
/// 10-minute slot (and once per day, and once per install) on the free Abacus counter service
/// (abacus.jasoncameron.dev). Nothing else is sent: no name, character, server, id or version. Only the number goes
/// up, so the count of a slot is how many meters were in the game then. Off in the settings ("Anonym mitzählen").
/// </summary>
public sealed class OnlineCounter : IDisposable
{
    public const string Base = "https://abacus.jasoncameron.dev";
    public const string Namespace = "peakmeter-live";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Settings _settings;
    private readonly Func<bool> _inGame;
    private readonly Timer _timer;
    private string? _lastSlot;
    private int _busy;

    public OnlineCounter(Settings settings, Func<bool> inGame)
    {
        _settings = settings;
        _inGame = inGame;
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
    }

    /// <summary>The 10-minute slot a time falls in, as the counter's key (UTC): o-20261028-1520.</summary>
    public static string SlotKey(DateTime utc) => $"o-{utc:yyyyMMdd}-{utc:HH}{utc.Minute / 10}0";

    public static string DayKey(DateTime utc) => $"d-{utc:yyyyMMdd}";

    private void Tick()
    {
        if (!_settings.CountMe || !_inGame()) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var now = DateTime.UtcNow;
                var slot = SlotKey(now);
                if (slot != _lastSlot && await Hit(slot)) _lastSlot = slot;

                var day = DayKey(now);
                if (_settings.LastCountDay != day && await Hit(day))
                {
                    _settings.LastCountDay = day;
                    if (!_settings.CountedInstall && await Hit("installs")) _settings.CountedInstall = true;
                    _settings.Save();
                }
            }
            catch
            {
                // Offline, or the service is down: try again next minute.
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    private static async Task<bool> Hit(string key)
    {
        using var r = await Http.GetAsync($"{Base}/hit/{Namespace}/{key}");
        return r.IsSuccessStatusCode;
    }

    public void Dispose() => _timer.Dispose();
}
