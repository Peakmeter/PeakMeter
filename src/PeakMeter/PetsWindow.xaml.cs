using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PeakMeter;

/// <summary>Every pet: level, souls toward the next level, souls left to level 3, and where its souls drop.</summary>
public partial class PetsWindow : Window
{
    private readonly Meter _meter;
    private bool _hideFull = true;
    private bool _chestOnly;
    private bool _areaOnly;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };

    private static readonly Dictionary<string, Brush> GenusBrushes = new()
    {
        ["cogni"] = Frozen(0x7F, 0xB2, 0xF2),
        ["fera"] = Frozen(0xE8, 0x8A, 0x5B),
        ["natura"] = Frozen(0x7C, 0xC7, 0x7A),
        ["varian"] = Frozen(0xB3, 0x8B, 0xE8),
    };

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public PetsWindow(Meter meter)
    {
        _meter = meter;
        InitializeComponent();
        _meter.Pets.Changed += OnChanged;
        // Monsters come into view and the map changes while the window is open: the area section follows.
        _timer.Tick += (_, _) => RefreshArea();
        _timer.Start();
        Closed += (_, _) =>
        {
            _meter.Pets.Changed -= OnChanged;
            _timer.Stop();
        };
        Refresh();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private int _areaKeyMap = -1;
    private string _areaKey = "";

    /// <summary>
    /// The current map's soul monsters whose pets are not full yet: each with its pets (coloured by level), where on
    /// the map and the nearest Kibelisk, and how many are in view right now; those in view first.
    /// </summary>
    private void RefreshArea()
    {
        var book = _meter.PetBook;
        var server = _meter.Tracker.SelfServerId;
        var known = _meter.Pets.Known(server);
        var map = _meter.Tracker.MapId;
        var near = _meter.NearbyPetMonsters();
        var drops = book.DropsOn(map).ToList();

        var codes = drops.Select(d => d.Drop.Npc).Concat(near.Keys).Distinct();
        var rows = new List<AreaRow>();
        foreach (var code in codes)
        {
            if (!book.ByNpc.TryGetValue(code, out var feeds)) continue;
            var drop = drops.FirstOrDefault(d => d.Drop.Npc == code).Drop
                       ?? book.Pets.Values.SelectMany(p => p.Drops).FirstOrDefault(d => d.Npc == code);
            if (drop is null) continue;
            var chips = new List<PetChip>();
            var minLeft = int.MaxValue;
            foreach (var pet in feeds)
            {
                var (level, points) = _meter.Pets.Get(server, pet);
                if (known && level >= PetCollection.MaxLevel) continue;
                minLeft = Math.Min(minLeft, PetCollection.ToMax(level, points));
                chips.Add(new PetChip(!known ? book.Name(pet)
                        : $"{book.Name(pet)} {L.S("St.", "Lv")} {level} · {points}/{PetCollection.Need(level)}",
                    known ? PetCollection.ColorOf(level) : (Brush)FindResource("TextSoft")));
            }
            if (chips.Count == 0) continue; // every pet of this monster is full: nothing to farm here
            var count = near.GetValueOrDefault(code);
            var where = new List<string>();
            if (drop.ZoneNames is { Count: > 0 } z) where.Add(PetBook.Local(z));
            if (drop.Teleport > 0)
                where.Add(L.S("Kibelisk", "Kibelisk") + $" #{drop.Teleport}" + (drop.TeleportNames is { Count: > 0 } t ? $" {PetBook.Local(t)}" : ""));
            if (drop.Spawns > 0) where.Add(L.S($"{drop.Spawns} Spawns", $"{drop.Spawns} spawns"));
            if (drop.Chance is { } c && c != "100%") where.Add(c);
            rows.Add(new AreaRow
            {
                Monster = $"{PetBook.Local(drop.NpcNames)}  ({L.S("St.", "Lv")} {drop.Level})",
                Pets = chips,
                Where = string.Join(" · ", where),
                Near = L.S($"● {count} in der Nähe", $"● {count} nearby"),
                NearVisible = count > 0 ? Visibility.Visible : Visibility.Collapsed,
                Url = drop.MapUrl,
                UrlVisible = drop.MapUrl is null ? Visibility.Collapsed : Visibility.Visible,
                Sort = (count > 0 ? 0 : 1_000_000) + minLeft,
            });
        }

        var mapName = drops.Select(d => PetBook.Local(d.Drop.MapNames)).FirstOrDefault(n => n.Length > 0) ?? _meter.Tracker.ZoneName;
        AreaTitle.Text = mapName is { Length: > 0 }
            ? L.S($"Hier im Gebiet: {mapName}", $"Here in the area: {mapName}")
            : L.S("Hier im Gebiet", "Here in the area");
        AreaOnlyChip.Foreground = (Brush)FindResource(_areaOnly ? "Accent" : "TextDim");
        AreaEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AreaEmpty.Text = map == 0 && near.Count == 0
            ? L.S("Karte noch unbekannt: nach dem nächsten Teleport oder Laden einer Karte erscheint hier, was du in diesem Gebiet farmen kannst.",
                  "Map not known yet: after the next teleport or map load this shows what you can farm in the area.")
            : drops.Count == 0 && near.Count == 0
                ? L.S("Auf dieser Karte droppen keine Pet-Seelen.", "No pet souls drop on this map.")
                : L.S("Alles voll: Hier gibt es keine Seelen mehr, die dir fehlen. 🎉", "All full: nothing here you still need. 🎉");

        // Only redraw when something changed, so the list does not flicker every 3 s.
        var key = string.Join("|", rows.Select(r => r.Monster + r.Near + string.Join(",", r.Pets.Select(p => p.Text))));
        if (key == _areaKey && map == _areaKeyMap) return;
        _areaKey = key;
        if (_areaOnly && map != _areaKeyMap) { _areaKeyMap = map; Refresh(); return; }
        _areaKeyMap = map;
        AreaList.ItemsSource = rows.OrderBy(r => r.Sort).ToList();
    }

    private void Map_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string { Length: > 0 } url) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private void AreaOnly_Click(object sender, RoutedEventArgs e)
    {
        _areaOnly = !_areaOnly;
        Refresh();
    }

    public sealed record PetChip(string Text, Brush Color);

    private sealed class AreaRow
    {
        public string Monster { get; init; } = "";
        public List<PetChip> Pets { get; init; } = new();
        public string Where { get; init; } = "";
        public string Near { get; init; } = "";
        public Visibility NearVisible { get; init; }
        public string? Url { get; init; }
        public Visibility UrlVisible { get; init; }
        public int Sort { get; init; }
    }

    private void Refresh()
    {
        var book = _meter.PetBook;
        var server = _meter.Tracker.SelfServerId;
        var known = _meter.Pets.Known(server);
        var query = Search.Text.Trim();
        SearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        HideFullChip.Foreground = (Brush)FindResource(_hideFull ? "Accent" : "TextDim");
        ChestChip.Foreground = (Brush)FindResource(_chestOnly ? "Accent" : "TextDim");

        Note.Visibility = known ? Visibility.Collapsed : Visibility.Visible;
        Note.Text = server <= 0
            ? L.S("Noch kein Charakter erkannt. Logge dich mit laufendem PeakMeter ein.", "No character seen yet. Log in with PeakMeter running.")
            : L.S("Stand noch unbekannt: einmal mit laufendem PeakMeter neu einloggen, dann kennt PeakMeter alle Pets (pro Server).",
                  "Progress not known yet: log in once with PeakMeter running, then PeakMeter knows all pets (per server).");

        var all = book.Pets.Values.Where(p => !p.Special).ToList();
        var rows = new List<Row>();
        int full = 0, left = 0;
        foreach (var p in all)
        {
            var (level, points) = _meter.Pets.Get(server, p.Id);
            var toMax = PetCollection.ToMax(level, points);
            var isFull = known && level >= PetCollection.MaxLevel;
            if (isFull) full++;
            else if (!p.ChestOnly) left += toMax;
            if (_hideFull && isFull) continue;
            if (p.ChestOnly && !_chestOnly) continue;
            if (_areaOnly && !p.Drops.Any(d => d.MapId == _meter.Tracker.MapId)) continue;
            var name = book.Name(p.Id);
            var best = p.Drops.OrderByDescending(d => d.Spawns).FirstOrDefault();
            var where = best is null ? L.S("nur aus Pet-Seelen-Truhen", "only from pet soul chests")
                : $"{PetBook.Local(best.NpcNames)} · {PetBook.Local(best.MapNames)} ({L.S("St.", "Lv")} {best.Level})" + (best.Chance is { } c && c != "100%" ? $" · {c}" : "");
            if (query.Length > 0 && !name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !p.Drops.Any(d => PetBook.Local(d.NpcNames).Contains(query, StringComparison.OrdinalIgnoreCase))
                && p.Id.ToString() != query)
                continue;
            var need = PetCollection.Need(level);
            rows.Add(new Row
            {
                Name = name,
                Genus = book.GenusName(p.Genus),
                GenusBrush = p.Genus is not null && GenusBrushes.TryGetValue(p.Genus, out var gb) ? gb : (Brush)FindResource("TextDim"),
                Where = where,
                Url = best?.MapUrl,
                UrlVisible = best?.MapUrl is null ? Visibility.Collapsed : Visibility.Visible,
                WhereAll = string.Join(Environment.NewLine, p.Drops.Select(d =>
                    $"{PetBook.Local(d.NpcNames)} – {PetBook.Local(d.MapNames)}, {L.S("St.", "Lv")} {d.Level}, {d.Chance}, {d.Spawns}×")) is { Length: > 0 } w ? w : where,
                Level = known ? $"{L.S("St.", "Lv")} {level}" : "–",
                Souls = !known ? "–" : level >= PetCollection.MaxLevel ? "MAX" : $"{points} / {need}",
                Bar = !known ? 0 : level >= PetCollection.MaxLevel ? 98 : Math.Round(98.0 * points / Math.Max(1, need)),
                Left = !known ? "–" : level >= PetCollection.MaxLevel ? "✓" : toMax.ToString(),
                LevelBrush = known ? PetCollection.ColorOf(level) : (Brush)FindResource("TextDim"),
                Full = isFull,
                Sort = !known ? int.MaxValue : isFull ? int.MaxValue - 1 : toMax,
            });
        }
        List.ItemsSource = rows.OrderBy(r => r.Sort).ThenBy(r => r.Name).ToList();
        _areaKey = "";
        RefreshArea();
        Summary.Text = known
            ? L.S($"{full} / {all.Count} auf St. 3 · noch {left} Seelen", $"{full} / {all.Count} at Lv 3 · {left} souls to go")
            : "";
    }

    private void Search_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();

    private void HideFull_Click(object sender, RoutedEventArgs e)
    {
        _hideFull = !_hideFull;
        Refresh();
    }

    private void Chest_Click(object sender, RoutedEventArgs e)
    {
        _chestOnly = !_chestOnly;
        Refresh();
    }

    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class Row
    {
        public string Name { get; init; } = "";
        public string Genus { get; init; } = "";
        public Brush? GenusBrush { get; init; }
        public string Where { get; init; } = "";
        public string WhereAll { get; init; } = "";
        public string? Url { get; init; }
        public Visibility UrlVisible { get; init; }
        public string Level { get; init; } = "";
        public string Souls { get; init; } = "";
        public double Bar { get; init; }
        public string Left { get; init; } = "";
        public Brush? LevelBrush { get; init; }
        public bool Full { get; init; }
        public int Sort { get; init; }
    }
}
