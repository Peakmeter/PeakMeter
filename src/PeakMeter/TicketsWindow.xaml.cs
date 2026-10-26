using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PeakMeter;

/// <summary>
/// Your characters grouped by server, with Odyle energy (estimated forward since each was last played) and the other
/// tickets. Tickets PeakMeter cannot read yet show "–" until a "learn tickets" recording has taught it where they are.
/// </summary>
public partial class TicketsWindow : Window
{
    private readonly Meter _meter;
    private readonly Settings _settings;
    private readonly DispatcherTimer _timer;

    public TicketsWindow(Meter meter, Settings settings)
    {
        _meter = meter;
        _settings = settings;
        InitializeComponent();
        IcoOdyle.Source = EventIcons.Get("odyle");
        IcoNightmare.Source = EventIcons.Get("nightmare");
        IcoAscension.Source = EventIcons.Get("ascension");
        IcoDaily.Source = EventIcons.Get("daily");
        IcoShugo.Source = EventIcons.Get("shugokey");
        IcoInvasion.Source = EventIcons.Get("invasionkey");
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        _meter.Characters.Changed += OnChanged;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _meter.Characters.Changed -= OnChanged;
        };
        Loaded += (_, _) => Refresh();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var now = DateTimeOffset.Now;
        var o = _meter.Odyle;
        var self = _meter.Tracker.SelfName;
        var selfServer = _meter.Tracker.SelfServerId;
        var estimated = 0;
        // values from before the last daily reset are stale: show "–" instead of a wrong number
        var lastReset = _meter.Timers.Events.FirstOrDefault(e => e.Def.Id == "daily")?.Start.AddDays(-1) ?? DateTimeOffset.MinValue;
        var rows = _meter.Characters.All().Select(c =>
        {
            var (total, gained, full) = Characters.Estimate(c, o.Cap, o.Regen, now);
            var bonus = c.OdyleBonus;
            var current = c.Name == self && c.ServerId == selfServer;
            if (current && o.Known)
            {
                total = o.Current(now);
                bonus = o.Bonus;
                gained = 0;
                full = o.Full(now);
            }
            if (gained > 0) estimated++;
            return (c.ServerId, Row: new Row
            {
                Key = c.Key,
                Name = c.Name,
                Seen = current ? L.S("Spielst du gerade", "Playing now") : L.S("Zuletzt gespielt ", "Last played ") + Ago(now - c.LastSeen),
                Odyle = total < 0 ? "–" : (gained > 0 ? "~" : "") + (total - bonus).ToString(L.Culture),
                Gain = total >= 0 && bonus > 0 ? $"(+{bonus})" : "",
                Full = full,
                Current = current,
                Nightmare = Ticket(c, "nightmare", lastReset),
                Ascension = Ticket(c, "ascension", lastReset),
                Daily = Ticket(c, "daily", lastReset),
                ShugoKey = Ticket(c, "shugokey", lastReset),
                InvasionKey = Ticket(c, "invasionkey", lastReset),
                ClassBrush = ClassStyle.Solid(c.Class),
                Icon = ClassStyle.Icon(c.Class),
            });
        }).ToList();

        Groups.ItemsSource = rows.GroupBy(r => r.ServerId).Select(g => new Group
        {
            Server = g.Key == 0 ? L.S("Server unbekannt", "Unknown server") : _meter.Data.ServerName(g.Key),
            QuaiText = L.S("Quai-Mitgliedschaft", "Quai membership"),
            QuaiVisible = _settings.OdyleQuai ? Visibility.Visible : Visibility.Collapsed,
            Rows = g.Select(x => x.Row).ToList(),
        }).ToList();
        EmptyText.Text = rows.Count == 0
            ? L.S("Noch keine Charaktere. Log dich mit PeakMeter im Hintergrund ein – jeder Charakter, den du spielst, erscheint hier.",
                  "No characters yet. Log in with PeakMeter running – every character you play shows up here.")
            : "";
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var daily = _meter.Timers.Events.FirstOrDefault(e => e.Def.Id == "daily");
        var weekly = _meter.Timers.Events.FirstOrDefault(e => e.Def.Id == "weekly");
        string Time(EventState? e) => e is null ? "?" : e.Start.ToLocalTime().ToString(_settings.Use24h ? "HH:mm" : "h:mm tt", L.Culture);
        var weekday = weekly is null ? "" : weekly.Start.ToLocalTime().ToString("dddd", L.Culture);
        ResetText.Text = L.S($"Tägliches Zurücksetzen um {Time(daily)}, wöchentliches am {weekday} um {Time(weekly)}",
                             $"Daily reset at {Time(daily)}, weekly on {weekday} at {Time(weekly)}");
        EstimateText.Text = estimated == 0 ? ""
            : L.S($"{estimated} geschätzt seit du den Charakter zuletzt gespielt hast (+15 alle 3 Std.)",
                  $"{estimated} estimated since you last played the character (+15 every 3 h)");
        var fullCount = rows.Count(r => r.Row.Full);
        FullText.Text = fullCount == 0 ? ""
            : L.S($"{fullCount} voll, die nächste Aufladung verfällt", $"{fullCount} full, the next recharge is wasted");
        EstimateText.Visibility = EstimateText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        FullText.Visibility = FullText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        LearnText.Text = L.S("„–“ = PeakMeter kennt dieses Ticket noch nicht. Mit „Tickets lernen“ zeigst du es ihm einmal, danach kommt es automatisch.",
                             "“–” = PeakMeter does not know this ticket yet. Use “Learn tickets” once, after that it comes in by itself.");
    }

    private static string Ticket(CharacterInfo c, string key, DateTimeOffset lastReset) =>
        c.TicketsAt is { } at && at >= lastReset && c.Tickets.TryGetValue(key, out var v) ? v.ToString(L.Culture) : "–";

    private static string Ago(TimeSpan t)
    {
        if (t.TotalMinutes < 2) return L.S("gerade eben", "just now");
        if (t.TotalHours < 1) return L.S($"vor {(int)t.TotalMinutes} Min.", $"{(int)t.TotalMinutes} min ago");
        if (t.TotalDays < 1) return L.S($"vor {(int)t.TotalHours} Std.", $"{(int)t.TotalHours} h ago");
        return L.S($"vor {(int)t.TotalDays} Tg.", $"{(int)t.TotalDays} d ago");
    }

    private void Learn_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowTicketProbe();

    private void Energy_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowEnergy();

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Row r) _meter.Characters.Remove(r.Key);
    }

    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public sealed class Group
    {
        public string Server { get; init; } = "";
        public string QuaiText { get; init; } = "";
        public Visibility QuaiVisible { get; init; }
        public List<Row> Rows { get; init; } = [];
    }

    public sealed class Row
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string Seen { get; init; } = "";
        public string Odyle { get; init; } = "";
        public string Gain { get; init; } = "";
        public string Nightmare { get; init; } = "–";
        public string Ascension { get; init; } = "–";
        public string Daily { get; init; } = "–";
        public string ShugoKey { get; init; } = "–";
        public string InvasionKey { get; init; } = "–";
        public bool Full { get; init; }
        public bool Current { get; init; }
        public Brush? ClassBrush { get; init; }
        public ImageSource? Icon { get; init; }
    }
}
