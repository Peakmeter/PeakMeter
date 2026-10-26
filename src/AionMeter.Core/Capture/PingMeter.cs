namespace AionMeter.Core.Capture;

/// <summary>
/// The game's ping, measured passively from the captured connection: the time between a packet the game sends and
/// the server's acknowledgement of it. Nothing is sent; it only watches the traffic that is there anyway.
/// </summary>
public sealed class PingMeter
{
    private readonly object _gate = new();
    private readonly Queue<(uint EndSeq, long Us)> _sent = new();
    private readonly Queue<(long Us, double Ms)> _samples = new();

    /// <summary>A segment the game sent to the server (payload only).</summary>
    public void Sent(uint seq, int length, long timeUs)
    {
        if (length <= 0) return;
        lock (_gate)
        {
            _sent.Enqueue((unchecked(seq + (uint)length), timeUs));
            while (_sent.Count > 64) _sent.Dequeue();
        }
    }

    /// <summary>A segment from the server acknowledging data up to <paramref name="ack"/>.</summary>
    public void Acked(uint ack, long timeUs)
    {
        lock (_gate)
        {
            long? first = null;
            while (_sent.Count > 0 && (int)(ack - _sent.Peek().EndSeq) >= 0)
            {
                var (_, us) = _sent.Dequeue();
                first ??= us;
            }
            if (first is not { } t) return;
            var ms = (timeUs - t) / 1000.0;
            if (ms is < 0 or > 5000) return;
            _samples.Enqueue((timeUs, ms));
            while (_samples.Count > 0 && timeUs - _samples.Peek().Us > 15_000_000) _samples.Dequeue();
            while (_samples.Count > 60) _samples.Dequeue();
        }
    }

    /// <summary>Median of the last 15 seconds; null when there is nothing recent to go by.</summary>
    public double? Ms
    {
        get
        {
            lock (_gate)
            {
                if (_samples.Count == 0) return null;
                var nowUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
                if (nowUs - _samples.Last().Us > 30_000_000) return null;
                var sorted = _samples.Select(s => s.Ms).OrderBy(x => x).ToList();
                return sorted[sorted.Count / 2];
            }
        }
    }
}
