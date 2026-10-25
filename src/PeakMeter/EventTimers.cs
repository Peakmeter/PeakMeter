using System.IO;
using System.Media;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeakMeter;

/// <summary>One scheduled event, as written in timers.json.</summary>
public sealed class EventDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Every hour at this minute (e.g. 0 for :00). Used when <see cref="Times"/> is empty.</summary>
    public int? HourlyAt { get; set; }
    /// <summary>Fixed start times, "HH:mm".</summary>
    public List<string> Times { get; set; } = [];
    /// <summary>Mon … Sun; empty = every day.</summary>
    public List<string> Days { get; set; } = [];
    public int DurationMin { get; set; } = 10;
    /// <summary>Overrides the file's time zone (resets run on UTC).</summary>
    public string? TimeZone { get; set; }
    public bool Alert { get; set; }
    /// <summary>Minutes before the start the chime plays; null = the file's default.</summary>
    public int? LeadMin { get; set; }
}

public sealed class TimersFile
{
    public int Version { get; set; }
    public string Info { get; set; } = "";
    public string TimeZone { get; set; } = "Europe/Berlin";
    public int LeadMin { get; set; } = 1;
    public List<EventDef> Events { get; set; } = [];
}

/// <summary>Next / running occurrence of an event, in UTC.</summary>
public sealed class EventState(EventDef def)
{
    public EventDef Def { get; } = def;
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public DateTimeOffset AlertedFor { get; set; }
    public bool Running(DateTimeOffset now) => now >= Start && now < End;
}

/// <summary>
/// Scheduled game events (Shugo Festival, rifts, sieges, resets …) read from %AppData%\PeakMeter\timers.json, so
/// times can be fixed by hand when a patch moves them. Each event's next start is cached; nothing is computed
/// again until it has passed.
/// </summary>
public sealed class EventTimers
{
    public static string FilePath => Path.Combine(Settings.Dir, "timers.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private TimersFile _file = Defaults();
    private DateTime _loadedStamp;

    public IReadOnlyList<EventState> Events { get; private set; } = [];
    public string? Error { get; private set; }

    /// <summary>Raised once per occurrence when an event with the bell on is about to start.</summary>
    public event Action<EventState, int>? Alert;

    public EventTimers() => Load();

    public void Load()
    {
        Error = null;
        try
        {
            if (!File.Exists(FilePath))
            {
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Defaults(), Json));
            }
            _file = JsonSerializer.Deserialize<TimersFile>(File.ReadAllText(FilePath), Json) ?? Defaults();
            if (Upgrade(_file)) File.WriteAllText(FilePath, JsonSerializer.Serialize(_file, Json));
            _loadedStamp = File.GetLastWriteTimeUtc(FilePath);
        }
        catch (Exception ex)
        {
            Error = L.S("timers.json fehlerhaft: ", "timers.json has an error: ") + ex.Message;
            _file = Defaults();
        }
        var now = DateTimeOffset.UtcNow;
        Events = _file.Events.Select(d => new EventState(d) { AlertedFor = DateTimeOffset.MinValue }).ToList();
        foreach (var e in Events) Advance(e, now);
    }

    private const int CurrentVersion = 2;

    /// <summary>Brings an older timers.json up to date without touching the user's own changes: new events are added,
    /// v2 names the half-hour invasion after the Beritra air raid. True when the file changed.</summary>
    private static bool Upgrade(TimersFile f)
    {
        if (f.Version >= CurrentVersion) return false;
        var defaults = Defaults();
        if (f.Version < 2 && f.Events.FirstOrDefault(e => e.Id == "invasion") is { } inv && inv.Name == "Dimensionsinvasion")
        {
            var d = defaults.Events.First(e => e.Id == "invasion");
            inv.Name = d.Name;
            inv.DurationMin = d.DurationMin;
            inv.Alert = true;
        }
        foreach (var d in defaults.Events.Where(d => f.Events.All(e => e.Id != d.Id))) f.Events.Add(d);
        f.Info = defaults.Info;
        f.Version = CurrentVersion;
        return true;
    }

    /// <summary>EU (Berlin time) or NA (New York time): all events follow the server's clock.</summary>
    public void SetRegion(string region)
    {
        var tz = region == "na" ? "America/New_York" : "Europe/Berlin";
        if (_file.TimeZone == tz) return;
        _file.TimeZone = tz;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_file, Json));
            Load();
        }
        catch { }
    }

    /// <summary>Minutes before an event that the chime plays (default for all events).</summary>
    public int LeadMinutes
    {
        get => _file.LeadMin;
        set
        {
            _file.LeadMin = Math.Clamp(value, 0, 30);
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_file, Json)); _loadedStamp = File.GetLastWriteTimeUtc(FilePath); } catch { }
        }
    }

    /// <summary>Bell on/off from the timer window, written back to the file.</summary>
    public void SetAlert(EventState e, bool on)
    {
        e.Def.Alert = on;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_file, Json));
            _loadedStamp = File.GetLastWriteTimeUtc(FilePath);
        }
        catch { }
    }

    /// <summary>Once a second: roll finished events forward, fire alerts, pick up hand edits of the file.</summary>
    public void Tick(DateTimeOffset now)
    {
        try
        {
            if (File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != _loadedStamp) Load();
        }
        catch { }

        foreach (var e in Events)
        {
            if (now >= e.End) Advance(e, now);
            if (!e.Def.Alert || e.AlertedFor == e.Start) continue;
            var lead = TimeSpan.FromMinutes(e.Def.LeadMin ?? _file.LeadMin);
            if (now >= e.Start - lead && now < e.Start + TimeSpan.FromSeconds(30))
            {
                e.AlertedFor = e.Start;
                Alert?.Invoke(e, (int)Math.Ceiling((e.Start - now).TotalMinutes));
            }
        }
    }

    /// <summary>Events ordered by what comes next: running ones first, then by start.</summary>
    public IEnumerable<EventState> Upcoming(DateTimeOffset now) =>
        Events.OrderBy(e => e.Running(now) ? 0 : 1).ThenBy(e => e.Running(now) ? e.End : e.Start);

    private void Advance(EventState e, DateTimeOffset now)
    {
        var tz = Zone(e.Def.TimeZone ?? _file.TimeZone);
        var local = TimeZoneInfo.ConvertTime(now, tz).Date;
        var duration = TimeSpan.FromMinutes(Math.Max(1, e.Def.DurationMin));
        var times = e.Def.Times.Count > 0
            ? e.Def.Times.Select(t => TimeSpan.TryParse(t, out var ts) ? ts : (TimeSpan?)null).OfType<TimeSpan>().ToList()
            : e.Def.HourlyAt is { } m ? Enumerable.Range(0, 24).Select(h => new TimeSpan(h, Math.Clamp(m, 0, 59), 0)).ToList()
            : [];
        var days = e.Def.Days.Select(ParseDay).OfType<DayOfWeek>().ToHashSet();

        for (var d = -1; d <= 8; d++)
        {
            var day = local.AddDays(d);
            if (days.Count > 0 && !days.Contains(day.DayOfWeek)) continue;
            foreach (var t in times)
            {
                var start = day + t;
                if (tz.IsInvalidTime(start)) continue;
                var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, tz), TimeSpan.Zero);
                if (startUtc + duration <= now) continue;
                e.Start = startUtc;
                e.End = startUtc + duration;
                return;
            }
        }
        e.Start = e.End = DateTimeOffset.MaxValue; // nothing scheduled
    }

    private static DayOfWeek? ParseDay(string s) => s.Trim().ToLowerInvariant()[..Math.Min(2, s.Trim().Length)] switch
    {
        "mo" => DayOfWeek.Monday,
        "tu" or "di" => DayOfWeek.Tuesday,
        "we" or "mi" => DayOfWeek.Wednesday,
        "th" or "do" => DayOfWeek.Thursday,
        "fr" => DayOfWeek.Friday,
        "sa" => DayOfWeek.Saturday,
        "su" or "so" => DayOfWeek.Sunday,
        _ => null,
    };

    private static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { }
        // Older Windows without ICU only knows Windows ids ("W. Europe Standard Time").
        try
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win)) return TimeZoneInfo.FindSystemTimeZoneById(win);
        }
        catch { }
        return TimeZoneInfo.Local;
    }

    /// <summary>EU schedule as reported by the community (shugo.gg, 2026-10). NCSOFT publishes no official times.</summary>
    private static TimersFile Defaults() => new()
    {
        Version = CurrentVersion,
        Info = "PeakMeter Event-Timer. Zeiten in der Zeitzone 'timeZone' (EU: Europe/Berlin, NA: America/New_York). " +
               "hourlyAt = jede Stunde zu dieser Minute; times = feste Uhrzeiten; days = Mon..Sun (leer = täglich); " +
               "alert = Klingelton; leadMin = Minuten vorher. Datei speichern, PeakMeter übernimmt es sofort.",
        TimeZone = "Europe/Berlin",
        LeadMin = 1,
        Events =
        [
            new() { Id = "shugo", Name = L.S("Shugo-Festival", "Shugo Festival"), HourlyAt = 0, DurationMin = 10, Alert = true },
            // One of three invasions picked each half hour: Beritra air raid, Accursed Sword, Incursion of the Spirits.
            new() { Id = "invasion", Name = L.S("Luftangriff / Dimensionsinvasion", "Air raid / Dimensional invasion"), HourlyAt = 30, DurationMin = 10, Alert = true },
            new() { Id = "rift", Name = L.S("Raumzeit-Riss", "Spacetime Rift"), Times = ["02:00", "05:00", "08:00", "11:00", "14:00", "17:00", "20:00", "23:00"], DurationMin = 10, Alert = true, LeadMin = 2 },
            new() { Id = "kaira", Name = L.S("Wächterin Kaira", "Watcher Kaira"), Times = ["01:00", "04:00", "07:00", "10:00", "13:00", "16:00", "19:00", "22:00"], DurationMin = 30 },
            new() { Id = "battlefield", Name = L.S("Schlachtfeld", "Battlefield"), Times = ["11:00", "19:00"], DurationMin = 120 },
            new() { Id = "siege", Name = L.S("Artefakt-Belagerung", "Artifact Siege"), Days = ["Mon", "Thu", "Sat"], Times = ["21:00"], DurationMin = 30, Alert = true, LeadMin = 5 },
            new() { Id = "siegebosses", Name = L.S("Belagerungs-Bosse", "Siege bosses"), Days = ["Mon", "Thu", "Sat"], Times = ["21:30"], DurationMin = 30 },
            new() { Id = "nahma", Name = L.S("Wächterfürst Nahma", "Guardian Lord Nahma"), Days = ["Fri", "Sun"], Times = ["21:00"], DurationMin = 30, Alert = true, LeadMin = 5 },
            new() { Id = "daily", Name = L.S("Täglicher Reset", "Daily reset"), Times = ["07:00"], TimeZone = "UTC", DurationMin = 1 },
            new() { Id = "weekly", Name = L.S("Wöchentlicher Reset", "Weekly reset"), Days = ["Wed"], Times = ["07:00"], TimeZone = "UTC", DurationMin = 1 },
        ],
    };
}

/// <summary>
/// A soft two-note bell, synthesised once: pure sine tones with a quick fade-in and a long, gentle fade-out, no
/// sharp attack and nothing above ~2 kHz. Its loudest point is capped at a third of full scale even at 100 %.
/// </summary>
public static class Chime
{
    private static SoundPlayer? _player;
    private static int _builtFor = -1;

    public static void Play(int volumePercent)
    {
        volumePercent = Math.Clamp(volumePercent, 0, 100);
        if (volumePercent == 0) return;
        try
        {
            if (_builtFor != volumePercent || _player is null)
            {
                _player?.Dispose();
                _player = new SoundPlayer(new MemoryStream(Build(volumePercent / 100.0)));
                _player.Load();
                _builtFor = volumePercent;
            }
            _player.Play(); // async, never blocks
        }
        catch { }
    }

    private static byte[] Build(double volume)
    {
        const int rate = 44100;
        const double seconds = 1.6;
        var n = (int)(rate * seconds);
        var samples = new double[n];
        // E5 then A5: a calm rising fourth.
        AddNote(samples, rate, 659.25, 0.00, 1.0);
        AddNote(samples, rate, 880.00, 0.20, 0.8);

        var peak = samples.Max(Math.Abs);
        var gain = peak > 0 ? 0.33 * volume / peak : 0; // never louder than ~-10 dBFS
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        foreach (var s in samples) w.Write((short)Math.Round(Math.Clamp(s * gain, -1, 1) * short.MaxValue));
        return ms.ToArray();
    }

    private static void AddNote(double[] buf, int rate, double freq, double at, double level)
    {
        var start = (int)(at * rate);
        for (var i = start; i < buf.Length; i++)
        {
            var t = (double)(i - start) / rate;
            var attack = Math.Min(1, t / 0.012);          // 12 ms fade-in: no click
            var decay = Math.Exp(-t / 0.38);              // bell-like ring-out
            var tail = Math.Min(1, (buf.Length - i) / (rate * 0.05)); // fade to silence at the end
            var tone = Math.Sin(2 * Math.PI * freq * t)
                       + 0.18 * Math.Sin(2 * Math.PI * freq * 2 * t) * Math.Exp(-t / 0.15); // a touch of shimmer, short-lived
            buf[i] += level * attack * decay * tail * tone;
        }
    }
}
