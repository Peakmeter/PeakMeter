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
        Closed += (_, _) => _meter.Pets.Changed -= OnChanged;
        Refresh();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

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
        public string Level { get; init; } = "";
        public string Souls { get; init; } = "";
        public double Bar { get; init; }
        public string Left { get; init; } = "";
        public Brush? LevelBrush { get; init; }
        public bool Full { get; init; }
        public int Sort { get; init; }
    }
}
