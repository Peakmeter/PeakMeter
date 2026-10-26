using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AionMeter.Core.Capture;
using AionMeter.Core.Combat;
using AionMeter.Core.Demo;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.Storage;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace PeakMeter;

public partial class App : Application
{
    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _clickItem;
    private Hotkeys? _hotkeys;
    private Overlay? _overlay;
    private LogsWindow? _logs;
    private TimersWindow? _timers;
    private EnergyWindow? _energy;

    /// <summary>"1.8.0" — from the build.</summary>
    public static string Version { get; } = typeof(App).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    /// <summary>One line shown after an update.</summary>
    private const string WhatsNew = "Klick auf die Energie öffnet die Charakter-Übersicht, fremde Charaktere aufgeräumt";
    private const string WhatsNewEn = "clicking the energy opens the character overview, strangers cleaned up";

    /// <summary>Ko-fi page for donations; the tray item and the settings button use it once it is filled in.</summary>
    public const string KofiUrl = "https://ko-fi.com/peakmeter";

    /// <summary>"owner/repo" on GitHub: releases there are the updates. Empty = no update checks.</summary>
    public const string GitHubRepo = "Peakmeter/PeakMeter";

    public static string GitHubUrl => GitHubRepo.Length > 0 ? $"https://github.com/{GitHubRepo}" : "";

    public Updater? Updater { get; private set; }
    private Forms.ToolStripMenuItem? _updateItem;

    public Meter Meter { get; private set; } = null!;
    public Settings Settings { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One meter at a time: starting it again just brings the overlay back.
        var p = Array.IndexOf(e.Args, "--profile");
        if (p >= 0 && p + 1 < e.Args.Length) global::PeakMeter.Settings.UseProfile(e.Args[p + 1]);
        _single = new Mutex(true, @"Local\PeakMeter.Single" + global::PeakMeter.Settings.Profile, out var first);
        if (!first && e.Args.Contains("--restart"))
        {
            try { first = _single.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { first = true; }
        }
        if (!first)
        {
            if (EventWaitHandle.TryOpenExisting(@"Local\PeakMeter.Show" + global::PeakMeter.Settings.Profile, out var h)) using (h) h.Set();
            Shutdown();
            return;
        }
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PeakMeter.Show" + global::PeakMeter.Settings.Profile);
        ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => Dispatcher.BeginInvoke(() => SetVisible(true)), null, -1, false);

        // --- Keep the game first --------------------------------------------------------------------------------
        // Software rendering: a small overlay redrawn a few times a second costs almost nothing on the CPU and
        // keeps the graphics card entirely for the game. Below-normal priority: Windows always serves the game first.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(20));
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

        DispatcherUnhandledException += (_, ex) => ex.Handled = true;

        Settings = Settings.Load();
        SkillIcons.Init(Path.Combine(AppContext.BaseDirectory, "data"));
        L.Use(Settings.Language, Path.Combine(AppContext.BaseDirectory, "data"));
        if (!Settings.RememberClickThrough) Settings.ClickThrough = false;
        if (Settings.Region == "na") Settings.Region = "nae"; // before 1.18 NA was one choice
        Meter = new Meter(Settings, demo: e.Args.Contains("--demo"), saveDemo: e.Args.Contains("--save-demo"));

        _overlay = new Overlay(this);
        new WindowInteropHelper(_overlay).EnsureHandle();
        if (Settings.Visible) _overlay.Show();

        _hotkeys = new Hotkeys(new WindowInteropHelper(_overlay).Handle);
        RegisterHotkeys();

        CreateTray();

        // Updates from GitHub releases: a banner to click, or installed by itself once the game is closed.
        Updater = new Updater(Settings);
        Updater.Available += v => Dispatcher.BeginInvoke(() =>
        {
            if (_updateItem is not null)
            {
                _updateItem.Text = L.S($"Update auf v{v} installieren", $"Install update v{v}");
                _updateItem.Visible = true;
            }
            var text = Settings.AutoUpdate
                ? L.S("Wird installiert, wenn Aion 2 zu ist. Klick = jetzt", "Installs when AION 2 is closed. Click = now")
                : L.S("Klicken zum Installieren", "Click to install");
            Toasts.Show($"PeakMeter v{v}", text, "update", 20, () => _ = Updater.Install());
        });
        Updater.Status += s => Dispatcher.BeginInvoke(() => Toasts.Show("PeakMeter", s, "update", 6));
        Meter.Timers.Alert += OnEventAlert;

        // Just updated: say so once, with what is new (no sound).
        if (Settings.LastVersion != Version)
        {
            Settings.LastVersion = Version;
            Settings.Save();
            Toasts.Show(L.S($"Aktualisiert auf v{Version}", $"Updated to v{Version}"), L.S("Neu: ", "New: ") + L.S(WhatsNew, WhatsNewEn), "update", 12);
        }
        Meter.Start();
        if (e.Args.Contains("--demo")) Meter.StartDemo();
        if (e.Args.Contains("--step-back")) // test aid: show the previous fight in the meter
            new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(4), System.Windows.Threading.DispatcherPriority.Background,
                (t, _) => { ((System.Windows.Threading.DispatcherTimer)t!).Stop(); _overlay?.StepBack(); }, Dispatcher).Start();
        if (e.Args.Contains("--open-all"))
        {
            ShowTimers(); ShowLogs(); ShowEnergy(); ShowGearProbe(); ShowTickets(); ShowSettings(); ShowTicketProbe(); ShowServerProbe();
            Notify(L.S("Shugo-Festival", "Shugo Festival"), L.S("startet in 1 Min", "starts in 1 min"), "shugo");
            Notify(L.S("Luftangriff / Dimensionsinvasion", "Air raid / Dimensional invasion"), L.S("startet in 1 Min", "starts in 1 min"), "invasion");
            Notify("Watcher Kaira", L.S("spawnt in 2 Min", "respawns in 2 min"), "boss");
        }
    }

    /// <summary>The three shortcuts from the settings; returns the ones another program already uses.</summary>
    public List<string> RegisterHotkeys()
    {
        var failed = new List<string>();
        if (_hotkeys is null) return failed;
        _hotkeys.UnregisterAll();
        if (!_hotkeys.Register(Settings.HotkeyToggle, () => SetVisible(!_overlay!.IsVisible))) failed.Add(Settings.HotkeyToggle);
        if (!_hotkeys.Register(Settings.HotkeyReset, Meter.Reset)) failed.Add(Settings.HotkeyReset);
        if (!_hotkeys.Register(Settings.HotkeyClickThrough, ToggleClickThrough)) failed.Add(Settings.HotkeyClickThrough);
        if (Settings.HotkeyCopy.Length > 0 && !_hotkeys.Register(Settings.HotkeyCopy, () => _overlay?.CopySummary())) failed.Add(Settings.HotkeyCopy);
        return failed;
    }

    /// <summary>Starts a fresh PeakMeter (after a language change) and ends this one.</summary>
    public void Restart()
    {
        Settings.Save();
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = false });
        Shutdown();
    }

    private SettingsWindow? _settingsWindow;

    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
    }

    /// <summary>The overlay re-reads display settings (rows, opacity, …).</summary>
    public void SettingsChanged() => _overlay?.ApplySettings();

    public void SetVisible(bool visible)
    {
        if (_overlay is null) return;
        if (visible) _overlay.Show(); else _overlay.Hide();
        Settings.Visible = visible;
        Settings.Save();
    }

    public void ToggleClickThrough()
    {
        Settings.ClickThrough = !Settings.ClickThrough;
        _overlay?.ApplyClickThrough();
        if (_clickItem is not null) _clickItem.Checked = Settings.ClickThrough;
        Settings.Save();
        _tray?.ShowBalloonTip(1500, "PeakMeter",
            Settings.ClickThrough ? L.S("Durchklicken an – Strg+Shift+L zum Ausschalten", "Click-through on – press the hotkey again to turn it off") : L.S("Durchklicken aus", "Click-through off"), Forms.ToolTipIcon.None);
    }

    private long _lastAlert = -10_000;

    private void OnEventAlert(EventState e, int minutes) =>
        Notify(e.Def.Name, minutes <= 0 ? L.S("startet jetzt", "starts now") : L.S($"startet in {minutes} Min", $"starts in {minutes} min"), EventIcons.ForEvent(e.Def.Id));

    /// <summary>Watched world bosses about to respawn (called once a second by the overlay's clock).</summary>
    public void CheckBossAlerts()
    {
        foreach (var t in Meter.Bosses.TakeDueAlerts(DateTimeOffset.Now, Settings.BossLeadMin))
        {
            var minutes = t.NextSpawn is { } next ? (int)Math.Ceiling((next - DateTimeOffset.Now).TotalMinutes) : 0;
            var name = Meter.BossName(t.NpcCode);
            Notify(name, minutes <= 0 ? L.S("ist wieder da", "is back") : L.S($"spawnt in {minutes} Min", $"respawns in {minutes} min"), "boss");
        }
    }

    /// <summary>One soft chime and a card sliding in at the right (or the banner in the meter, if cards are off).</summary>
    public void Notify(string title, string detail = "", string kind = "info", Action? onClick = null)
    {
        // Events starting together (e.g. Shugo and a rift at :00): one chime, the cards stack.
        var now = Environment.TickCount64;
        if (now - _lastAlert >= 3000) Chime.Play(Settings.SoundVolume);
        _lastAlert = now;
        if (Settings.Toasts) Toasts.Show(title, detail, kind, Settings.ToastSeconds, onClick);
        else
        {
            var text = detail.Length > 0 ? $"{title} – {detail}" : title;
            if (_overlay is { IsVisible: true }) _overlay.ShowAlert(text);
            else _tray?.ShowBalloonTip(5000, "PeakMeter", text, Forms.ToolTipIcon.None);
        }
    }

    public void ShowLogs()
    {
        if (_logs is null)
        {
            _logs = new LogsWindow(Meter);
            _logs.Closed += (_, _) => _logs = null;
            _logs.Show();
        }
        _logs.Activate();
    }

    public void ShowEnergy()
    {
        if (_energy is null)
        {
            _energy = new EnergyWindow(Meter, Settings);
            _energy.Closed += (_, _) => _energy = null;
            _energy.Show();
        }
        _energy.Activate();
    }

    private GearProbeWindow? _gearProbe;

    public void ShowGearProbe()
    {
        if (_gearProbe is null)
        {
            _gearProbe = new GearProbeWindow(Meter);
            _gearProbe.Closed += (_, _) => _gearProbe = null;
            _gearProbe.Show();
        }
        _gearProbe.Activate();
    }

    private TicketsWindow? _tickets;

    private ServerProbeWindow? _serverProbe;

    public void ShowServerProbe()
    {
        if (_serverProbe is null)
        {
            _serverProbe = new ServerProbeWindow(Meter);
            _serverProbe.Closed += (_, _) => _serverProbe = null;
            _serverProbe.Show();
        }
        _serverProbe.Activate();
    }

    private TicketProbeWindow? _ticketProbe;

    public void ShowTicketProbe()
    {
        if (_ticketProbe is null)
        {
            _ticketProbe = new TicketProbeWindow(Meter);
            _ticketProbe.Closed += (_, _) => _ticketProbe = null;
            _ticketProbe.Show();
        }
        _ticketProbe.Activate();
    }

    public void ShowTickets()
    {
        if (_tickets is null)
        {
            _tickets = new TicketsWindow(Meter, Settings);
            _tickets.Closed += (_, _) => _tickets = null;
            _tickets.Show();
        }
        _tickets.Activate();
    }

    public void ShowTimers()
    {
        if (_timers is null)
        {
            _timers = new TimersWindow(Meter, Settings);
            _timers.Closed += (_, _) => _timers = null;
            _timers.Show();
        }
        _timers.Activate();
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
        menu.Items.Add(new Forms.ToolStripMenuItem($"PeakMeter v{Version}") { Enabled = false });
        _updateItem = new Forms.ToolStripMenuItem("", null, (_, _) => _ = Updater?.Install()) { Visible = false };
        _updateItem.Font = new Drawing.Font(_updateItem.Font, Drawing.FontStyle.Bold);
        menu.Items.Add(_updateItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L.S("Anzeigen / Verstecken", "Show / hide") + "    " + Settings.HotkeyToggle, null, (_, _) => SetVisible(!_overlay!.IsVisible));
        _clickItem = new Forms.ToolStripMenuItem(L.S("Durchklicken", "Click-through") + "    " + Settings.HotkeyClickThrough, null, (_, _) => ToggleClickThrough()) { Checked = Settings.ClickThrough };
        menu.Items.Add(_clickItem);
        menu.Items.Add(L.S("Zurücksetzen", "Reset") + "    " + Settings.HotkeyReset, null, (_, _) => Meter.Reset());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L.S("Einstellungen", "Settings"), null, (_, _) => ShowSettings());
        menu.Items.Add("Logs", null, (_, _) => ShowLogs());
        menu.Items.Add(L.S("Event-Timer", "Event timers"), null, (_, _) => ShowTimers());
        menu.Items.Add("Tickets", null, (_, _) => ShowTickets());
        menu.Items.Add(L.S("Odyle-Energie", "Odyle energy"), null, (_, _) => ShowEnergy());
        menu.Items.Add(L.S("Energie & Tickets lernen", "Learn energy & tickets"), null, (_, _) => ShowTicketProbe());
        menu.Items.Add(L.S("Server lernen (Raid)", "Learn servers (raid)"), null, (_, _) => ShowServerProbe());
        menu.Items.Add(L.S("Gearscore & CP finden (Beta)", "Find gear score & CP (beta)"), null, (_, _) => ShowGearProbe());
        var party = new Forms.ToolStripMenuItem(L.S("Nur ich / meine Gruppe", "Only me / my party")) { Checked = Settings.PartyOnly, CheckOnClick = true };
        party.CheckedChanged += (_, _) => { Settings.PartyOnly = party.Checked; Settings.Save(); };
        menu.Items.Add(party);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L.S("Demo-Kampf (Test)", "Demo fight (test)"), null, (_, _) => { SetVisible(true); Meter.StartDemo(); });
        menu.Items.Add(L.S("Einstellungsordner öffnen", "Open settings folder"), null, (_, _) =>
        {
            Directory.CreateDirectory(Settings.Dir);
            Process.Start(new ProcessStartInfo("explorer.exe", Settings.Dir) { UseShellExecute = true });
        });
        if (KofiUrl.Length > 0)
            menu.Items.Add(L.S("PeakMeter unterstützen ☕", "Support PeakMeter ☕"), null, (_, _) =>
                Process.Start(new ProcessStartInfo(KofiUrl) { UseShellExecute = true }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L.S("Beenden", "Exit"), null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = TrayGlyph(),
            Text = $"PeakMeter v{Version} – Aion 2 DPS",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) SetVisible(!_overlay!.IsVisible); };
    }

    /// <summary>The PeakMeter logo at the notification area's size (sharp, not a scaled 32 px image).</summary>
    private static Drawing.Icon TrayGlyph()
    {
        try
        {
            var res = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (res is not null)
                using (res.Stream)
                    return new Drawing.Icon(res.Stream, Forms.SystemInformation.SmallIconSize);
        }
        catch { }
        return Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? Drawing.SystemIcons.Application;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _overlay?.SavePlacement();
        Settings?.Save();
        _hotkeys?.Dispose();
        Updater?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        Meter?.Dispose();
        base.OnExit(e);
    }
}

/// <summary>Capture → decoder → combat tracker, plus the name cache so players keep their names across restarts.</summary>
public sealed class Meter : IDisposable
{
    private readonly Settings _settings;
    private readonly NameCache _names = new(Path.Combine(Settings.Dir, "names-cache.json"));
    public HistoryStore History { get; } = new(Path.Combine(Settings.Dir, "fights"));
    public EventTimers Timers { get; } = new();
    public BossTimers Bosses { get; }
    public Odyle Odyle { get; }
    public Records Records { get; }
    public Characters Characters { get; } = new();

    /// <summary>The newest party roster (name, level, class, gear score, combat power); null outside a party.</summary>
    public PartyRosterEvent? Roster { get; private set; }

    /// <summary>Raised (on any thread) when the party's members change.</summary>
    public event Action? RosterChanged;
    /// <summary>Raised on a worker thread after a fight was written to the logs.</summary>
    public event Action? FightSaved;
    private readonly bool _demoOnly;
    private IEventSource? _capture;

    /// <summary>The game's ping in ms, measured from the captured traffic; null when not connected.</summary>
    public double? PingMs => (_capture as LiveCapture)?.Ping.Ms;
    private DemoFightSource? _demo;
    private int _savedVersion;
    private long _lastSave;

    private readonly bool _saveDemo;

    // Character switch: the login's energy waits here until the game names the character.
    private readonly object _loginGate = new();
    private (int Pool, int Bonus, long At)? _loginEnergy;
    private string? _identity;
    private long _identityAt;
    private long _liveEnergyAt = long.MinValue / 2;

    public Meter(Settings settings, bool demo, bool saveDemo = false)
    {
        _saveDemo = saveDemo;
        _settings = settings;
        _demoOnly = demo;
        Data = GameData.Load(Path.Combine(AppContext.BaseDirectory, "data"), settings.Language);
        Tracker = new CombatTracker(Data, new MeterOptions { TargetMode = settings.BossOnly ? TargetMode.BossOnly : TargetMode.All });
        if (!demo && _names.Load() is { } cached) Tracker.ImportCache(cached);
        Tracker.EncounterFinished += OnFightFinished;
        Odyle = new Odyle(settings);
        Tracker.Options.IdleTimeoutMs = Math.Clamp(settings.IdleTimeoutSec, 3, 120) * 1000;
        Timers.SetRegion(settings.Region);
        // The game says who is logged in (again every few minutes). A switch to another character brings that
        // character's energy back – unless the login already sent its exact energy, which then belongs to it.
        Tracker.SelfIdentified += name =>
        {
            var server = Tracker.SelfServerId;
            var now = Environment.TickCount64;
            lock (_loginGate)
            {
                var switched = !string.Equals(name, _identity, StringComparison.Ordinal);
                _identity = name;
                _identityAt = now;
                if (_loginEnergy is { } le && now - le.At < 10 * 60_000)
                {
                    _loginEnergy = null;
                    Characters.Odyle(name, server, le.Pool, le.Bonus);
                    return;
                }
                if (!switched) return;
            }
            var stored = Characters.Get(name, server);
            if (stored is { OdylePool: >= 0, OdyleAt: { } at })
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Odyle.Restore(stored.OdylePool, stored.OdyleBonus, at));
            else
            {
                // Nothing known for this character: the value on screen belongs to someone else (the previous
                // character, or whoever was logged in when PeakMeter last ran). Only a value the game sent just
                // now, before the name, may stay.
                var fresh = now - Interlocked.Read(ref _liveEnergyAt) < 60_000;
                if (!fresh)
                {
                    Log($"Energy: no value known for {name} yet, hiding the previous one");
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Odyle.Clear());
                }
                else if (Odyle.Known)
                    Characters.Odyle(name, server, Odyle.Pool(DateTimeOffset.Now), Odyle.Bonus);
            }
        };
        Records = new Records(History, this);
        // Values entered before the bonus pool existed: anything above the cap was bonus energy.
        if (settings.OdyleValue > Odyle.Cap && settings.OdyleBonus == 0 && settings.OdyleFromGameAt is null)
            Odyle.Set(settings.OdyleValue);
        Tracker.RosterReceived += r =>
        {
            var before = Roster;
            Roster = r.Members.Count > 0 ? r : null;
            // The server repeats the roster every few seconds: only a real change counts.
            static string Key(PartyRosterEvent? x) => x is null ? "" :
                string.Join("|", x.Members.Select(m => $"{m.Name}:{m.GearScore}:{m.CombatPower}:{m.Level}"));
            if (Key(before) != Key(Roster)) RosterChanged?.Invoke();
        };

        // World boss respawns: kills seen nearby and the in-game field boss list (map → exploration → field monsters).
        Bosses = new BossTimers(Path.Combine(Settings.Dir, "boss-timers.json"), Data, Path.Combine(AppContext.BaseDirectory, "data"), persistent: !demo);
        Bosses.SetCurrentServer(Tracker.SelfServerId);
        Tracker.BossNoticed += n => { if (!_demoOnly && !DemoRunning) Bosses.OnNotice(n); };
        Tracker.FieldBossListed += l =>
        {
            if (_demoOnly) return;
            Log($"Field boss list: map {l.MapId}, {l.Slots.Count}/{l.Count} slots");
            Bosses.OnList(l);
        };
        Tracker.SelfIdentified += _ => Bosses.SetCurrentServer(Tracker.SelfServerId);
        _savedVersion = Tracker.CacheVersion;
    }

    public GameData Data { get; }
    public CombatTracker Tracker { get; }

    /// <summary>German boss name, or "Feldboss #slot" for a list slot whose boss is not known yet.</summary>
    public string BossName(int npcCode) => npcCode > 0 ? Data.NpcName(npcCode) : L.S($"Feldboss #{-npcCode % 100}", $"Field boss #{-npcCode % 100}");

    public CaptureStatus Status =>
        !LiveCapture.IsNpcapInstalled() ? new CaptureStatus(CaptureState.Error, "Npcap fehlt")
        : _capture?.Status ?? CaptureStatus.Stopped;

    public bool DemoRunning => _demo?.IsRunning == true;

    public void Start()
    {
        if (_demoOnly || !LiveCapture.IsNpcapInstalled()) return;
        try
        {
            _capture = new LiveCapture(Data, new LiveCaptureOptions { DeviceName = _settings.CaptureDevice });
            _capture.EventDecoded += Tracker.Process;
            _capture.EventDecoded += e =>
            {
                if (e is ResourceRawEvent raw)
                {
                    // Other counters in the same message (tickets, keys …): logged once each, to learn their layout.
                    var hex = Convert.ToHexString(raw.Raw);
                    if (_seenRaw.Add(hex)) Log($"Counter message (unknown): {hex} [char {Tracker.SelfName}]");
                    return;
                }
                if (e is not OdyleEnergyEvent o) return;
                Log($"Odyle from game: pool {o.Regen}, bonus {o.Bonus}, change {o.Change:+0;-0;0} ({Convert.ToHexString(o.Raw)})");
                try { File.AppendAllText(Path.Combine(Settings.Dir, "energy-events.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {Tracker.SelfName}  pool {o.Regen}  bonus {o.Bonus}  change {o.Change}  {Convert.ToHexString(o.Raw)}{Environment.NewLine}"); } catch { }
                Interlocked.Exchange(ref _liveEnergyAt, Environment.TickCount64);
                // The value is always the logged-in character's: show it right away.
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Odyle.SetFromGame(o.Regen, o.Bonus));
                // Which character that is: at a login (Change 0) the name record can come before or after the energy,
                // and the previous character's name record also repeats now and then. So a login's energy always
                // waits for the next name record (it repeats every few minutes) instead of going to the previous one.
                if (o.Change == 0)
                {
                    lock (_loginGate) _loginEnergy = (o.Regen, o.Bonus, Environment.TickCount64);
                    return;
                }
                if (Tracker.SelfName is { } self) Characters.Odyle(self, Tracker.SelfServerId, o.Regen, o.Bonus);
            };
            _capture.StatusChanged += st =>
            {
                // The capture reports every couple of seconds; only a change of state is worth a line.
                var key = st.State + st.Message;
                if (key == _lastStatus) return;
                _lastStatus = key;
                Log($"Capture: {st.State} - {st.Message} (packets {st.Packets}, events {st.Events})");
            };
            _capture.Start();
        }
        catch (Exception ex)
        {
            _capture = null;
            Debug.WriteLine(ex);
        }
    }

    private static readonly object LogGate = new();
    private string? _lastStatus;

    /// <summary>A small text log in %AppData%\PeakMeter\peakmeter.log (capture state, saved fights) for bug reports.
    /// Kept under ~200 KB; no names or chat, only states and numbers.</summary>
    public static void Log(string line)
    {
        try
        {
            lock (LogGate)
            {
                var path = Path.Combine(Settings.Dir, "peakmeter.log");
                Directory.CreateDirectory(Settings.Dir);
                if (File.Exists(path) && new FileInfo(path).Length > 200_000) File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
            }
        }
        catch { }
    }

    // ------------------------------------------------------------------ energy probe (finding Odyle in the traffic)

    private readonly object _probeGate = new();
    private StreamWriter? _probe;
    private long _probeUntil;
    private readonly Dictionary<ushort, int> _probeCounts = new();

    /// <summary>Frequent messages (combat, movement, heartbeats) carry no energy: left out of the probe.</summary>
    private static readonly HashSet<ushort> ProbeSkip =
    [
        AionMeter.Core.Protocol.Opcodes.Heartbeat, AionMeter.Core.Protocol.Opcodes.Cast, AionMeter.Core.Protocol.Opcodes.HitNotice,
        AionMeter.Core.Protocol.Opcodes.Damage, AionMeter.Core.Protocol.Opcodes.Dot, AionMeter.Core.Protocol.Opcodes.HpMp,
        AionMeter.Core.Protocol.Opcodes.RemainHp, AionMeter.Core.Protocol.Opcodes.Spawn, AionMeter.Core.Protocol.Opcodes.ObjectSpawn,
        AionMeter.Core.Protocol.Opcodes.ObjectGone,
    ];

    public bool ProbeRunning
    {
        get { lock (_probeGate) return _probe is not null; }
    }

    public TimeSpan ProbeLeft => TimeSpan.FromMilliseconds(Math.Max(0, _probeUntil - Environment.TickCount64));

    public string? ProbePath { get; private set; }

    /// <summary>
    /// Five minutes of the game's rarer messages (opcode + bytes) in %AppData%\PeakMeter\energy-probe-*.txt, with the
    /// energy values the player enters as markers in between. Only on this PC; used to find where the server sends
    /// Odyle energy. Returns false when no live capture is running.
    /// </summary>
    private bool _probeWide;

    /// <param name="wide">Also keep spawns, big packets and unpacked bundles (login state, other players' servers).</param>
    public bool StartProbe(string kind = "energy", bool wide = false)
    {
        if (_capture is not LiveCapture live) return false;
        lock (_probeGate)
        {
            if (_probe is not null) return true;
            Directory.CreateDirectory(Settings.Dir);
            ProbePath = Path.Combine(Settings.Dir, $"{kind}-probe-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            _probe = new StreamWriter(ProbePath);
            _probeCounts.Clear();
            _probeUntil = Environment.TickCount64 + 10 * 60_000;
            _probeWide = wide;
            _probe.WriteLine($"# PeakMeter {kind} probe {DateTime.Now:O}  (time_ms opcode length hex)");
        }
        live.Pipeline.Parser.Tap = ProbeTap;
        Log($"Probe started ({kind})");
        return true;
    }

    public void ProbeMark(string note)
    {
        lock (_probeGate)
        {
            _probe?.WriteLine($"# MARK {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} {note}");
            _probe?.Flush();
        }
    }

    public void StopProbe()
    {
        if (_capture is LiveCapture live) live.Pipeline.Parser.Tap = null;
        lock (_probeGate)
        {
            if (_probe is null) return;
            _probe.WriteLine("# END");
            _probe.Dispose();
            _probe = null;
        }
        Log("Energy probe stopped");
    }

    private void ProbeTap(long timeMs, ushort opcode, ReadOnlySpan<byte> body)
    {
        if (_probeWide
                ? opcode is AionMeter.Core.Protocol.Opcodes.Heartbeat or AionMeter.Core.Protocol.Opcodes.Damage or AionMeter.Core.Protocol.Opcodes.Dot
                      or AionMeter.Core.Protocol.Opcodes.Cast or AionMeter.Core.Protocol.Opcodes.HitNotice or AionMeter.Core.Protocol.Opcodes.HpMp
                      or AionMeter.Core.Protocol.Opcodes.RemainHp || body.Length > 2_000_000
                : ProbeSkip.Contains(opcode) || body.Length > 32768) return;
        if (Environment.TickCount64 > _probeUntil)
        {
            ThreadPool.QueueUserWorkItem(_ => StopProbe());
            return;
        }
        lock (_probeGate)
        {
            if (_probe is null) return;
            // Anything sent hundreds of times in five minutes is movement or combat, not a resource counter.
            var n = _probeCounts[opcode] = _probeCounts.GetValueOrDefault(opcode) + 1;
            if (n > (_probeWide ? 4000 : 300)) return;
            _probe.Write(timeMs);
            _probe.Write(' ');
            _probe.Write(opcode.ToString("X4"));
            _probe.Write(' ');
            _probe.Write(body.Length);
            _probe.Write(' ');
            _probe.WriteLine(Convert.ToHexString(body));
        }
    }

    public void StartDemo()
    {
        _demo?.Dispose();
        Tracker.Reset();
        _demo = new DemoFightSource(Tracker.Process);
        _demo.Start(TimeSpan.FromSeconds(60));
    }

    public void SetBossOnly(bool bossOnly)
    {
        _settings.BossOnly = bossOnly;
        Tracker.Options.TargetMode = bossOnly ? TargetMode.BossOnly : TargetMode.All;
        _settings.Save();
    }

    /// <summary>Close the current fight and start counting fresh; known names stay known.</summary>
    public void Reset()
    {
        _demo?.Dispose();
        Tracker.Reset();
    }

    private readonly HashSet<string> _seenRaw = new();

    public void Tick()
    {
        Tracker.Tick(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (!_demoOnly && !DemoRunning && Tracker.SelfName is { } me) Characters.Seen(me, Tracker.SelfServerId, Tracker.SelfClass);
        if (_demoOnly || DemoRunning) return;
        var now = Environment.TickCount64;
        if (now - _lastSave < 10_000 || Tracker.CacheVersion == _savedVersion) return;
        _lastSave = now;
        _savedVersion = Tracker.CacheVersion;
        try { _names.Save(Tracker.ExportCache()); } catch { }
    }

    /// <summary>Boss fights always go to the logs, other fights when long enough. Demo fights never.</summary>
    private void OnFightFinished(FightRecord record)
    {
        var s = record.Summary;
        if (((_demoOnly || DemoRunning) && !_saveDemo) || s.Combatants.Count == 0) return;
        if (s.Boss is null && s.CombatMs < _settings.LogMinSeconds * 1000L) return;
        if (_disposing) Save();
        else ThreadPool.QueueUserWorkItem(_ => Save());

        void Save()
        {
            try
            {
                History.Save(record);
                Log($"Fight saved: {s.Title}, {s.CombatMs / 1000}s, {s.PlayerCount} players, boss={s.Boss is not null}");
                FightSaved?.Invoke();
            }
            catch { }
        }
    }

    private bool _disposing;

    public void Dispose()
    {
        StopProbe();
        _disposing = true;
        _capture?.Dispose();
        if (!_demoOnly) Tracker.Reset(); // closing mid-fight keeps the fight so far
        _demo?.Dispose();
        if (!_demoOnly) try { _names.Save(Tracker.ExportCache()); } catch { }
    }
}
