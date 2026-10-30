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
using AionMeter.Core.Events;

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
    private string? _loaded;
    private (string Name, string Server) _profile;

    private void Profile_Click(object sender, RoutedEventArgs e) =>
        Armory.Open(_profile.Name, _profile.Server, ((App)Application.Current).Settings.Region);

    public LogsWindow(Meter meter)
    {
        _meter = meter;
        InitializeComponent();
        _meter.FightSaved += OnSaved;
        SkillIcons.Loaded += ShowRecord;
        Closed += (_, _) =>
        {
            _meter.FightSaved -= OnSaved;
            SkillIcons.Loaded -= ShowRecord;
        };
        SizeChanged += (_, _) => ShowRecord();
        LoadList();
    }

    private void OnSaved() => Dispatcher.BeginInvoke(LoadList);

    private void LoadList()
    {
        // A new fight is saved about every minute while farming: keep the list where the player scrolled to,
        // otherwise it jumps back to the top and older fights cannot be clicked.
        var scroll = FindScroll(List);
        var offset = scroll?.VerticalOffset ?? 0;
        var before = List.ItemsSource is List<FightItem> old ? old.Count : 0;
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
        if (scroll is not null && offset > 0)
        {
            var added = Math.Max(0, items.Count - before);
            Dispatcher.BeginInvoke(() => scroll.ScrollToVerticalOffset(offset + added), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private static System.Windows.Controls.ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is System.Windows.Controls.ScrollViewer sv) return sv;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    private void Item_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBoxItem { DataContext: FightItem item })
        {
            List.SelectedItem = item;
            e.Handled = true;
        }
    }

    private void List_Selected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is FightItem same && _record is not null && _loaded == same.Entry.FileName) return; // list refreshed, same fight
        _record = List.SelectedItem is FightItem item ? _meter.History.Load(item.Entry.Path) : null;
        _loaded = _record is null ? null : (List.SelectedItem as FightItem)?.Entry.FileName;
        Pick.Text = List.SelectedItem is FightItem && _record is null
            ? L.S("Dieser Kampf konnte nicht geöffnet werden (Datei fehlt oder ist beschädigt).", "This fight could not be opened (file missing or damaged).")
            : L.S("Wähle links einen Kampf.", "Pick a fight on the left.");
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

        var selfServer = s.Combatants.FirstOrDefault(c => c.IsSelf)?.ServerId ?? 0;
        var list = s.Combatants.OrderBy(c => c.IsUnknownSummons).ThenByDescending(c => c.Damage).ToList();
        var top = Math.Max(1, list.Count > 0 ? list.Max(c => c.Damage) : 1);
        var width = Math.Max(0, Rows.ActualWidth > 0 ? Rows.ActualWidth : 400);
        Rows.ItemsSource = list.Select((c, i) =>
        {
            var vm = new RowVm
            {
                ActorId = c.ActorId,
                Rank = c.IsUnknownSummons ? "" : (i + 1).ToString(),
                Name = c.IsUnknownSummons ? L.S("Beschwörungen", "Summons")
                    : c.Name + Armory.Tag(_meter.Data.ServerName(c.ServerId), _meter.Data.ServerName(selfServer)),
                Dps = Format.Compact(c.Dps),
                Share = Format.Share(c.Share),
                Power = c.CombatPower > 0 ? "  " + Format.Power(c.CombatPower) : "",
                Deaths = Overlay.DeathsText(c.Deaths),
                Stagger = Overlay.StaggerText(c.Stagger, s.TotalStagger),
                BarWidth = Math.Round(width * c.Damage / top),
                IsSelf = c.IsSelf,
                IsSelected = c.ActorId == _selected,
                RowHeight = 28,
            };
            vm.SetClass(c.Class);
            return vm;
        }).ToList();

        IdleText.Visibility = Visibility.Collapsed;
        DeathText.Visibility = Visibility.Collapsed;
        GroupButton.Foreground = (Brush)FindResource(_selected == GroupId ? "Accent" : "Text");
        BuffsButton.Foreground = (Brush)FindResource(_selected == BuffsId ? "Accent" : "Text");
        if (_selected == BuffsId)
        {
            ShowBuffs(r, width);
            return;
        }
        if (_selected == GroupId)
        {
            ShowGroupSkills(r, width);
            return;
        }
        ProfileButton.Visibility = Visibility.Visible;
        var d = r.Details.FirstOrDefault(x => x.ActorId == _selected);
        if (d is null)
        {
            SkillPanel.Visibility = Visibility.Collapsed;
            return;
        }
        SkillPanel.Visibility = Visibility.Visible;
        SkillTitle.Text = d.Name;
        _profile = (d.Name, _meter.Data.ServerName(d.ServerId));
        SkillStats.Text = L.S($"{Format.Compact(d.Damage)} Schaden · {Format.Compact(d.Dps)} DPS · Krit {Format.Percent(d.Quality.CritRate)} · ", $"{Format.Compact(d.Damage)} damage · {Format.Compact(d.Dps)} DPS · crit {Format.Percent(d.Quality.CritRate)} · ") +
                          $"Max {Format.Compact(d.Quality.MaxHit)}" + (d.Stagger > 0 ? $" · Stagger {d.Stagger}" : "");
        var idle = Insights.IdleText(d.Casts);
        IdleText.Text = idle;
        IdleText.Visibility = idle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var deaths = Insights.DeathLines(d.DeathRecaps);
        DeathText.Text = string.Join("\n", deaths);
        DeathText.Visibility = deaths.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var skills = d.Skills.OrderByDescending(x => x.Damage).ToList();
        var stop = Math.Max(1, skills.Count > 0 ? skills[0].Damage : 1);
        var swidth = Math.Max(0, Skills.ActualWidth > 0 ? Skills.ActualWidth : width);
        var brush = ClassStyle.Bar(d.Class, 0.22);
        Skills.ItemsSource = skills.Select(x => new SkillVm
        {
            Name = $"{x.Name}   ×{x.Hits + x.DotTicks}",
            Icon = SkillIcons.Get(x.SkillCode),
            Damage = Format.Compact(x.Damage),
            Share = Format.Share(x.Share),
            Stagger = Overlay.StaggerText(x.Stagger, d.Stagger),
            BarWidth = Math.Round(swidth * x.Damage / stop),
            BarBrush = brush,
        }).ToList();
    }

    /// <summary>"Player" id of the group view: every player's skills in one list.</summary>
    private const uint GroupId = 0xFFFF_FFFE;

    /// <summary>"Player" id of the buff view.</summary>
    private const uint BuffsId = 0xFFFF_FFFD;

    private void Buffs_Click(object sender, RoutedEventArgs e)
    {
        _selected = _selected == BuffsId ? _record?.Summary.Combatants.FirstOrDefault(c => c.IsSelf)?.ActorId : BuffsId;
        ShowRecord();
    }

    /// <summary>
    /// The fight's buffs and debuffs: bar and right column = uptime; middle = DPS with / without, and the difference.
    /// A buff compares the damage of the players who had it; a debuff on the boss compares the whole group's.
    /// </summary>
    private void ShowBuffs(FightRecord r, double width)
    {
        SkillPanel.Visibility = Visibility.Visible;
        ProfileButton.Visibility = Visibility.Collapsed;
        SkillTitle.Text = L.S("Buffs & Debuffs", "Buffs & debuffs");
        var buffs = r.Buffs ?? [];
        SkillStats.Text = r.Buffs is null
            ? L.S("Keine Buff-Daten: Kämpfe werden erst ab v1.29.0 mit Buffs gespeichert.", "No buff data: fights are saved with buffs from v1.29.0 on.")
            : buffs.Count == 0 ? L.S("In diesem Kampf wurden keine Buffs erkannt.", "No buffs were seen in this fight.")
            : L.S("Rechts: Uptime · Mitte: DPS mit / ohne", "Right: uptime · middle: DPS with / without");
        var swidth = Math.Max(0, Skills.ActualWidth > 0 ? Skills.ActualWidth : width);
        var dim = ClassStyle.Bar(GameClass.Unknown, 0.22);
        Skills.ItemsSource = buffs.Select(b =>
        {
            var who = b.OnTarget ? L.S("Debuff auf dem Boss", "debuff on the boss") : $"{b.Players}/{b.PlayersInFight}";
            var by = b.CasterName is { Length: > 0 } c && !b.OnTarget ? L.S($" · von {c}", $" · by {c}") : b.OnTarget && b.CasterName is { Length: > 0 } c2 ? L.S($" · von {c2}", $" · by {c2}") : "";
            var compare = b is { DpsWith: { } w, DpsWithout: { } wo } ? $"{Format.Compact(w)} / {Format.Compact(wo)}" : "–";
            var gain = b is { DpsWith: { } w2, DpsWithout: { } wo2 } && wo2 > 0 ? $"{(w2 / wo2 - 1) * 100:+0;-0;0}%" : "";
            return new SkillVm
            {
                Name = $"{b.Name}   {who}{by}",
                Icon = b.Icon is { } icon ? SkillIcons.ByName(icon) : null,
                Stagger = gain,
                Damage = compare,
                Share = Format.Share(b.Uptime),
                BarWidth = Math.Round(swidth * b.Uptime),
                BarBrush = b.OnTarget ? ClassStyle.Bar(GameClass.Unknown, 0.30) : dim,
            };
        }).ToList();
    }

    private void Group_Click(object sender, RoutedEventArgs e)
    {
        _selected = _selected == GroupId ? _record?.Summary.Combatants.FirstOrDefault(c => c.IsSelf)?.ActorId : GroupId;
        ShowRecord();
    }

    /// <summary>The fight's strongest skills over all players: which skill of whom carried the group.</summary>
    private void ShowGroupSkills(FightRecord r, double width)
    {
        var s = r.Summary;
        var total = Math.Max(1, r.Details.Where(d => d.ActorId != Combatant.UnknownSummonsId).Sum(d => d.Damage));
        var all = r.Details
            .Where(d => d.ActorId != Combatant.UnknownSummonsId)
            .SelectMany(d => d.Skills.Select(x => (Player: d, Skill: x)))
            .OrderByDescending(x => x.Skill.Damage)
            .Take(25)
            .ToList();
        SkillPanel.Visibility = Visibility.Visible;
        ProfileButton.Visibility = Visibility.Collapsed;
        SkillTitle.Text = L.S("Gruppe – stärkste Skills", "Group – strongest skills");
        var stagger = s.TotalStagger > 0 ? $" · Stagger {s.TotalStagger}" : "";
        SkillStats.Text = L.S($"{Format.Compact(total)} Schaden · {Format.Compact(s.PartyDps)} DPS", $"{Format.Compact(total)} damage · {Format.Compact(s.PartyDps)} DPS") + stagger;
        var stop = Math.Max(1, all.Count > 0 ? all[0].Skill.Damage : 1);
        var swidth = Math.Max(0, Skills.ActualWidth > 0 ? Skills.ActualWidth : width);
        Skills.ItemsSource = all.Select(x => new SkillVm
        {
            Name = $"{x.Skill.Name} · {x.Player.Name}   ×{x.Skill.Hits + x.Skill.DotTicks}",
            Icon = SkillIcons.Get(x.Skill.SkillCode),
            Damage = Format.Compact(x.Skill.Damage),
            Share = Format.Share((double)x.Skill.Damage / total),
            Stagger = Overlay.StaggerText(x.Skill.Stagger, s.TotalStagger),
            BarWidth = Math.Round(swidth * x.Skill.Damage / stop),
            BarBrush = ClassStyle.Bar(x.Player.Class, 0.22),
        }).ToList();
    }

    private void Rows_Click(object sender, MouseButtonEventArgs e)
    {
        if (RowHit.RowOf(e.OriginalSource) is not { } row) return;
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

    /// <summary>The fight as a picture for Discord: everyone by damage, and your idle time and deaths.</summary>
    private void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        if (_record is not { Summary: var s } r) return;
        var rows = s.Combatants.Where(c => !c.IsUnknownSummons).OrderByDescending(c => c.Damage).Take(10)
            .Select(c => new DiscordCard.Row(c.Name, c.Class, c.Dps, c.Share, c.Deaths, c.IsSelf)).ToList();
        var parts = new List<string>();
        if (s.Reason == AionMeter.Core.Combat.EncounterEndReason.Kill) parts.Add("Kill");
        else if (s.Reason == AionMeter.Core.Combat.EncounterEndReason.Wipe) parts.Add("Wipe");
        if (!string.IsNullOrEmpty(s.Zone) && s.Zone != s.Title) parts.Add(s.Zone!);
        parts.Add(L.S($"{rows.Count} Spieler", rows.Count == 1 ? "1 player" : $"{rows.Count} players"));
        parts.Add(L.S($"Gruppe {Format.Compact(s.PartyDps)} DPS", $"Party {Format.Compact(s.PartyDps)} DPS"));
        parts.Add(s.StartedAt.ToLocalTime().ToString("d", L.Culture));
        var notes = new List<string>();
        if (s.Combatants.FirstOrDefault(c => c.IsSelf) is { } me && r.Details.FirstOrDefault(x => x.ActorId == me.ActorId) is { } d)
        {
            if (Insights.IdleText(d.Casts) is { Length: > 0 } idle) notes.Add($"{me.Name}: {idle}");
            notes.AddRange(Insights.DeathLines(d.DeathRecaps, 2).Select(l => $"{me.Name}: {l}"));
        }
        var ok = DiscordCard.Copy(s.Title, string.Join(" · ", parts), Format.ClockShort(s.CombatMs), "DPS", rows, notes);
        // Feedback on the chip itself for two seconds.
        if (sender is System.Windows.Controls.Button { Content: System.Windows.Controls.StackPanel { Children: [_, System.Windows.Controls.TextBlock label] } })
        {
            label.Text = ok ? L.S("Kopiert ✓", "Copied ✓") : L.S("Nochmal versuchen", "Try again");
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (_, _) => { t.Stop(); label.Text = L.S("Bild", "Picture"); };
            t.Start();
        }
    }

    /// <summary>The fight as a page on the website; the link goes to the clipboard.</summary>
    private async void ShareLink_Click(object sender, RoutedEventArgs e)
    {
        if (_record is not { } r) return;
        var label = sender is System.Windows.Controls.Button { Content: System.Windows.Controls.StackPanel { Children: [_, System.Windows.Controls.TextBlock l] } } ? l : null;
        if (label is not null) label.Text = "…";
        var (url, error) = await ReportShare.Share(r, _meter.Data, _meter.Settings);
        var ok = false;
        if (url is not null) { try { Clipboard.SetText(url); ok = true; } catch { } }
        if (label is null) return;
        label.Text = ok ? L.S("Link kopiert ✓", "Link copied ✓") : error ?? L.S("Nochmal versuchen", "Try again");
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(ok ? 3 : 5) };
        t.Tick += (_, _) => { t.Stop(); label.Text = "Link"; };
        t.Start();
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
