using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AionMeter.Core;
using AionMeter.Core.Capture;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;

namespace PeakMeter;

public partial class Overlay : Window
{
    private const int MaxRows = 12;
    private const int MaxSkills = 8;

    private readonly App _app;
    private readonly Meter _meter;
    private readonly Settings _settings;
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<RowVm> _rows = new();
    private readonly ObservableCollection<SkillVm> _skills = new();
    private Guid? _segment;          // null = live
    private AionMeter.Core.Storage.HistoryEntry? _savedEntry;   // a saved fight (older than this session) shown via ◀ ▶
    private FightRecord? _savedRecord;
    private bool Browsing => _segment is not null || _savedRecord is not null;
    private uint? _selected;         // player whose skills are shown
    private EncounterSnapshot? _last;
    private long _lastTopmost;
    private long _lastIdleCheck = Environment.TickCount64;

    public Overlay(App app)
    {
        _app = app;
        _meter = app.Meter;
        _settings = app.Settings;
        InitializeComponent();
        Rows.ItemsSource = _rows;
        Skills.ItemsSource = _skills;

        Width = Math.Clamp(_settings.Width, MinWidth, MaxWidth);
        if (!double.IsNaN(_settings.Left) && !double.IsNaN(_settings.Top) && OnScreen(_settings.Left, _settings.Top))
        {
            Left = _settings.Left;
            Top = _settings.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Top + 140;
        }
        ApplyOpacity();
        ApplyScale();
        UpdateChips();

        MouseEnter += (_, _) => Toolbar.Visibility = Visibility.Visible;
        MouseLeave += (_, _) => { if (!OpacityPopup.IsOpen) Toolbar.Visibility = Visibility.Collapsed; };
        OpacityPopup.Closed += (_, _) => { if (!IsMouseOver) Toolbar.Visibility = Visibility.Collapsed; };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            Refresh();
            UpdateExtras(DateTimeOffset.UtcNow);
        };
        PartyList.ItemsSource = _party;
        // Joining a party (or its members changing) opens the party list with everyone's gear score and combat power.
        _meter.RosterChanged += () => Dispatcher.BeginInvoke(() =>
        {
            _partyAuto = _meter.Roster is not null;
            if (IsVisible) UpdateParty();
        });
        // Ctrl + mouse wheel anywhere on the meter: see-through ↔ solid.
        PreviewMouseWheel += (_, e) =>
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            e.Handled = true;
            _settings.Opacity = Math.Round(Math.Clamp(_settings.Opacity + (e.Delta > 0 ? 0.05 : -0.05), 0.2, 1), 2);
            ApplyOpacity();
            SubText.Text = L.S("Hintergrund", "Background") + $" {_settings.Opacity * 100:0}%";
            _settings.Save();
        };

        // Slow when nothing happens, 4× a second during a fight. Nothing is drawn while hidden.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1000) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        // Never steal focus from the game, never show up in Alt+Tab.
        Native.SetExStyle(hwnd, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW, true);
        ApplyClickThrough();
    }

    public void ApplyClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0) Native.SetExStyle(hwnd, Native.WS_EX_TRANSPARENT, _settings.ClickThrough);
    }

    // ------------------------------------------------------------------ loop

    private void OnTick()
    {
        _meter.Tick();
        var nowUtc = DateTimeOffset.UtcNow;
        _meter.Timers.Tick(nowUtc);
        if (_bannerUntil != 0 && Environment.TickCount64 > _bannerUntil)
        {
            _bannerUntil = 0;
            Banner.Visibility = Visibility.Collapsed;
        }
        var live = _meter.Tracker.LiveFight() is not null || _meter.DemoRunning;
        _app.CheckBossAlerts();
        if (_meter.Odyle.TakeFullAlert(DateTimeOffset.Now))
            _app.Notify(L.S("Odyle-Energie voll", "Odyle energy full"), L.S($"{_meter.Odyle.Cap} erreicht – Zeit zum Raiden!", $"{_meter.Odyle.Cap} reached – time to raid!"), "energy");
        if (live) _partyAuto = false; // a fight needs the room: the party list steps aside
        if (Environment.TickCount64 - _lastIdleCheck > 60_000)
        {
            _lastIdleCheck = Environment.TickCount64;
            _app.Updater?.InstallWhenIdle();
        }
        var wanted = TimeSpan.FromMilliseconds(live ? Math.Clamp(_settings.RefreshMs, 100, 1000) : 1000);
        if (_timer.Interval != wanted) _timer.Interval = wanted;
        if (live) _lastFight = Environment.TickCount64;
        ApplyVisibilityRules();
        if (!IsVisible) return;
        ApplyFade(live);

        Refresh();
        UpdateExtras(nowUtc);

        // Some games push themselves over topmost windows when they take focus: claim the top again now and then.
        var now = Environment.TickCount64;
        if (now - _lastTopmost > 5000)
        {
            _lastTopmost = now;
            Native.KeepOnTop(new WindowInteropHelper(this).Handle);
        }
    }

    private void Refresh()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var snap = _savedRecord?.Summary ?? _meter.Tracker.Snapshot(_segment, now);
        if (snap is null && _segment is not null)
        {
            _segment = null; // the fight fell out of memory: back to live
            snap = _meter.Tracker.Snapshot(null, now);
        }
        // A finished "fight" without any damage (a mob tagged for a second) must not replace the last real result.
        if (!Browsing && snap is { IsActive: false, TotalDamage: <= 0 }
            && _meter.Tracker.Segments().Where(s => !s.IsActive && s.TotalDamage > 0).OrderByDescending(s => s.StartedAt).FirstOrDefault() is { } real)
            snap = _meter.Tracker.Snapshot(real.Id, now) ?? snap;
        _last = snap;

        if (snap is null || snap.Combatants.Count == 0)
        {
            ShowEmpty();
            return;
        }

        var zone = _meter.Tracker.ZoneName is { Length: > 0 } z && snap.Boss is not null && !snap.Title.Contains(z) ? z : null;
        TitleText.Text = zone is null ? snap.Title : $"{zone}  ▸  {snap.Title}";
        var shown = Visible(snap);
        var players = shown.List.Count(c => !c.IsUnknownSummons);
        var tag = !Browsing ? (snap.IsActive ? "LIVE" : L.S("Ende", "End")) : L.S("Verlauf", "History");
        var who = shown.Filtered ? (_meter.Roster is null ? "Solo" : L.S("Gruppe", "Party")) : L.S("Alle", "All");
        SubText.Text = players <= 1 && shown.Filtered
            ? $"{tag} · {who} · {Format.Compact(shown.Dps)} DPS"
            : L.S($"{tag} · {who} {Format.Compact(shown.Dps)} DPS · {players} Spieler", $"{tag} · {who} {Format.Compact(shown.Dps)} DPS · {players} players");
        // aDPS counts from your own first hit, so the clock does too (joining a running fight starts at 0:00)
        var mine = snap.Combatants.FirstOrDefault(c => c.IsSelf);
        ClockText.Text = Format.ClockShort(_settings.DpsMode == "a" && mine is { ActiveMs: > 0 } ? mine.ActiveMs : snap.CombatMs);
        ClockText.Foreground = snap.IsActive ? (Brush)FindResource("Text") : (Brush)FindResource("TextDim");

        if (snap.Boss is { } boss)
        {
            HpPanel.Visibility = Visibility.Visible;
            var f = boss.HpFraction;
            HpFill.Width = Math.Max(0, HpTrack.ActualWidth * f);
            HpText.Text = boss.Hp <= 0 ? L.S("tot", "dead") : (f * 100).ToString(f < 0.1 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + "%";
            HpAmount.Text = boss.MaxHpKnown && boss.MaxHp > 0 ? $"{Format.Compact(Math.Max(0, boss.Hp))} / {Format.Compact(boss.MaxHp)} HP" : "";
            KillText.Text = KillEstimate(snap, boss);
        }
        else
        {
            HpPanel.Visibility = Visibility.Collapsed;
            _hpSamples.Clear();
        }

        _showingEmpty = false;
        EmptyText.Visibility = Visibility.Collapsed;
        var focus = _settings.FocusMode && snap.IsActive && !IsMouseOver;
        Header.Visibility = focus ? Visibility.Collapsed : Visibility.Visible;
        UpdateCompare(snap);
        UpdateRows(snap);
        UpdateDetail();
    }

    private readonly Queue<(long Ms, long Hp)> _hpSamples = new();
    private int _hpBoss;

    /// <summary>
    /// "Kill in ~1:23": the boss's HP left divided by how fast it dropped over the last 20 s (or the whole fight
    /// when shorter) – the current pace, so phases and burst windows are reflected quickly.
    /// </summary>
    private string KillEstimate(EncounterSnapshot snap, BossSnapshot boss)
    {
        if (boss.Hp <= 0) return L.S($"Gelegt in {Format.ClockShort(snap.CombatMs)}", $"Down in {Format.ClockShort(snap.CombatMs)}");
        if (!snap.IsActive || Browsing || !boss.MaxHpKnown) { _hpSamples.Clear(); return ""; }
        if (_hpBoss != boss.NpcCode || (_hpSamples.Count > 0 && snap.CombatMs < _hpSamples.Last().Ms)) { _hpSamples.Clear(); _hpBoss = boss.NpcCode; }
        if (_hpSamples.Count == 0 || _hpSamples.Last().Ms != snap.CombatMs) _hpSamples.Enqueue((snap.CombatMs, boss.Hp));
        while (_hpSamples.Count > 2 && snap.CombatMs - _hpSamples.Peek().Ms > 20_000) _hpSamples.Dequeue();
        var (ms0, hp0) = _hpSamples.Peek();
        var span = snap.CombatMs - ms0;
        var lost = hp0 - boss.Hp;
        if (span < 3_000 || lost <= 0) return L.S("Kill in …", "Kill in …");
        var leftMs = boss.Hp / (lost / (double)span);
        if (leftMs > 60 * 60_000) return L.S("Kill in > 1 Std.", "Kill in > 1 h");
        return L.S($"Kill in ~{Format.ClockShort((long)leftMs)}", $"Kill in ~{Format.ClockShort((long)leftMs)}");
    }

    /// <summary>In a boss fight: your DPS against last time, and the best kill time so far.</summary>
    private void UpdateCompare(EncounterSnapshot snap)
    {
        var code = snap.Boss?.NpcCode ?? 0;
        var rec = code != 0 ? _meter.Records.For(code, snap.StartedAt) : null;
        var self = snap.Combatants.FirstOrDefault(c => c.IsSelf);
        if (rec is not { } r || (r.LastSelfDps <= 0 && r.BestKillMs <= 0))
        {
            ComparePanel.Visibility = Visibility.Collapsed;
            return;
        }
        ComparePanel.Visibility = Visibility.Visible;
        if (self is not null && r.LastSelfDps > 0 && snap.CombatMs > 5000)
        {
            var diff = self.Dps / r.LastSelfDps - 1;
            var up = diff >= 0;
            CompareArrow.Text = up ? "\u25B2" : "\u25BC";
            CompareArrow.Foreground = up ? Up : Down;
            CompareText.Text = L.S($"{Math.Abs(diff) * 100:0}% {(up ? "besser" : "weniger")} als letztes Mal ({Format.Compact(r.LastSelfDps)})",
                $"{Math.Abs(diff) * 100:0}% {(up ? "better" : "lower")} than last time ({Format.Compact(r.LastSelfDps)})");
        }
        else
        {
            CompareArrow.Text = "";
            CompareText.Text = r.LastSelfDps > 0 ? L.S($"Letztes Mal: {Format.Compact(r.LastSelfDps)} DPS", $"Last time: {Format.Compact(r.LastSelfDps)} DPS") : "";
        }
        BestText.Text = r.BestKillMs > 0 ? L.S("Bestzeit", "Best") + $" {Format.ClockShort(r.BestKillMs)}" : "";
    }

    private static readonly Brush Up = Frozen(Color.FromRgb(0x3F, 0xD1, 0x8B));
    private static readonly Brush Down = Frozen(Color.FromRgb(0xFF, 0x6B, 0x81));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private void View_Click(object sender, RoutedEventArgs e)
    {
        _settings.View = _settings.View switch { "dps" => "heal", "heal" => "tank", _ => "dps" };
        _settings.Save();
        UpdateChips();
        Refresh();
    }

    private void ShowEmpty()
    {
        _showingEmpty = true;
        Header.Visibility = Visibility.Visible;
        ComparePanel.Visibility = Visibility.Collapsed;
        _rows.Clear();
        HpPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Visible;
        TitleText.Text = _meter.Tracker.SelfName ?? "PeakMeter";
        ClockText.Text = "0:00";
        ClockText.Foreground = (Brush)FindResource("TextDim");

        var s = _meter.Status;
        (SubText.Text, EmptyText.Text) = s.State switch
        {
            CaptureState.Error when s.Message == "Npcap fehlt" =>
                ("Npcap fehlt", L.S("Npcap ist nicht installiert.\nLade es kostenlos von npcap.com und starte PeakMeter neu.", "Npcap is not installed.\nGet it free from npcap.com and restart PeakMeter.")),
            CaptureState.Error => (L.S("Fehler", "Error"), s.Message),
            CaptureState.WaitingForGame when s.Message.StartsWith("AION 2 is not running") =>
                (L.S("Warte auf Aion 2 …", "Waiting for AION 2 …"), L.S("Starte Aion 2 – PeakMeter verbindet sich von selbst.", "Start AION 2 – PeakMeter connects by itself.")),
            CaptureState.WaitingForGame when s.Message.StartsWith("Waiting for the game") =>
                (L.S("Aion 2 gefunden", "AION 2 found"), L.S("Warte, bis das Spiel mit dem Server verbunden ist …", "Waiting for the game to connect …")),
            CaptureState.WaitingForGame or CaptureState.Starting =>
                (L.S("Verbunden", "Connected"), L.S("Bereit. Greif etwas an, dann geht's los.", "Ready. Hit something to start.")),
            CaptureState.Capturing => (L.S("Verbunden ✓", "Connected ✓"), L.S("Bereit. Greif etwas an, dann geht's los.", "Ready. Hit something to start.")),
            _ => (L.S("Bereit", "Ready"), L.S("Warte auf den ersten Treffer …", "Waiting for the first hit …")),
        };
    }

    /// <summary>
    /// Who the meter shows: in a party only its members (like the game's own party frames), solo only yourself —
    /// strangers fighting the same mob in the open world stay out. Shares are then of what is shown, so a party adds up
    /// to 100 %. Until your character is known (right after starting mid-zone) everyone is shown.
    /// </summary>
    /// <summary>The number a row is about in the current view: damage, healing or damage taken.</summary>
    private long Metric(CombatantSnapshot c) => _settings.View switch
    {
        "heal" => c.Healing,
        "tank" => c.DamageTaken,
        _ => c.Damage,
    };

    private (List<CombatantSnapshot> List, Dictionary<uint, double> Share, double Dps, bool Filtered) Visible(EncounterSnapshot snap)
    {
        IEnumerable<CombatantSnapshot> pick = snap.Combatants;
        var filtered = false;
        if (_settings.PartyOnly)
        {
            var roster = _meter.Roster;
            var self = _meter.Tracker.SelfName;
            // Raids and dungeons: the game sends no (or only your own) party list. Inside an instance everyone
            // fighting is your group, so they stay visible. In the open world strangers on a field boss are not.
            var group = Browsing ? _meter.Data.IsDungeonZone(snap.Zone) : _meter.Tracker.InInstance;
            if (roster is not null)
            {
                var names = roster.Members.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var mates = snap.Combatants.Count(c => !c.IsSelf && !c.IsUnknownSummons && names.Contains(c.Name));
                var others = snap.Combatants.Count(c => !c.IsSelf && !c.IsUnknownSummons);
                if (!group || mates >= others || mates >= 3)
                    pick = pick.Where(c => c.IsSelf || (!c.IsUnknownSummons && names.Contains(c.Name)));
                filtered = true;
            }
            else if (!group && (self is not null || snap.Combatants.Any(c => c.IsSelf)))
            {
                pick = pick.Where(c => c.IsSelf);
                filtered = true;
            }
        }
        if (_settings.View != "dps") pick = pick.Where(c => Metric(c) > 0 && !c.IsUnknownSummons);
        var list = pick.OrderBy(c => c.IsUnknownSummons).ThenByDescending(Metric).Take(MaxRows).ToList();
        var total = list.Sum(c => (double)Metric(c));
        var mine = filtered || _settings.View != "dps";
        var share = list.ToDictionary(c => c.ActorId, c => mine ? (total > 0 ? Metric(c) / total : 0) : c.Share);
        return (list, share, filtered || _settings.DpsMode == "a" ? list.Sum(DpsOf) : snap.PartyDps, filtered);
    }

    /// <summary>eDPS (whole fight) or aDPS (the player's own active time), per the settings.</summary>
    private double DpsOf(CombatantSnapshot c) => _settings.DpsMode == "a" ? c.ActiveDps : c.Dps;

    /// <summary>Others by class name when "hide player names" is on (for streams); you keep yours.</summary>
    private string NameOf(CombatantSnapshot c) =>
        c.IsUnknownSummons ? L.S("Beschwörungen", "Summons")
        : _settings.HideNames && !c.IsSelf ? ClassStyle.Name(c.Class)
        : c.Name + Armory.Tag(ServerOf(c.Name, c.ServerId), _meter.Data.ServerName(_meter.Tracker.SelfServerId));

    /// <summary>The game sends other players' servers only in the party list: use it when the player has none.</summary>
    private string ServerOf(string name, int serverId)
    {
        if (serverId == 0)
            serverId = _meter.Roster?.Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))?.ServerId ?? 0;
        return _meter.Data.ServerName(serverId);
    }

    private string PowerOf(CombatantSnapshot c)
    {
        if (_settings.ValueChip == "none" || c.IsUnknownSummons) return "";
        var member = _meter.Roster?.Members.FirstOrDefault(m => string.Equals(m.Name, c.Name, StringComparison.OrdinalIgnoreCase));
        if (_settings.ValueChip == "gs")
        {
            var gs = c.GearScore > 0 ? c.GearScore : member?.GearScore ?? 0;
            return gs > 0 ? "  " + gs.ToString("N0", L.Culture) : "";
        }
        var cp = c.CombatPower > 0 ? c.CombatPower : member?.CombatPower ?? 0;
        return cp > 0 ? "  " + Format.Power(cp) : "";
    }

    private void UpdateRows(EncounterSnapshot snap)
    {
        var (list, shares, _, _) = Visible(snap);
        var top = list.Count > 0 ? Math.Max(1, list.Max(Metric)) : 1;
        var seconds = Math.Max(1, snap.CombatMs / 1000.0);
        var width = Math.Max(0, Rows.ActualWidth > 0 ? Rows.ActualWidth : Width - 22);
        var rowHeight = _settings.Compact ? 22 : 26;

        while (_rows.Count > list.Count) _rows.RemoveAt(_rows.Count - 1);
        while (_rows.Count < list.Count) _rows.Add(new RowVm());

        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            var r = _rows[i];
            r.ActorId = c.ActorId;
            r.RowHeight = rowHeight;
            r.Rank = c.IsUnknownSummons ? "" : (i + 1).ToString();
            r.Name = NameOf(c);
            r.Dps = _settings.View switch
            {
                "heal" => Format.Compact(c.Healing / seconds),
                "tank" => Format.Compact(c.DamageTaken),
                _ => _settings.ShowTotal ? $"{Format.Compact(c.Damage)} · {Format.Compact(DpsOf(c))}" : Format.Compact(DpsOf(c)),
            };
            r.Share = Format.Share(shares[c.ActorId]);
            r.Power = PowerOf(c);
            r.BarWidth = Math.Round(width * Metric(c) / top);
            r.SetClass(c.Class);
            r.IsSelf = c.IsSelf;
            r.IsSelected = _selected == c.ActorId;
        }
    }

    private (string Name, string Server) _profile;

    private void Profile_Click(object sender, RoutedEventArgs e) => Armory.Open(_profile.Name, _profile.Server, _settings.Region);

    private void UpdatePing()
    {
        if (_meter.PingMs is not { } ms)
        {
            PingPanel.Visibility = Visibility.Collapsed;
            return;
        }
        PingPanel.Visibility = Visibility.Visible;
        PingText.Text = $"{Math.Round(ms)}ms";
        var c = ms < 60 ? Color.FromRgb(0x3F, 0xD1, 0x8B) : ms < 120 ? Color.FromRgb(0xFF, 0xB0, 0x70) : Color.FromRgb(0xFF, 0x4D, 0x6D);
        var brush = new SolidColorBrush(c);
        PingText.Foreground = brush;
        PingIcon.Foreground = brush;
    }

    private void UpdateDetail()
    {
        var d = _selected is not { } id ? null
            : _savedRecord is { } rec ? rec.Details.FirstOrDefault(x => x.ActorId == id)
            : _meter.Tracker.Detail(_segment, id);
        if (d is null)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }
        DetailPanel.Visibility = Visibility.Visible;
        DetailTitle.Text = d.Name;
        _profile = (d.Name, ServerOf(d.Name, d.ServerId));
        DetailStats.Text = $"{Format.Compact(d.Damage)} · {L.S("Krit", "Crit")} {Format.Percent(d.Quality.CritRate)} · Max {Format.Compact(d.Quality.MaxHit)}";

        var skills = d.Skills.OrderByDescending(s => s.Damage).Take(MaxSkills).ToList();
        var top = skills.Count > 0 ? Math.Max(1, skills[0].Damage) : 1;
        var width = Math.Max(0, Skills.ActualWidth > 0 ? Skills.ActualWidth : Width - 22);
        var brush = ClassStyle.Bar(d.Class, 0.22);

        while (_skills.Count > skills.Count) _skills.RemoveAt(_skills.Count - 1);
        while (_skills.Count < skills.Count) _skills.Add(new SkillVm());
        for (var i = 0; i < skills.Count; i++)
        {
            var s = skills[i];
            var vm = _skills[i];
            vm.Name = s.Name;
            vm.Damage = Format.Compact(s.Damage);
            vm.Share = Format.Share(s.Share);
            vm.BarWidth = Math.Round(width * s.Damage / top);
            vm.BarBrush = brush;
        }
    }

    // ------------------------------------------------------------------ overlay behaviour (settings)

    private long _lastFight = Environment.TickCount64;
    private bool _hiddenByRule;
    private long _lastRuleCheck;

    /// <summary>"Only over the game" and "hide while the game is closed": the meter steps aside by itself.</summary>
    private void ApplyVisibilityRules()
    {
        if (!_settings.OnlyOverGame && !_settings.HideWhenGameClosed)
        {
            if (_hiddenByRule) { _hiddenByRule = false; if (_settings.Visible) Show(); }
            return;
        }
        var now = Environment.TickCount64;
        if (now - _lastRuleCheck < 1000) return; // a second is plenty, and keeps it cheap
        _lastRuleCheck = now;
        var hide = false;
        if (_settings.HideWhenGameClosed && !Native.GameRunning()) hide = true;
        if (!hide && _settings.OnlyOverGame && !Native.GameOrMeterInFront()) hide = true;
        if (hide && IsVisible) { _hiddenByRule = true; Hide(); }
        else if (!hide && _hiddenByRule) { _hiddenByRule = false; if (_settings.Visible) Show(); }
    }

    /// <summary>"Fade when idle": after a while without a fight the meter turns faint until the next one or the mouse.</summary>
    private void ApplyFade(bool live)
    {
        var faint = _settings.FadeIdle && !live && !IsMouseOver &&
                    Environment.TickCount64 - _lastFight > Math.Max(10, _settings.FadeAfterSec) * 1000L;
        var target = faint ? 0.25 : 1.0;
        if (Math.Abs(Root.Opacity - target) > 0.01) Root.Opacity = target;
    }

    // ------------------------------------------------------------------ personal: class, party, energy

    private readonly ObservableCollection<PartyVm> _party = new();
    private GameClass? _selfClass;

    private static readonly ImageSource? Logo = LoadLogo();

    private static ImageSource? LoadLogo()
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/Assets/logo.png");
            bmp.DecodePixelWidth = 64;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
    private bool _partyAuto, _partyPinned, _showingEmpty;


    private void UpdateExtras(DateTimeOffset nowUtc)
    {
        UpdateNextEvent(nowUtc);
        UpdateSelf();
        UpdateParty();
        UpdateEnergy();
    }

    /// <summary>Own class emblem in a ring of the class colour next to the title, and a matching glow on top.</summary>
    private void UpdateSelf()
    {
        var c = _meter.Tracker.SelfClass;
        if (c == _selfClass) return;
        _selfClass = c;
        if (c == GameClass.Unknown)
        {
            // Not logged in yet (or not seen): the PeakMeter logo instead of a class emblem.
            SelfIcon.Source = Logo;
            SelfRing.Stroke = null;
            SelfRing.Fill = null;
            SelfIcon.Width = SelfIcon.Height = 28;
            SelfBadge.Visibility = Visibility.Visible;
            ClassGlow.Visibility = Visibility.Collapsed;
            return;
        }
        SelfIcon.Width = SelfIcon.Height = 20;
        var color = ClassStyle.ColorOf(c);
        SelfIcon.Source = ClassStyle.Icon(c);
        SelfRing.Stroke = ClassStyle.Solid(c);
        SelfRing.Fill = new RadialGradientBrush(Color.FromArgb(0x55, color.R, color.G, color.B), Color.FromArgb(0x10, color.R, color.G, color.B));
        SelfBadge.Visibility = Visibility.Visible;
        var glow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0xE0, color.R, color.G, color.B), 0.5));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        glow.Freeze();
        ClassGlow.Background = glow;
        ClassGlow.Visibility = Visibility.Visible;
    }

    private void UpdateParty()
    {
        var roster = _meter.Roster;
        var show = _partyPinned || (roster is not null && (_partyAuto || _showingEmpty));
        PartyPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PartyButton.Foreground = (Brush)FindResource(_partyPinned ? "Accent" : "TextDim");
        if (!show) return;

        if (roster is null)
        {
            _party.Clear();
            PartyTitle.Text = L.S("Gruppe", "Party");
            PartySummary.Text = L.S("Keine Gruppe. Sobald du einer beitrittst, siehst du hier Gearscore und Combat Power aller Mitglieder.", "No party. Once you join one, you see every member’s gear score and combat power here.");
            PartySummary.TextWrapping = TextWrapping.Wrap;
            return;
        }

        var self = _meter.Tracker.SelfName;
        var members = roster.Members.OrderByDescending(m => m.CombatPower).ToList();
        while (_party.Count > members.Count) _party.RemoveAt(_party.Count - 1);
        while (_party.Count < members.Count) _party.Add(new PartyVm());
        for (var i = 0; i < members.Count; i++)
        {
            var m = members[i];
            var vm = _party[i];
            vm.Name = m.Name;
            vm.Level = m.Level > 0 ? $"Lv {m.Level}" : "";
            vm.Gear = m.GearScore > 0 ? m.GearScore.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) : "–";
            vm.Power = m.CombatPower > 0 ? Format.Power(m.CombatPower) : "–";
            vm.IsSelf = self is not null && string.Equals(m.Name, self, StringComparison.Ordinal);
            vm.SetClass(m.Class);
        }
        PartyTitle.Text = L.S("Gruppe", "Party") + $" · {members.Count}";
        var gs = members.Where(m => m.GearScore > 0).Select(m => (double)m.GearScore).DefaultIfEmpty().Average();
        var cp = members.Where(m => m.CombatPower > 0).Select(m => (double)m.CombatPower).DefaultIfEmpty().Average();
        PartySummary.TextWrapping = TextWrapping.NoWrap;
        PartySummary.Text = gs > 0 ? $"Ø GS {gs:0} · Ø CP {Format.Power((long)cp)}" : "";
    }

    private void Party_Click(object sender, RoutedEventArgs e)
    {
        _partyPinned = !_partyPinned;
        if (!_partyPinned) _partyAuto = false;
        UpdateParty();
    }

    private void UpdateEnergy()
    {
        UpdatePing();
        var o = _meter.Odyle;
        var now = DateTimeOffset.Now;
        if (!o.Known)
        {
            EnergyText.Text = L.S("Energie", "Energy");
            EnergyButton.Foreground = (Brush)FindResource("TextDim");
            return;
        }
        // like the game: "135 (+755)" = regenerating pool and bonus
        EnergyText.Text = o.Bonus > 0 ? $"{o.Pool(now)} (+{o.Bonus})" : o.Pool(now).ToString();
        EnergyButton.ToolTip = (o.Bonus > 0 ? L.S("Odyle-Energie", "Odyle energy") + $": {o.Pool(now)}/{o.Cap} + {o.Bonus} Bonus" : L.S("Odyle-Energie", "Odyle energy") + $": {o.Pool(now)}/{o.Cap}")
                               + (o.FromGameAt is not null ? L.S(" · vom Spiel erkannt", " · read from the game") : L.S(" · von Hand", " · entered by hand"));
        var near = o.FullAt(now) is not { } full || full - now < TimeSpan.FromHours(3);
        EnergyButton.Foreground = (Brush)FindResource(near ? "Accent" : "TextDim");
    }

    private void Energy_Click(object sender, RoutedEventArgs e) => _app.ShowEnergy();

    // ------------------------------------------------------------------ events

    private long _bannerUntil;
    private bool _nextSoon;

    /// <summary>"2 h 05", "12 Min", "45 s": seconds only in the last minute.</summary>
    private static string Coarse(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes:00}";
        if (t.TotalMinutes >= 1) return L.S($"{(int)Math.Ceiling(t.TotalMinutes)} Min", $"{(int)Math.Ceiling(t.TotalMinutes)} min");
        return $"{(int)Math.Ceiling(t.TotalSeconds)} s";
    }

    /// <summary>The next event with the bell on (or simply the next one) in the footer.</summary>
    private void UpdateNextEvent(DateTimeOffset now)
    {
        var all = _meter.Timers.Upcoming(now).Where(e => e.Start != DateTimeOffset.MaxValue).ToList();
        var next = all.FirstOrDefault(e => e.Def.Alert) ?? all.FirstOrDefault();
        if (next is null)
        {
            NextText.Text = L.S("Event-Timer", "Event timers");
            return;
        }
        var running = next.Running(now);
        var text = running
            ? L.S($"{next.Def.Name} läuft · noch {Coarse(next.End - now)}", $"{next.Def.Name} running · {Coarse(next.End - now)} left")
            : $"{next.Def.Name} in {Coarse(next.Start - now)}";
        // Only touch the text when it really changes: every redraw costs CPU, and minutes change rarely.
        if (NextText.Text != text) NextText.Text = text;
        var soon = running || next.Start - now <= TimeSpan.FromMinutes(5);
        if (soon != _nextSoon || NextText.Foreground is null)
        {
            _nextSoon = soon;
            var brush = (Brush)FindResource(soon ? "Accent" : "TextDim");
            NextText.Foreground = brush;
            NextIcon.Foreground = brush;
        }
    }

    private Action? _bannerAction;

    /// <summary>A banner that does something when clicked (e.g. install an update); stays for a minute.</summary>
    public void ShowAction(string text, Action action)
    {
        ShowAlert(text);
        _bannerAction = action;
        _bannerUntil = Environment.TickCount64 + 60_000;
    }

    /// <summary>A short banner in the meter when an event is about to start.</summary>
    public void ShowAlert(string text)
    {
        _bannerAction = null;
        BannerText.Text = text;
        Banner.Visibility = Visibility.Visible;
        _bannerUntil = Environment.TickCount64 + 15_000;
    }

    private void Banner_Click(object sender, MouseButtonEventArgs e)
    {
        var action = _bannerAction;
        _bannerAction = null;
        action?.Invoke();
        _bannerUntil = 0;
        Banner.Visibility = Visibility.Collapsed;
    }

    private void Logs_Click(object sender, RoutedEventArgs e) => _app.ShowLogs();
    private void Tickets_Click(object sender, RoutedEventArgs e) => _app.ShowTickets();
    private void Settings_Click(object sender, RoutedEventArgs e) => _app.ShowSettings();

    public void ApplySettings()
    {
        ApplyScale();
        _meter.Tracker.Options.IdleTimeoutMs = Math.Clamp(_settings.IdleTimeoutSec, 3, 120) * 1000;
        ApplyOpacity();
        UpdateChips();
        Refresh();
    }
    private void Timers_Click(object sender, RoutedEventArgs e) => _app.ShowTimers();

    // ------------------------------------------------------------------ interaction

    private void Rows_Click(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not RowVm row) return;
        _selected = _selected == row.ActorId ? null : row.ActorId;
        Refresh();
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.Locked || e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { }
        SavePlacement();
    }

    private void Header_Wheel(object sender, MouseWheelEventArgs e) => Step(e.Delta < 0 ? +1 : -1);
    private void Older_Click(object sender, RoutedEventArgs e) => Step(+1);
    public void StepBack() => Step(+1);
    private void Newer_Click(object sender, RoutedEventArgs e) => Step(-1);

    /// <summary>+1 = an older fight, −1 = a newer one; reaching the newest returns to live.</summary>
    /// <remarks>
    /// The fights of this session come from memory; older ones from the saved logs, so the history still works
    /// right after PeakMeter restarted (e.g. after an update).
    /// </remarks>
    private void Step(int direction)
    {
        var live = _meter.Tracker.Segments().Where(s => s.IsActive || s.TotalDamage > 0).OrderByDescending(s => s.StartedAt).ToList();
        var oldest = live.Count > 0 ? live[^1].StartedAt.AddSeconds(-2) : DateTimeOffset.MaxValue;
        var fights = live.Select(s => (Seg: (Guid?)s.Id, Saved: (AionMeter.Core.Storage.HistoryEntry?)null))
            .Concat(_meter.History.List(200).Where(e => e.StartedAt < oldest).Select(e => (Seg: (Guid?)null, Saved: (AionMeter.Core.Storage.HistoryEntry?)e)))
            .ToList();
        if (live.Count == 0) fights.Insert(0, (null, null)); // position 0 is always "live"
        if (fights.Count <= 1) return;
        var index = _segment is { } id ? fights.FindIndex(f => f.Seg == id)
                  : _savedEntry is { } cur ? fights.FindIndex(f => f.Saved?.FileName == cur.FileName)
                  : 0;
        if (index < 0) index = 0;
        index = Math.Clamp(index + direction, 0, fights.Count - 1);
        var pick = fights[index];
        _segment = index == 0 ? null : pick.Seg;
        _savedEntry = index == 0 ? null : pick.Saved;
        _savedRecord = _savedEntry is { } entry ? _meter.History.Load(entry.Path) : null;
        if (_savedEntry is not null && _savedRecord is null) _savedEntry = null; // file gone: stay on live
        _selected = null;
        Refresh();
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        _meter.SetBossOnly(!_settings.BossOnly);
        UpdateChips();
        Refresh();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopySummary();

    /// <summary>The ranking for the chat: one line, or one line per player (settings).</summary>
    public void CopySummary()
    {
        if (_last is not { } s || s.Combatants.Count == 0) return;
        var (list, shares, _, _) = Visible(s);
        var head = $"{s.Title} ({Format.ClockShort(s.CombatMs)})";
        var lines = list.Where(c => !c.IsUnknownSummons).Take(8)
            .Select((c, i) => $"{i + 1}. {NameOf(c)} {Format.Compact(DpsOf(c))} ({Format.Share(shares[c.ActorId])})").ToList();
        var text = _settings.SummaryMultiline
            ? head + Environment.NewLine + string.Join(Environment.NewLine, lines)
            : head + " | " + string.Join(" | ", lines);
        try { Clipboard.SetText(text); } catch { }
        SubText.Text = L.S("In die Zwischenablage kopiert ✓", "Copied to clipboard ✓");
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _meter.Reset();
        _segment = null;
        _savedEntry = null;
        _savedRecord = null;
        _selected = null;
        Refresh();
    }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        _settings.Locked = !_settings.Locked;
        UpdateChips();
        _settings.Save();
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => _app.SetVisible(false);

    private void Grip_Drag(object sender, DragDeltaEventArgs e)
    {
        if (_settings.Locked) return;
        Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, MaxWidth);
    }

    private void Grip_Done(object sender, DragCompletedEventArgs e)
    {
        SavePlacement();
        Refresh();
    }

    private void UpdateChips()
    {
        ModeChip.Content = _settings.BossOnly ? "BOSS" : L.S("ALLE", "ALL");
        ViewChip.Content = _settings.View switch { "heal" => "HEAL", "tank" => "TANK", _ => "DPS" };
        LockButton.Content = _settings.Locked ? "" : "";
        LockButton.ToolTip = _settings.Locked ? L.S("Position entsperren", "Unlock position") : L.S("Position sperren", "Lock position");
        Grip.IsHitTestVisible = !_settings.Locked;
    }

    /// <summary>Only the panel's background fades: text, bars and icons stay fully readable.</summary>
    private void ApplyScale()
    {
        var k = Math.Clamp(_settings.UiScale, 0.7, 1.6);
        Root.LayoutTransform = Math.Abs(k - 1) < 0.01 ? Transform.Identity : new ScaleTransform(k, k);
    }

    private void ApplyOpacity()
    {
        var o = Math.Clamp(_settings.Opacity, 0.2, 1);
        var bg = new SolidColorBrush(Color.FromArgb((byte)Math.Round(o * 255), 0x0D, 0x0F, 0x15));
        bg.Freeze();
        Root.Background = bg;
        Root.Opacity = 1;
        if (OpacityValue is not null) OpacityValue.Text = $"{o * 100:0}%";
    }

    private void Opacity_Click(object sender, RoutedEventArgs e)
    {
        OpacitySlider.Value = Math.Round(_settings.Opacity * 100);
        OpacityValue.Text = $"{_settings.Opacity * 100:0}%";
        OpacityPopup.IsOpen = !OpacityPopup.IsOpen;
    }

    private void OpacitySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        _settings.Opacity = Math.Round(e.NewValue / 100, 2);
        ApplyOpacity();
        _settings.Save();
    }

    public void SavePlacement()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Width = Width;
        _settings.Save();
    }

    private static bool OnScreen(double left, double top) =>
        left > SystemParameters.VirtualScreenLeft - 50 && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 &&
        top > SystemParameters.VirtualScreenTop - 10 && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
}

// ---------------------------------------------------------------------- view models

public abstract class Vm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Only raises when the value really changed, so an unchanged row costs no layout work.</summary>
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class RowVm : Vm
{
    private string _rank = "", _name = "", _dps = "", _share = "";
    private double _bar, _height = 26;
    private bool _self, _selected;
    private GameClass? _class;
    private Brush? _classBrush, _barBrush;
    private ImageSource? _icon;

    public uint ActorId { get; set; }
    public string Rank { get => _rank; set => Set(ref _rank, value); }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Dps { get => _dps; set => Set(ref _dps, value); }
    public string Share { get => _share; set => Set(ref _share, value); }
    private string _power = "";
    /// <summary>"  129.7K" after the name (combat power), empty when unknown.</summary>
    public string Power { get => _power; set => Set(ref _power, value); }
    public double BarWidth { get => _bar; set => Set(ref _bar, value); }
    public double RowHeight { get => _height; set => Set(ref _height, value); }
    public bool IsSelf { get => _self; set => Set(ref _self, value); }
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
    public Brush? ClassBrush { get => _classBrush; private set => Set(ref _classBrush, value); }
    public Brush? BarBrush { get => _barBrush; private set => Set(ref _barBrush, value); }
    public ImageSource? Icon { get => _icon; private set => Set(ref _icon, value); }

    public void SetClass(GameClass c)
    {
        if (_class == c) return;
        _class = c;
        ClassBrush = ClassStyle.Solid(c);
        BarBrush = ClassStyle.Bar(c, 0.30);
        Icon = ClassStyle.Icon(c);
    }
}

public sealed class PartyVm : Vm
{
    private string _name = "", _level = "", _gear = "", _power = "";
    private bool _self;
    private GameClass? _class;
    private Brush? _brush;
    private ImageSource? _icon;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Level { get => _level; set => Set(ref _level, value); }
    public string Gear { get => _gear; set => Set(ref _gear, value); }
    public string Power { get => _power; set => Set(ref _power, value); }
    public bool IsSelf { get => _self; set => Set(ref _self, value); }
    public Brush? ClassBrush { get => _brush; private set => Set(ref _brush, value); }
    public ImageSource? Icon { get => _icon; private set => Set(ref _icon, value); }

    public void SetClass(GameClass c)
    {
        if (_class == c) return;
        _class = c;
        ClassBrush = ClassStyle.Solid(c);
        Icon = ClassStyle.Icon(c);
    }
}

public sealed class SkillVm : Vm
{
    private string _name = "", _damage = "", _share = "";
    private double _bar;
    private Brush? _brush;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Damage { get => _damage; set => Set(ref _damage, value); }
    public string Share { get => _share; set => Set(ref _share, value); }
    public double BarWidth { get => _bar; set => Set(ref _bar, value); }
    public Brush? BarBrush { get => _brush; set => Set(ref _brush, value); }
}

/// <summary>Class colours and emblems; brushes are frozen and cached, so rows share them.</summary>
public static class ClassStyle
{
    private static readonly Dictionary<(GameClass, double), Brush> Bars = new();
    private static readonly Dictionary<GameClass, Brush> Solids = new();
    private static readonly Dictionary<GameClass, ImageSource?> Icons = new();

    public static Color ColorOf(GameClass c) => c switch
    {
        GameClass.Gladiator => Color.FromRgb(0xE8, 0xA8, 0x4C),
        GameClass.Templar => Color.FromRgb(0x5E, 0x9B, 0xF0),
        GameClass.Ranger => Color.FromRgb(0x6F, 0xCF, 0x7C),
        GameClass.Assassin => Color.FromRgb(0xB0, 0x7C, 0xF0),
        GameClass.Sorcerer => Color.FromRgb(0x47, 0xC8, 0xF5),
        GameClass.Elementalist => Color.FromRgb(0xF2, 0x6C, 0xA7),
        GameClass.Cleric => Color.FromRgb(0xF5, 0xE0, 0x6E),
        GameClass.Chanter => Color.FromRgb(0x3F, 0xD1, 0xB5),
        GameClass.Brawler => Color.FromRgb(0xFF, 0x74, 0x4D),
        _ => Color.FromRgb(0x8A, 0x93, 0xA6),
    };

    public static string Name(GameClass c) => c switch
    {
        GameClass.Gladiator => "Gladiator",
        GameClass.Templar => L.S("Templer", "Templar"),
        GameClass.Ranger => L.S("Waldläufer", "Ranger"),
        GameClass.Assassin => L.S("Assassine", "Assassin"),
        GameClass.Sorcerer => L.S("Magier", "Sorcerer"),
        GameClass.Elementalist => L.S("Beschwörer", "Elementalist"),
        GameClass.Cleric => L.S("Kleriker", "Cleric"),
        GameClass.Chanter => L.S("Kantor", "Chanter"),
        GameClass.Brawler => L.S("Faustkämpfer", "Brawler"),
        _ => L.S("Unbekannt", "Unknown"),
    };

    public static Brush Solid(GameClass c)
    {
        if (!Solids.TryGetValue(c, out var b))
        {
            b = new SolidColorBrush(ColorOf(c));
            b.Freeze();
            Solids[c] = b;
        }
        return b;
    }

    public static Brush Bar(GameClass c, double alpha)
    {
        if (!Bars.TryGetValue((c, alpha), out var b))
        {
            var col = ColorOf(c);
            var g = new LinearGradientBrush(
                Color.FromArgb((byte)(alpha * 255), col.R, col.G, col.B),
                Color.FromArgb((byte)(alpha * 0.45 * 255), col.R, col.G, col.B), 0);
            g.Freeze();
            Bars[(c, alpha)] = b = g;
        }
        return b;
    }

    public static ImageSource? Icon(GameClass c)
    {
        if (Icons.TryGetValue(c, out var img)) return img;
        if (c != GameClass.Unknown)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri($"pack://application:,,,/Assets/Classes/{c.ToString().ToLowerInvariant()}.png");
                bmp.DecodePixelWidth = 32; // small decode: a few KB instead of the full image
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                img = bmp;
            }
            catch { img = null; }
        }
        Icons[c] = img;
        return img;
    }
}
