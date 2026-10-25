using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Storage;

namespace PeakMeter;

/// <summary>Saved fights: list on the left, the chosen fight's ranking and a player's skills on the right.</summary>
public partial class LogsWindow : Window
{
    private static CultureInfo De => L.Culture;
    private static readonly Brush KillBrush = Frozen(0x33, 0x3F, 0xD1, 0x8B);
    private static readonly Brush WipeBrush = Frozen(0x33, 0xFF, 0x4D, 0x6D);
    private static readonly Brush NoBrush = Frozen(0x14, 0xFF, 0xFF, 0xFF);

    private readonly Meter _meter;
    private bool _bossesOnly;
    private FightRecord? _record;
    private uint? _selected;

    public LogsWindow(Meter meter)
    {
        _meter = meter;
        InitializeComponent();
        _meter.FightSaved += OnSaved;
        Closed += (_, _) => _meter.FightSaved -= OnSaved;
        SizeChanged += (_, _) => ShowRecord();
        LoadList();
    }

    private void OnSaved() => Dispatcher.BeginInvoke(LoadList);

    private void LoadList()
    {
        var keep = (List.SelectedItem as FightItem)?.Entry.FileName;
        var entries = _meter.History.List().Where(e => !_bossesOnly || e.IsBoss).ToList();
        var items = entries.Select(e => new FightItem(e, e.Reason == EncounterEndReason.Kill && e.BossCode != 0 &&
                                                         e.CombatMs == _meter.Records.BestKillMs(e.BossCode))).ToList();
        List.ItemsSource = items;
        EmptyList.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = items.Count == 1 ? L.S("1 Kampf", "1 fight") : L.S($"{items.Count} Kämpfe", $"{items.Count} fights");
        AllChip.Foreground = (Brush)FindResource(_bossesOnly ? "TextDim" : "Text");
        BossChip.Foreground = (Brush)FindResource(_bossesOnly ? "Text" : "TextDim");

        var again = items.FirstOrDefault(i => i.Entry.FileName == keep) ?? items.FirstOrDefault();
        List.SelectedItem = again;
    }

    private void List_Selected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _record = List.SelectedItem is FightItem item ? _meter.History.Load(item.Entry.Path) : null;
        _selected = _record?.Summary.Combatants.FirstOrDefault(c => c.IsSelf)?.ActorId;
        ShowRecord();
    }

    private void ShowRecord()
    {
        if (_record is not { } r)
        {
            Detail.Visibility = Visibility.Collapsed;
            Pick.Visibility = Visibility.Visible;
            return;
        }
        Pick.Visibility = Visibility.Collapsed;
        Detail.Visibility = Visibility.Visible;

        var s = r.Summary;
        FightTitle.Text = s.Title;
        var result = s.Reason switch { EncounterEndReason.Kill => "Kill", EncounterEndReason.Wipe => "Wipe", _ => null };
        FightMeta.Text = string.Join("  ·  ", new[]
        {
            s.StartedAt.ToLocalTime().ToString("ddd dd.MM.yyyy  HH:mm", De),
            Format.ClockShort(s.CombatMs),
            L.S($"Gruppe {Format.Compact(s.PartyDps)} DPS", $"Party {Format.Compact(s.PartyDps)} DPS"),
            L.S($"{s.PlayerCount} Spieler", $"{s.PlayerCount} players"),
            result, s.Zone,
        }.Where(x => !string.IsNullOrEmpty(x)));

        var list = s.Combatants.OrderBy(c => c.IsUnknownSummons).ThenByDescending(c => c.Damage).ToList();
        var top = Math.Max(1, list.Count > 0 ? list.Max(c => c.Damage) : 1);
        var width = Math.Max(0, Rows.ActualWidth > 0 ? Rows.ActualWidth : 400);
        Rows.ItemsSource = list.Select((c, i) =>
        {
            var vm = new RowVm
            {
                ActorId = c.ActorId,
                Rank = c.IsUnknownSummons ? "" : (i + 1).ToString(),
                Name = c.IsUnknownSummons ? L.S("Beschwörungen", "Summons") : c.Name,
                Dps = Format.Compact(c.Dps),
                Share = Format.Share(c.Share),
                Power = c.CombatPower > 0 ? "  " + Format.Power(c.CombatPower) : "",
                BarWidth = Math.Round(width * c.Damage / top),
                IsSelf = c.IsSelf,
                IsSelected = c.ActorId == _selected,
                RowHeight = 28,
            };
            vm.SetClass(c.Class);
            return vm;
        }).ToList();

        var d = r.Details.FirstOrDefault(x => x.ActorId == _selected);
        if (d is null)
        {
            SkillPanel.Visibility = Visibility.Collapsed;
            return;
        }
        SkillPanel.Visibility = Visibility.Visible;
        SkillTitle.Text = d.Name;
        SkillStats.Text = L.S($"{Format.Compact(d.Damage)} Schaden · {Format.Compact(d.Dps)} DPS · Krit {Format.Percent(d.Quality.CritRate)} · ", $"{Format.Compact(d.Damage)} damage · {Format.Compact(d.Dps)} DPS · crit {Format.Percent(d.Quality.CritRate)} · ") +
                          $"Max {Format.Compact(d.Quality.MaxHit)}";
        var skills = d.Skills.OrderByDescending(x => x.Damage).ToList();
        var stop = Math.Max(1, skills.Count > 0 ? skills[0].Damage : 1);
        var swidth = Math.Max(0, Skills.ActualWidth > 0 ? Skills.ActualWidth : width);
        var brush = ClassStyle.Bar(d.Class, 0.22);
        Skills.ItemsSource = skills.Select(x => new SkillVm
        {
            Name = $"{x.Name}   ×{x.Hits + x.DotTicks}",
            Damage = Format.Compact(x.Damage),
            Share = Format.Share(x.Share),
            BarWidth = Math.Round(swidth * x.Damage / stop),
            BarBrush = brush,
        }).ToList();
    }

    private void Rows_Click(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not RowVm row) return;
        _selected = row.ActorId;
        ShowRecord();
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        _bossesOnly = (sender as FrameworkElement)?.Tag as string == "boss";
        LoadList();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_record?.Summary is not { } s) return;
        var sb = new StringBuilder();
        sb.Append(s.Title).Append(" (").Append(Format.ClockShort(s.CombatMs)).Append(')');
        var i = 0;
        foreach (var c in s.Combatants.Where(c => !c.IsUnknownSummons).OrderByDescending(c => c.Damage).Take(8))
            sb.Append(" | ").Append(++i).Append(". ").Append(c.Name).Append(' ')
              .Append(Format.Compact(c.Dps)).Append(" (").Append(Format.Share(c.Share)).Append(')');
        try { Clipboard.SetText(sb.ToString()); } catch { }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not FightItem item) return;
        var index = List.SelectedIndex;
        _meter.History.Delete(item.Entry.Path);
        LoadList();
        if (List.Items.Count > 0) List.SelectedIndex = Math.Min(index, List.Items.Count - 1);
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_meter.History.Directory);
        Process.Start(new ProcessStartInfo("explorer.exe", _meter.History.Directory) { UseShellExecute = true });
    }

    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public sealed class FightItem(HistoryEntry entry, bool best = false)
    {
        public HistoryEntry Entry { get; } = entry;
        public string Title { get; } = entry.Title;
        public string Meta { get; } =
            $"{entry.StartedAt.ToLocalTime().ToString("ddd dd.MM. HH:mm", De)} · {Format.ClockShort(entry.CombatMs)} · {Format.Compact(entry.PartyDps)} DPS";
        public string Badge { get; } = best ? L.S("BESTZEIT", "BEST") : entry.Reason switch { EncounterEndReason.Kill => "KILL", EncounterEndReason.Wipe => "WIPE", _ => entry.IsBoss ? "BOSS" : "" };
        public Brush BadgeBrush { get; } = entry.Reason switch
        {
            EncounterEndReason.Kill => KillBrush,
            EncounterEndReason.Wipe => WipeBrush,
            _ => entry.IsBoss ? NoBrush : Brushes.Transparent,
        };
        public string Place { get; } = entry.SelfPlace > 0 ? $"#{entry.SelfPlace} · {Format.Compact(entry.SelfDps)}" : "";
    }
}
