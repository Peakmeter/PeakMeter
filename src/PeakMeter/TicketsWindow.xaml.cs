using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PeakMeter;

/// <summary>All your characters and their Odyle energy, estimated forward since you last played each.</summary>
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
        var rows = _meter.Characters.All().Select(c =>
        {
            var (total, gained, full) = Characters.Estimate(c, o.Cap, o.Regen, now);
            var current = c.Name == self && c.ServerId == _meter.Tracker.SelfServerId;
            if (current && o.Known) // the one you play: the live value
            {
                total = o.Current(now);
                gained = 0;
                full = o.Full(now);
            }
            var server = _meter.Data.ServerName(c.ServerId);
            return new Row
            {
                Key = c.Key,
                Name = c.Name + (server.Length > 0 ? $"  ·  {server}" : ""),
                Seen = current ? L.S("Spielst du gerade", "Playing now") : L.S("Zuletzt gespielt ", "Last played ") + Ago(now - c.LastSeen),
                Odyle = total >= 0 ? total.ToString(L.Culture) : "–",
                Gain = gained > 0 ? $"(+{gained})" : "",
                Full = full,
                Current = current,
                ClassBrush = ClassStyle.Solid(c.Class),
                Icon = ClassStyle.Icon(c.Class),
            };
        }).ToList();
        List.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QuaiBadge.Visibility = _settings.OdyleQuai ? Visibility.Visible : Visibility.Collapsed;

        var daily = _meter.Timers.Events.FirstOrDefault(e => e.Def.Id == "daily");
        var weekly = _meter.Timers.Events.FirstOrDefault(e => e.Def.Id == "weekly");
        string Time(EventState? e) => e is null ? "?" : e.Start.ToLocalTime().ToString(_settings.Use24h ? "HH:mm" : "h:mm tt", L.Culture);
        var weekday = weekly is null ? "" : weekly.Start.ToLocalTime().ToString("dddd", L.Culture);
        ResetText.Text = L.S($"Tägliches Zurücksetzen um {Time(daily)}, wöchentliches am {weekday} um {Time(weekly)}",
                             $"Daily reset at {Time(daily)}, weekly on {weekday} at {Time(weekly)}");
        var fullCount = rows.Count(r => r.Full);
        FullText.Text = fullCount == 0 ? ""
            : fullCount == 1 ? L.S("1 Charakter voll – die nächste Aufladung verfällt", "1 character full – the next recharge is wasted")
            : L.S($"{fullCount} Charaktere voll – die nächste Aufladung verfällt", $"{fullCount} characters full – the next recharge is wasted");
    }

    private static string Ago(TimeSpan t)
    {
        if (t.TotalMinutes < 2) return L.S("gerade eben", "just now");
        if (t.TotalHours < 1) return L.S($"vor {(int)t.TotalMinutes} Min.", $"{(int)t.TotalMinutes} min ago");
        if (t.TotalDays < 1) return L.S($"vor {(int)t.TotalHours} Std.", $"{(int)t.TotalHours} h ago");
        return L.S($"vor {(int)t.TotalDays} Tg.", $"{(int)t.TotalDays} d ago");
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Row r) _meter.Characters.Remove(r.Key);
    }

    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public sealed class Row
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string Seen { get; init; } = "";
        public string Odyle { get; init; } = "";
        public string Gain { get; init; } = "";
        public bool Full { get; init; }
        public bool Current { get; init; }
        public Brush? ClassBrush { get; init; }
        public ImageSource? Icon { get; init; }
    }
}
