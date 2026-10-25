using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PeakMeter;

public partial class TimersWindow : Window
{
    private static CultureInfo De => L.Culture;
    private readonly Meter _meter;
    private readonly Settings _settings;
    private readonly DispatcherTimer _timer;
    private List<EventVm> _items = [];

    public TimersWindow(Meter meter, Settings settings)
    {
        _meter = meter;
        _settings = settings;
        InitializeComponent();
        Volume.Value = _settings.SoundVolume;
        VolumeText.Text = $"{_settings.SoundVolume}%";
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        Closed += (_, _) => _timer.Stop();
        ShowTab(false);
    }

    // ------------------------------------------------------------------ tabs

    private bool _bossTab;
    private string _region = "";
    private List<BossVm> _bossItems = [];

    private void Tab_Click(object sender, RoutedEventArgs e) => ShowTab((sender as FrameworkElement)?.Tag as string == "bosses");

    private void ShowTab(bool bosses)
    {
        _bossTab = bosses;
        EventsPanel.Visibility = bosses ? Visibility.Collapsed : Visibility.Visible;
        BossesPanel.Visibility = bosses ? Visibility.Visible : Visibility.Collapsed;
        EditButton.Visibility = bosses ? Visibility.Collapsed : Visibility.Visible;
        NoteText.Margin = new Thickness(0, 0, bosses ? 0 : 120, 0);
        NoteText.Text = bosses
            ? L.S("Respawn-Zeiten nach dem Kill aus der Community, bis das Spiel die genauen Zeiten liefert. Jeder Server hat eigene Timer.", "Respawn times after a kill from the community, until the game sends the exact times. Every server has its own timers.")
            : L.S("EU-Zeiten aus der Community (NCSoft veröffentlicht keine). Stimmt etwas nicht? Einfach anpassen.", "EU times from the community (NCSOFT publishes none). Something off? Just edit them.");
        EventsTab.Foreground = (System.Windows.Media.Brush)FindResource(bosses ? "TextDim" : "Text");
        BossesTab.Foreground = (System.Windows.Media.Brush)FindResource(bosses ? "Text" : "TextDim");
        MarkRegion();
        Refresh();
    }

    private void Region_Click(object sender, RoutedEventArgs e)
    {
        _region = (sender as FrameworkElement)?.Tag as string ?? "";
        MarkRegion();
        Refresh();
    }

    private void MarkRegion()
    {
        foreach (var (chip, tag) in new[] { (RegAll, ""), (RegV, "Verteron"), (RegA, "Altgard") })
            chip.Foreground = (System.Windows.Media.Brush)FindResource(tag == _region ? "Text" : "TextDim");
    }

    private void RefreshBosses()
    {
        var b = _meter.Bosses;
        var server = b.CurrentServer;
        var now = DateTimeOffset.Now;
        var timers = b.ForServer(server).GroupBy(t => t.NpcCode).ToDictionary(g => g.Key, g => g.First());
        var rows = b.DefaultRespawn.Keys.Concat(timers.Keys).Distinct()
            .Select(code => (Code: code, Timer: timers.GetValueOrDefault(code)))
            .Where(r => _region.Length == 0 || b.RegionOf(r.Code) == _region)
            .Select(r =>
            {
                var vm = _bossItems.FirstOrDefault(x => x.Code == r.Code) ?? new BossVm(r.Code, _meter);
                vm.Update(r.Timer, now);
                return vm;
            })
            .OrderBy(vm => vm.SortGroup).ThenBy(vm => vm.SortKey).ThenBy(vm => vm.Name)
            .ToList();
        if (rows.Count != _bossItems.Count || rows.Where((r, i) => !ReferenceEquals(_bossItems[i], r)).Any())
        {
            _bossItems = rows;
            BossList.ItemsSource = _bossItems;
        }
    }

    private void BossBell_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BossVm vm) return;
        _meter.Bosses.Watch(_meter.Bosses.CurrentServer, vm.Code, !vm.Watch);
        Refresh();
        if (!vm.Watch) return;
        Chime.Play(_settings.SoundVolume);
    }

    private void Killed_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BossVm vm) return;
        _meter.Bosses.MarkKilled(_meter.Bosses.CurrentServer, vm.Code);
        Refresh();
    }

    public sealed class BossVm(int code, Meter meter) : Vm
    {
        private string _sub = "", _countdown = "", _at = "";
        private bool _alive, _known, _watch;

        public int Code { get; } = code;
        public string Name { get; } = meter.BossName(code);
        public string Sub { get => _sub; private set => Set(ref _sub, value); }
        public string Countdown { get => _countdown; private set => Set(ref _countdown, value); }
        public string At { get => _at; private set => Set(ref _at, value); }
        public bool Alive { get => _alive; private set => Set(ref _alive, value); }
        public bool Known { get => _known; private set => Set(ref _known, value); }
        public bool Watch { get => _watch; private set => Set(ref _watch, value); }
        /// <summary>0 = counting down, 1 = alive / due, 2 = unknown.</summary>
        public int SortGroup { get; private set; }
        public long SortKey { get; private set; }

        public void Update(BossTimer? t, DateTimeOffset now)
        {
            var b = meter.Bosses;
            var minutes = t is { RespawnMinutes: > 0 } ? t.RespawnMinutes : b.DefaultRespawn.GetValueOrDefault(Code);
            var region = b.RegionOf(Code) ?? (t?.Zone ?? "");
            var parts = new List<string>();
            if (region.Length > 0) parts.Add(region);
            if (minutes > 0) parts.Add(L.S("alle ", "every ") + Interval(minutes));
            if (t?.FromGame == true) parts.Add(L.S("Zeit vom Spiel ✓", "time from the game ✓"));
            Sub = string.Join(" · ", parts);
            Watch = t?.Watch ?? false;

            if (t?.AliveNow == true)
            {
                Alive = true; Known = true;
                Countdown = L.S("lebt", "alive");
                At = t.AliveSince is { } since ? L.S("seit ", "since ") + since.ToLocalTime().ToString("HH:mm", De) : "";
                SortGroup = 1; SortKey = 0;
            }
            else if (t?.NextSpawn is { } next)
            {
                Known = true;
                if (next > now)
                {
                    Alive = false;
                    Countdown = Clock(next - now);
                    var local = next.ToLocalTime();
                    At = local.Date == DateTime.Today ? L.S("um ", "at ") + local.ToString("HH:mm", De) : local.ToString("ddd HH:mm", De);
                    SortGroup = 0; SortKey = next.ToUnixTimeSeconds();
                }
                else
                {
                    Alive = true;
                    Countdown = L.S("sollte da sein", "should be up");
                    At = L.S("seit ", "since ") + next.ToLocalTime().ToString("HH:mm", De);
                    SortGroup = 1; SortKey = -next.ToUnixTimeSeconds();
                }
            }
            else
            {
                Alive = false; Known = false;
                Countdown = "—";
                At = L.S("unbekannt", "unknown");
                SortGroup = 2; SortKey = 0;
            }
        }

        private static string Interval(int minutes) =>
            minutes < 60 ? L.S($"{minutes} Min", $"{minutes} min") : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60:00}";
    }

    private void Refresh()
    {
        if (_bossTab)
        {
            RefreshBosses();
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var t = _meter.Timers;
        ErrorText.Text = t.Error ?? "";
        ErrorText.Visibility = t.Error is null ? Visibility.Collapsed : Visibility.Visible;

        var order = t.Upcoming(now).Where(e => e.Start != DateTimeOffset.MaxValue).ToList();
        if (order.Count != _items.Count || order.Where((e, i) => !ReferenceEquals(_items[i].State, e)).Any())
        {
            _items = order.Select(e => new EventVm(e)).ToList();
            List.ItemsSource = _items;
        }
        foreach (var vm in _items) vm.Update(now);
    }

    private void Bell_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EventVm vm) return;
        _meter.Timers.SetAlert(vm.State, !vm.State.Def.Alert);
        vm.Update(DateTimeOffset.UtcNow);
        if (vm.State.Def.Alert) Chime.Play(_settings.SoundVolume);
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeText is null) return;
        _settings.SoundVolume = (int)Math.Round(e.NewValue);
        VolumeText.Text = _settings.SoundVolume == 0 ? L.S("aus", "off") : $"{_settings.SoundVolume}%";
    }

    private void Volume_Done(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => VolumeSet();
    private void Volume_Up(object sender, MouseButtonEventArgs e) => VolumeSet();

    private void VolumeSet()
    {
        _settings.Save();
        Chime.Play(_settings.SoundVolume);
    }

    private void Test_Click(object sender, RoutedEventArgs e) => Chime.Play(_settings.SoundVolume);

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{EventTimers.FilePath}\"") { UseShellExecute = true }); }
        catch { }
    }

    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public sealed class EventVm(EventState state) : Vm
    {
        private string _countdown = "", _at = "";
        private bool _running, _alert;

        public EventState State { get; } = state;
        public string Name => State.Def.Name;
        public string Schedule => Describe(State.Def);
        public string Countdown { get => _countdown; private set => Set(ref _countdown, value); }
        public string At { get => _at; private set => Set(ref _at, value); }
        public bool Running { get => _running; private set => Set(ref _running, value); }
        public bool Alert { get => _alert; private set => Set(ref _alert, value); }

        public void Update(DateTimeOffset now)
        {
            Alert = State.Def.Alert;
            Running = State.Running(now);
            if (Running)
            {
                Countdown = L.S("läuft · ", "running · ") + Clock(State.End - now);
                At = L.S("bis ", "until ") + State.End.ToLocalTime().ToString("HH:mm", De);
            }
            else
            {
                Countdown = Clock(State.Start - now);
                var local = State.Start.ToLocalTime();
                At = local.Date == DateTime.Today ? L.S("um ", "at ") + local.ToString("HH:mm", De)
                    : local.Date == DateTime.Today.AddDays(1) ? L.S("morgen ", "tomorrow ") + local.ToString("HH:mm", De)
                    : local.ToString("ddd HH:mm", De);
            }
        }
    }

    /// <summary>12:34 below an hour, "2 h 05" above, "3 T 4 h" above a day.</summary>
    public static string Clock(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalDays >= 1) return L.S($"{(int)t.TotalDays} T {t.Hours} h", $"{(int)t.TotalDays} d {t.Hours} h");
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes:00}";
        return $"{t.Minutes}:{t.Seconds:00}";
    }

    private static string Describe(EventDef d)
    {
        var days = d.Days.Count == 0 ? "" : string.Join(", ", d.Days.Select(GermanDay)) + " ";
        string when;
        if (d.Times.Count == 0 && d.HourlyAt is { } m) when = L.S($"stündlich zur :{m:00}", $"hourly at :{m:00}");
        else if (d.Times.Count <= 3) when = string.Join(", ", d.Times);
        else when = L.S($"{d.Times.Count}× täglich ab {d.Times[0]}", $"{d.Times.Count}× daily from {d.Times[0]}");
        var tz = d.TimeZone is "UTC" ? " (UTC)" : "";
        return L.S($"{days}{when}{tz} · {d.DurationMin} Min", $"{days}{when}{tz} · {d.DurationMin} min");
    }

    private static string GermanDay(string s) => s.Trim().ToLowerInvariant()[..Math.Min(2, s.Trim().Length)] switch
    {
        "mo" => L.S("Mo", "Mon"), "tu" or "di" => L.S("Di", "Tue"), "we" or "mi" => L.S("Mi", "Wed"), "th" or "do" => L.S("Do", "Thu"),
        "fr" => L.S("Fr", "Fri"), "sa" => L.S("Sa", "Sat"), "su" or "so" => L.S("So", "Sun"), _ => s,
    };
}
