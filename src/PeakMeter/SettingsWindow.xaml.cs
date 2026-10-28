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
        ShowPortrait.IsChecked = _s.ShowPortrait;
        ShowPets.IsChecked = _s.ShowPets;
        CountMe.IsChecked = _s.CountMe;
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
        foreach (var (code, name) in L.Languages)
        {
            var chip = new Button { Style = (Style)FindResource("Chip"), Height = 26, Content = name, Tag = code, Margin = new Thickness(4, 0, 0, 4) };
            chip.Click += Lang_Click;
            LangChips.Children.Add(chip);
        }
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
        _s.ShowPortrait = ShowPortrait.IsChecked == true;
        _s.ShowPets = ShowPets.IsChecked == true;
        _s.CountMe = CountMe.IsChecked == true;
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
        "v1.34.0 – Anonyme Nutzerzahl: Solange PeakMeter mit dem Spiel verbunden ist, zählt es einen öffentlichen Zähler hoch, damit wir sehen, wie viele mit PeakMeter spielen. Kein Name, kein Charakter, keine ID. Abschaltbar unter Einstellungen → „Anonym mitzählen“\n\n" +
        "v1.33.0 – Bereit-Check: In der Gruppenliste steht in der Lobby ✓ (bereit) oder ✗ (nicht bereit) bei jedem Mitglied, oben „Bereit 2/4“. Danke an derjerm für die Aufnahme!\n\n" +
        "v1.32.0 – Stirbst du am Boss und wirst am Kibelisk wiederbelebt, läuft der Kampf weiter (3 Min. Zeit zum Zurücklaufen) · Fremde an Trainingspuppen starten keinen Kampf mehr; in einer Gruppe startet nur noch deine Gruppe einen Kampf · Gruppenliste: neue Mitglieder erscheinen auch ohne offenes Gruppenfenster, fremde Gruppen aus der Gruppensuche werden ignoriert, Verlassen der Gruppe wird erkannt\n\n" +
        "v1.31.6 – Weltboss-Timer: alle 24 Verteron-Bosse (und Altgard) werden jetzt fest dem richtigen Platz in der Spiel-Liste zugeordnet. Vorher fehlten manche (z. B. Garshim) oder zeigten falsche Zeiten; alte falsche Zuordnungen werden automatisch gelöscht\n\n" +
        "v1.31.5 – Pet-Fenster: Knopf „Karte“ bei jedem Monster zeigt alle Spawns auf der interaktiven Karte von aion2timers.com, mit nächstem Kibelisk\n\n" +
        "v1.31.4 – Logs: Kampfname wird nicht mehr von den Knöpfen abgeschnitten · Demo zeigt Stagger, Tode, Buffs und Pets\n\n" +
        "v1.31.3 – Neu in den Einstellungen: „Pets anzeigen“ – wer mit Pets fertig ist, kann die Pet-Zeile und den Pet-Knopf ausschalten\n\n" +
        "v1.31.2 – Pet-Symbol: helle Pfote statt dunklem Emoji (Meter und Pet-Fenster)\n\n" +
        "v1.31.1 – Pet-Zeile wechselt sofort zum Monster, das du gerade triffst (vorher erst nach ~10 s)\n\n" +
        "v1.31.0 – Pet-Fenster: oben „Hier im Gebiet“ – alle Monster auf deiner Karte, deren Seelen dir noch fehlen, mit Gebiet, nächstem Kibelisk und wie viele gerade in der Nähe sind · Filter „nur dieses Gebiet“\n\n" +
        "v1.30.3 – Energie: solange das Spiel sie noch nicht geschickt hat, steht „~“ davor (geschätzt) und es kommt keine „voll“-Meldung\n\n" +
        "v1.30.2 – Pets: farbig – grün = voll, orange = Stufe 2, rot = viel fehlt (Meter und Pet-Fenster)\n\n" +
        "v1.30.1 – Energie: falsche Werte (z. B. 590 (+1)) und ständige „Energie voll“-Meldungen seit v1.29.1 behoben\n\n" +
        "v1.30.0 – Pets: der Meter zeigt beim Monster, wie viele Seelen dir für sein Pet noch fehlen oder ob es schon voll ist, und nach jeder Seele den neuen Stand · neues Pet-Fenster (🐾) mit allen Pets, Stufe, Seelen und wo sie droppen\n\n" +
        "v1.29.1 – Energie: wird jetzt auch aus anderen Spiel-Nachrichten gelesen (z. B. nach einem Dungeon-Run) · ein anderer Zähler wurde fälschlich als 0 (+0) gelesen: behoben\n\n" +
        "v1.29.0 – Logs: Knopf „Buffs“ zeigt jeden Buff und Boss-Debuff mit Uptime und DPS mit / ohne (für die ganze Gruppe)\n\n" +
        "v1.28.0 – Logs: Knopf „Top-Skills“ zeigt die stärksten Skills der ganzen Gruppe (wer mit welchem Skill am meisten Schaden gemacht hat)\n\n" +
        "v1.27.0 – Stagger: ⚡ mit Prozent neben der DPS zeigt, wie viel Stagger jeder Spieler macht, und an den Skills, welcher Skill wie viel davon gemacht hat\n\n" +
        "v1.26.0 – Tode: ☠ mit Zahl hinter dem Namen zeigt, wie oft jemand im Kampf gestorben ist (Meter und Logs)\n\n" +
        "v1.25.1 – Kämpfe endeten auf manchen PCs nach 1 Sekunde (Uhr des Netzwerktreibers falsch): behoben\n\n" +
        "v1.25.0 – Neu in den Einstellungen: dein Charakterbild (offizielles Bild von NCSoft) statt Klassen-Logo im Meter\n\n" +
        "v1.24.5 – Klick auf einen Spielernamen (Logs und Meter) zeigt wieder seine Skills und das Profil\n\n" +
        "v1.24.4 – Logs: alte Kämpfe lassen sich wieder sofort per Klick öffnen\n\n" +
        "v1.24.3 – Charakter ohne bekannten Energiewert: keine fremde Energie mehr angezeigt, sondern „Energie“, bis das Spiel den Wert schickt\n\n" +
        "v1.24.2 – Klick auf die Energie öffnet die Charakter-Übersicht · fremde Spieler (z. B. „TopBanana“) werden nicht mehr für dich gehalten und aufgeräumt · Energie beim Charakterwechsel landet sicher beim richtigen Charakter\n\n" +
        "v1.24.1 – Maus über die Energie im Meter: Energie aller Charaktere auf einen Blick\n\n" +
        "v1.24 – Server-Kürzel hinter mehr Namen: aus der Gruppenliste und aus den Besitzer-Daten von Beschwörungen und Beute, auch in den Logs\n\n" +
        "v1.23.2 – Kampfkraft aller Gruppenmitglieder, auch mit leeren Plätzen in der Gruppe und Spielern von anderen Servern\n\n" +
        "v1.23.1 – Charakterwechsel: die Energie des neuen Charakters erscheint sofort und landet beim richtigen Charakter\n\n" +
        "v1.23 – 9 Sprachen: Deutsch, English, Français, Español, Português, Русский, 한국어, 繁體中文, 日本語 · Regionen Korea und Taiwan · Umlaute im Tray-Menü repariert\n\n" +
        "v1.22 – Skill-Icons im Meter und in den Logs (vom offiziellen AION-2-Bildserver)\n\n" +
        "v1.21.2 – Fremde Spieler in benannten Gebieten (z. B. Östliches Cantastal) werden ohne Gruppe nicht mehr angezeigt\n\n" +
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
        "v1.34.0 – Anonymous player count: while connected to the game, PeakMeter raises a public counter so we can see how many play with it. No name, no character, no id. Turn it off under Settings → “Count me anonymously”\n\n" +
        "v1.33.0 – Ready check: in the lobby the party list shows ✓ (ready) or ✗ (not ready) for every member, and “Ready 2/4” on top. Thanks to derjerm for the recording!\n\n" +
        "v1.32.0 – Die on a boss and revive at a Kibelisk: the fight goes on (3 min to run back) · Strangers on training dummies no longer start a fight; in a party only your party starts one · Party list: new members show without the party window open, other parties from the party finder are ignored, leaving the party is noticed\n\n" +
        "v1.31.6 – World boss timers: all 24 Verteron bosses (and Altgard) are now matched to their exact place in the in-game list. Before, some were missing (e.g. Garshim) or showed wrong times; old wrong matches are cleared automatically\n\n" +
        "v1.31.5 – Pet window: a “Map” button on every monster shows all its spawns on aion2timers.com's interactive map, with the nearest Kibelisk\n\n" +
        "v1.31.4 – Logs: the fight name is no longer cut off by the buttons · the demo shows stagger, deaths, buffs and pets\n\n" +
        "v1.31.3 – New setting: “Show pets” – done with pets? Turn off the pet line and the pet button\n\n" +
        "v1.31.2 – Pet symbol: a light paw instead of the dark emoji (meter and pet window)\n\n" +
        "v1.31.1 – The pet line switches at once to the monster you are hitting (it took ~10 s)\n\n" +
        "v1.31.0 – Pet window: “Here in the area” at the top – every monster on your map whose souls you still need, with its area, nearest Kibelisk and how many are nearby · filter “this area only”\n\n" +
        "v1.30.3 – Energy: until the game has sent it, it shows with “~” (estimated) and no “full” toast comes\n\n" +
        "v1.30.2 – Pets: in colour – green = full, orange = level 2, red = many souls missing (meter and pet window)\n\n" +
        "v1.30.1 – Energy: wrong values (e.g. 590 (+1)) and endless “energy full” toasts since v1.29.1 fixed\n\n" +
        "v1.30.0 – Pets: the meter shows for a monster how many souls its pet still needs or that it is full, and the new count after each soul · new pet window (🐾) with every pet, level, souls and where they drop\n\n" +
        "v1.29.1 – Energy: now also read from other game messages (e.g. after a dungeon run) · another counter was misread as 0 (+0): fixed\n\n" +
        "v1.29.0 – Logs: the “Buffs” button shows every buff and boss debuff with its uptime and the DPS with / without it (for the whole group)\n\n" +
        "v1.28.0 – Logs: the “Top skills” button shows the whole group's strongest skills (who did the most damage with which skill)\n\n" +
        "v1.27.0 – Stagger: ⚡ with a percentage next to the DPS shows each player's share of the stagger, and on the skills which skill did how much\n\n" +
        "v1.26.0 – Deaths: ☠ with a number after the name shows how often someone died in the fight (meter and logs)\n\n" +
        "v1.25.1 – Fights ended after 1 second on some PCs (network driver clock off): fixed\n\n" +
        "v1.25.0 – New setting: your character portrait (official NCSoft image) instead of the class emblem in the meter\n\n" +
        "v1.24.5 – Clicking a player's name (logs and meter) shows their skills and profile again\n\n" +
        "v1.24.4 – Logs: older fights open instantly on click again\n\n" +
        "v1.24.3 – A character without a known energy value no longer shows another character's energy; it waits for the game's value\n\n" +
        "v1.24.2 – Clicking the energy opens the character overview · strangers are no longer taken for you (and are cleaned up) · energy at a character switch reliably goes to the right character\n\n" +
        "v1.24.1 – Hover the energy in the meter: every character’s energy at a glance\n\n" +
        "v1.24 – Server tags after more names: from the party list and from summon and loot owner records, in the logs too\n\n" +
        "v1.23.2 – Combat power for every party member, also with empty slots and players from other servers\n\n" +
        "v1.23.1 – Character switch: the new character’s energy shows right away and is stored with the right character\n\n" +
        "v1.23 – 9 languages: Deutsch, English, Français, Español, Português, Русский, 한국어, 繁體中文, 日本語 · regions Korea and Taiwan\n\n" +
        "v1.22 – Skill icons in the meter and the logs (from the official AION 2 image server)\n\n" +
        "v1.21.2 – Strangers in named areas (e.g. Eastern Cantas Valley) no longer show without a party\n\n" +
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
