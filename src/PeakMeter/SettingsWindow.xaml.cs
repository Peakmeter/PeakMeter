using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace PeakMeter;

/// <summary>Settings with a sidebar: every switch writes straight to the settings and applies at once.</summary>
public partial class SettingsWindow : Window
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly App _app;
    private readonly Settings _s;
    private bool _loading = true;

    public SettingsWindow(App app)
    {
        _app = app;
        _s = app.Settings;
        InitializeComponent();
        VersionText.Text = $"PeakMeter v{App.Version}";
        Load();
        _loading = false;
    }

    private void Load()
    {
        AutoUpdate.IsChecked = _s.AutoUpdate;
        Autostart.IsChecked = IsAutostart();
        PartyOnly.IsChecked = _s.PartyOnly;
        ShowTotal.IsChecked = _s.ShowTotal;
        HideNames.IsChecked = _s.HideNames;
        Compact.IsChecked = _s.Compact;
        FocusMode.IsChecked = _s.FocusMode;
        FadeIdle.IsChecked = _s.FadeIdle;
        OnlyOverGame.IsChecked = _s.OnlyOverGame;
        RememberClick.IsChecked = _s.RememberClickThrough;
        HideClosed.IsChecked = _s.HideWhenGameClosed;
        ToastsOn.IsChecked = _s.Toasts;

        IdleSlider.Value = _s.IdleTimeoutSec;
        OpacitySlider.Value = Math.Round(_s.Opacity * 100);
        ScaleSlider.Value = Math.Round(_s.UiScale * 100);
        RefreshSlider.Value = _s.RefreshMs;
        Volume.Value = _s.SoundVolume;
        ToastSlider.Value = _s.ToastSeconds;
        LeadSlider.Value = _app.Meter.Timers.LeadMinutes;
        BossLeadSlider.Value = _s.BossLeadMin;
        UpdateTexts();

        KeyToggle.Text = Pretty(_s.HotkeyToggle);
        KeyReset.Text = Pretty(_s.HotkeyReset);
        KeyClick.Text = Pretty(_s.HotkeyClickThrough);
        KeyCopy.Text = Pretty(_s.HotkeyCopy);

        Mark(RegionChips, _s.Region);
        Mark(LangChips, _s.Language);
        Mark(TargetChips, _s.BossOnly ? "boss" : "all");
        Mark(DpsChips, _s.DpsMode);
        Mark(ChipChips, _s.ValueChip);
        Mark(ClockChips, _s.Use24h ? "24" : "12");
        Mark(SummaryChips, _s.SummaryMultiline ? "multi" : "one");

        ChangesText.Text = Changelog;
        SupportTitle.Text = L.S("PeakMeter ist kostenlos – für alle.", "PeakMeter is free – for everyone.");
        SupportSub.Text = L.S("Ohne Werbung, ohne Abo, ohne Haken.", "No ads, no subscription, no catch.");
        SupportBody.Text = L.S("Wenn dir PeakMeter beim Raiden hilft, freuen wir uns riesig über einen Kaffee auf Ko-fi. Jede Unterstützung hilft, neue Funktionen und Updates zu bauen. Danke!",
                               "If PeakMeter helps you raid, a coffee on Ko-fi means a lot. Every bit of support helps build new features and updates. Thank you!");
        SupportButton.Text = L.S("Auf Ko-fi unterstützen", "Support on Ko-fi");
        SupportFreeHead.Text = L.S("AUCH GANZ OHNE GELD", "ALSO WITHOUT MONEY");
        SupportFree.Text = L.S("Teile PeakMeter mit deiner Gruppe oder Legion, gib einen Stern auf GitHub oder melde Fehler – das hilft genauso.",
                               "Share PeakMeter with your party or legion, star it on GitHub or report bugs – that helps just as much.");
        CopyLinkLabel.Text = L.S("Download-Link kopieren", "Copy download link");
        StarLabel.Text = L.S("Stern auf GitHub", "Star on GitHub");
        var status = _app.Meter.Status;
        DebugStatus.Text = $"PeakMeter v{App.Version}\n" +
                           L.S("Aufnahme: ", "Capture: ") + status.State + " – " + status.Message + "\n" +
                           L.S("Charakter: ", "Character: ") + (_app.Meter.Tracker.SelfName ?? "–") + "\n" +
                           L.S("Daten: ", "Data: ") + Settings.Dir;
    }

    /// <summary>The chip whose Tag matches is lit, the others dim.</summary>
    private void Mark(Panel chips, string value)
    {
        foreach (var b in chips.Children.OfType<Button>())
        {
            var on = (b.Tag as string) == value;
            b.Foreground = (Brush)FindResource(on ? "Accent" : "TextDim");
            b.Background = on ? new SolidColorBrush(Color.FromArgb(0x26, 0x7F, 0xD8, 0xF2)) : null;
        }
    }

    private static string TagOf(object sender) => (sender as FrameworkElement)?.Tag as string ?? "";

    private void Changed()
    {
        if (_loading) return;
        _s.Save();
        _app.SettingsChanged();
    }

    private void UpdateTexts()
    {
        IdleText.Text = $"{IdleSlider.Value:0} s";
        OpacityText.Text = $"{OpacitySlider.Value:0}%";
        ScaleText.Text = $"{ScaleSlider.Value:0}%";
        RefreshText.Text = $"{RefreshSlider.Value:0} ms";
        VolumeText.Text = Volume.Value < 1 ? L.S("aus", "off") : $"{Volume.Value:0}%";
        ToastText.Text = $"{ToastSlider.Value:0} s";
        LeadText.Text = LeadSlider.Value < 1 ? L.S("aus", "off") : $"{LeadSlider.Value:0} Min";
        BossLeadText.Text = BossLeadSlider.Value < 1 ? L.S("aus", "off") : $"{BossLeadSlider.Value:0} Min";
    }

    // ------------------------------------------------------------------ navigation

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageGeneral is null) return; // during InitializeComponent
        var page = (sender as RadioButton)?.CommandParameter as string;
        foreach (var (name, panel) in new (string, UIElement)[]
                 {
                     ("general", PageGeneral), ("combat", PageCombat), ("display", PageDisplay), ("overlay", PageOverlay),
                     ("audio", PageAudio), ("events", PageEvents), ("keys", PageKeys), ("changes", PageChanges), ("debug", PageDebug),
                     ("support", PageSupport),
                 })
            panel.Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ switches and choices

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        _s.AutoUpdate = AutoUpdate.IsChecked == true;
        _s.PartyOnly = PartyOnly.IsChecked == true;
        _s.ShowTotal = ShowTotal.IsChecked == true;
        _s.HideNames = HideNames.IsChecked == true;
        _s.Compact = Compact.IsChecked == true;
        _s.FocusMode = FocusMode.IsChecked == true;
        _s.FadeIdle = FadeIdle.IsChecked == true;
        _s.OnlyOverGame = OnlyOverGame.IsChecked == true;
        _s.RememberClickThrough = RememberClick.IsChecked == true;
        _s.HideWhenGameClosed = HideClosed.IsChecked == true;
        _s.Toasts = ToastsOn.IsChecked == true;
        Changed();
    }

    private void Region_Click(object sender, RoutedEventArgs e)
    {
        _s.Region = TagOf(sender);
        _app.Meter.Timers.SetRegion(_s.Region);
        Mark(RegionChips, _s.Region);
        Changed();
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        var lang = TagOf(sender);
        if (lang == _s.Language) return;
        _s.Language = lang;
        _app.Restart();
    }

    private void Target_Click(object sender, RoutedEventArgs e)
    {
        _app.Meter.SetBossOnly(TagOf(sender) == "boss");
        Mark(TargetChips, _s.BossOnly ? "boss" : "all");
        Changed();
    }

    private void Dps_Click(object sender, RoutedEventArgs e) { _s.DpsMode = TagOf(sender); Mark(DpsChips, _s.DpsMode); Changed(); }
    private void Chip_Click(object sender, RoutedEventArgs e) { _s.ValueChip = TagOf(sender); Mark(ChipChips, _s.ValueChip); Changed(); }
    private void Clock_Click(object sender, RoutedEventArgs e) { _s.Use24h = TagOf(sender) == "24"; Mark(ClockChips, TagOf(sender)); Changed(); }
    private void Summary_Click(object sender, RoutedEventArgs e) { _s.SummaryMultiline = TagOf(sender) == "multi"; Mark(SummaryChips, TagOf(sender)); Changed(); }

    // ------------------------------------------------------------------ sliders

    private void Idle_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.IdleTimeoutSec = (int)Math.Round(e.NewValue);
        UpdateTexts();
        Changed();
    }

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.Opacity = Math.Round(e.NewValue / 100, 2);
        UpdateTexts();
        Changed();
    }

    private void Scale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.UiScale = Math.Round(e.NewValue / 100, 2);
        UpdateTexts();
        Changed();
    }

    private void Refresh_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.RefreshMs = (int)Math.Round(e.NewValue);
        UpdateTexts();
        Changed();
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.SoundVolume = (int)Math.Round(e.NewValue);
        UpdateTexts();
        _s.Save();
    }

    private void Toast_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.ToastSeconds = (int)Math.Round(e.NewValue);
        UpdateTexts();
        _s.Save();
    }

    private void Lead_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _app.Meter.Timers.LeadMinutes = (int)Math.Round(e.NewValue);
        UpdateTexts();
    }

    private void BossLead_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.BossLeadMin = (int)Math.Round(e.NewValue);
        UpdateTexts();
        _s.Save();
    }

    private void TestSound_Click(object sender, RoutedEventArgs e) => Chime.Play(_s.SoundVolume);

    private void TestToast_Click(object sender, RoutedEventArgs e) =>
        _app.Notify(L.S("Shugo-Festival", "Shugo Festival"), L.S("startet in 1 Min", "starts in 1 min"), "shugo");

    private void OpenTimers_Click(object sender, RoutedEventArgs e) => _app.ShowTimers();

    // ------------------------------------------------------------------ hotkeys

    /// <summary>The pressed combination becomes the field's hotkey (Ctrl, Shift or Alt plus a key); Delete clears it.</summary>
    private void Key_Down(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        string gesture;
        if (key is Key.Delete or Key.Back) gesture = "";
        else
        {
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.Tab) return;
            var mods = Keyboard.Modifiers;
            if (mods == ModifierKeys.None)
            {
                KeyHint.Text = L.S("Bitte mit Strg, Shift oder Alt kombinieren.", "Please combine with Ctrl, Shift or Alt.");
                return;
            }
            var parts = new List<string>();
            if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            var k = key.ToString();
            parts.Add(k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1]) ? k[1..] : k);
            gesture = string.Join("+", parts);
        }
        switch (box.Tag as string)
        {
            case "toggle": _s.HotkeyToggle = gesture; break;
            case "reset": _s.HotkeyReset = gesture; break;
            case "copy": _s.HotkeyCopy = gesture; break;
            default: _s.HotkeyClickThrough = gesture; break;
        }
        box.Text = Pretty(gesture);
        _s.Save();
        var failed = _app.RegisterHotkeys();
        KeyHint.Text = failed.Count == 0
            ? L.S("Gespeichert ✓", "Saved ✓")
            : L.S("Schon von einem anderen Programm belegt: ", "Already used by another program: ") + string.Join(", ", failed.Select(Pretty));
    }

    private static string Pretty(string gesture) =>
        gesture.Length == 0 ? L.S("Nicht festgelegt", "Not set")
        : L.En ? gesture.Replace("+", " + ") : gesture.Replace("Ctrl", "Strg").Replace("+", " + ");

    // ------------------------------------------------------------------ start with Windows

    private static bool IsAutostart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue("PeakMeter") is string;
    }

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (Autostart.IsChecked == true) k.SetValue("PeakMeter", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("PeakMeter", false);
        }
        catch { Autostart.IsChecked = IsAutostart(); }
    }

    // ------------------------------------------------------------------ updates, links, tools

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Updater is null || App.GitHubRepo.Length == 0) return;
        UpdateText.Text = L.S("Suche …", "Checking …");
        try
        {
            UpdateText.Text = await _app.Updater.Check()
                ? L.S($"v{_app.Updater.AvailableVersion} gefunden – siehe Hinweis rechts", $"v{_app.Updater.AvailableVersion} found – see the alert on the right")
                : L.S($"v{App.Version} ist die neueste Version ✓", $"v{App.Version} is the latest version ✓");
        }
        catch { UpdateText.Text = L.S("Keine Verbindung zu GitHub", "Could not reach GitHub"); }
    }

    private void GitHub_Click(object sender, RoutedEventArgs e) => Open(App.GitHubUrl);
    private void Bug_Click(object sender, RoutedEventArgs e) => Open(App.GitHubUrl + "/issues/new?labels=bug&title=" + Uri.EscapeDataString($"[v{App.Version}] "));
    private void Idea_Click(object sender, RoutedEventArgs e) => Open(App.GitHubUrl + "/issues/new?labels=idea");
    private void Kofi_Click(object sender, RoutedEventArgs e) => Open(App.KofiUrl);

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(App.GitHubUrl + "/releases/latest"); } catch { }
        CopyLinkText.Text = L.S("Link kopiert ✓ – einfach im Chat einfügen.", "Link copied ✓ – just paste it in chat.");
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Open(Settings.Dir);
    private void OpenLog_Click(object sender, RoutedEventArgs e) => Open(Path.Combine(Settings.Dir, "peakmeter.log"));
    private void Demo_Click(object sender, RoutedEventArgs e) { _app.SetVisible(true); _app.Meter.StartDemo(); }
    private void Energy_Click(object sender, RoutedEventArgs e) => _app.ShowEnergy();
    private void GearProbe_Click(object sender, RoutedEventArgs e) => _app.ShowGearProbe();

    private static void Open(string target)
    {
        if (target.Length > 0) try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }

    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string Changelog => L.S(
        "v1.21.1 – Offene Welt ohne Gruppe: nur noch du, auch beim Feldboss · DPS und Kampfzeit ab deinem ersten Treffer (aDPS), wie bei den anderen Metern\n\n" +
        "v1.21 – Ping im Meter (aus dem Datenverkehr gemessen) · Energie wie im Spiel: „135 (+755)“\n\n" +
        "v1.20 – Odyle-Energie wird beim Einloggen automatisch gelesen, für jeden Charakter\n\n" +
        "v1.19 – „Energie & Tickets lernen“ mit Energie-Feldern (Neu-Einloggen) · „Server lernen (Raid)“ · Energie-Werte aus dem Spiel sofort übernehmen\n\n" +
        "v1.18 – Server hinter dem Namen wie im Spiel, z. B. „RipHaku [Zik]“ · Region Europa / NA-Ost / NA-West · Profil-Knopf öffnet Gruppenmitglieder direkt\n\n" +
        "v1.17.1 – Verlauf im Meter (◀ ▶ / Mausrad) geht jetzt auch durch die gespeicherten Logs, auch nach einem Neustart · leere 1-Sekunden-Kämpfe überdecken das letzte Ergebnis nicht mehr\n\n" +
        "v1.17 – „Profil“-Knopf bei jedem Spieler: Ausrüstung, Daevanion, Arcana und Skills auf aion2index.com\n\n" +
        "v1.16 – Boss-Leben in Zahlen und geschätzte Zeit bis zum Kill\n\n" +
        "v1.15.1 – Raid: das ganze Team wird wieder angezeigt · Logs springen beim Farmen nicht mehr nach oben\n\n" +
        "v1.15 – Eigene Logos für Shugo-Fest, Luftangriff, Rift, Belagerung, Weltbosse · Tickets-Fenster neu: Odyle, Albtraum, Aszensusritus, Täglicher Dungeon, Shugo- und Invasions-Schlüssel, nach Server sortiert · „Tickets lernen“ · Unterstützen-Seite mit Ko-fi\n\n" +
        "v1.14 – Hinweise fahren rechts am Bildschirmrand rein · Einstellungen mit Seitenleiste · Tickets-Fenster mit Odyle pro Charakter · aDPS · Gearscore oder Kampfkraft neben den Namen · Gesamtschaden · Spielernamen ausblenden · UI-Skalierung · Aktualisierungsrate · Fokusmodus · Verblassen im Leerlauf · Nur über dem Spiel · Region EU/NA\n\n" +
        "v1.13 – Englisch · Auto-Update über GitHub · DPS / HEAL / TANK · Vergleich mit dem letzten Versuch · Bestzeiten · Installer\n\n" +
        "v1.12 – Neues Logo, Eisblau-Design\n\n" +
        "v1.10 – Allein nur du, in der Gruppe nur deine Gruppe · Kampfkraft neben den Namen\n\n" +
        "v1.6 – Odyle-Energie wird direkt aus dem Spiel gelesen\n\n" +
        "v1.3 – Klassen-Logo · Gruppe mit Gearscore und Kampfkraft\n\n" +
        "v1.2 – Luftangriff · alle 48 Weltbosse\n\n" +
        "v1.1 – Logs · Event-Timer mit sanftem Klingelton",
        "v1.21.1 – Open world without a party: only you, also on field bosses · DPS and fight time from your first hit (aDPS), like other meters\n\n" +
        "v1.21 – Ping in the meter (measured from the traffic) · energy like in the game: “135 (+755)”\n\n" +
        "v1.20 – Odyle energy is read automatically at login, for every character\n\n" +
        "v1.19 – “Learn energy & tickets” with energy fields (re-login) · “Learn servers (raid)” · energy values from the game apply right away\n\n" +
        "v1.18 – Server after the name like in the game, e.g. “RipHaku [Zik]” · region Europe / NA East / NA West · profile button opens party members directly\n\n" +
        "v1.17.1 – Meter history (◀ ▶ / mouse wheel) now goes through the saved logs too, even after a restart · empty one-second fights no longer replace the last result\n\n" +
        "v1.17 – “Profile” button for every player: gear, Daevanion, arcana and skills on aion2index.com\n\n" +
        "v1.16 – Boss HP in numbers and the estimated time to kill\n\n" +
        "v1.15.1 – Raids: the whole team shows again · logs no longer jump to the top while farming\n\n" +
        "v1.15 – Own icons for Shugo festival, air raid, rift, siege, world bosses · new tickets window: Odyle, nightmare, ascension, daily dungeon, Shugo and invasion keys, grouped by server · “Learn tickets” · support page with Ko-fi\n\n" +
        "v1.14 – Alerts slide in at the right edge · settings with a sidebar · tickets window with Odyle per character · aDPS · gear score or combat power next to names · total damage · hide player names · UI scale · refresh rate · focus mode · fade when idle · only over the game · region EU/NA\n\n" +
        "v1.13 – English · auto-update from GitHub · DPS / HEAL / TANK · comparison with your last attempt · best times · installer\n\n" +
        "v1.12 – New logo, ice-blue design\n\n" +
        "v1.10 – Solo only you, in a party only your party · combat power next to names\n\n" +
        "v1.6 – Odyle energy read straight from the game\n\n" +
        "v1.3 – Class emblem · party with gear score and combat power\n\n" +
        "v1.2 – Air raid · all 48 world bosses\n\n" +
        "v1.1 – Logs · event timers with a soft chime");
}
