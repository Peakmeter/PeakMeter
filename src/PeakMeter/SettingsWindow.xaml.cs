using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace PeakMeter;

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
        VersionText.Text = $"v{App.Version}";

        PartyOnly.IsChecked = _s.PartyOnly;
        ShowPower.IsChecked = _s.ShowPower;
        Compact.IsChecked = _s.Compact;
        AutoUpdate.IsChecked = _s.AutoUpdate;
        Autostart.IsChecked = IsAutostart();
        OpacitySlider.Value = Math.Round(_s.Opacity * 100);
        OpacityText.Text = $"{OpacitySlider.Value:0}%";
        Volume.Value = _s.SoundVolume;
        VolumeText.Text = $"{_s.SoundVolume}%";
        KeyToggle.Text = Pretty(_s.HotkeyToggle);
        KeyReset.Text = Pretty(_s.HotkeyReset);
        KeyClick.Text = Pretty(_s.HotkeyClickThrough);
        MarkLanguage();
        GitHubButton.Visibility = App.GitHubUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        KofiButton.Visibility = App.KofiUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _loading = false;
    }

    private void MarkLanguage()
    {
        var en = _s.Language == "en";
        LangDe.Foreground = (System.Windows.Media.Brush)FindResource(en ? "TextDim" : "Text");
        LangEn.Foreground = (System.Windows.Media.Brush)FindResource(en ? "Text" : "TextDim");
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        var lang = (sender as FrameworkElement)?.Tag as string ?? "de";
        if (lang == _s.Language) return;
        _s.Language = lang;
        _app.Restart();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        _s.PartyOnly = PartyOnly.IsChecked == true;
        _s.ShowPower = ShowPower.IsChecked == true;
        _s.Compact = Compact.IsChecked == true;
        _s.AutoUpdate = AutoUpdate.IsChecked == true;
        _s.Save();
        _app.SettingsChanged();
    }

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.Opacity = Math.Round(e.NewValue / 100, 2);
        OpacityText.Text = $"{e.NewValue:0}%";
        _s.Save();
        _app.SettingsChanged();
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.SoundVolume = (int)Math.Round(e.NewValue);
        VolumeText.Text = _s.SoundVolume == 0 ? L.S("aus", "off") : $"{_s.SoundVolume}%";
        _s.Save();
    }

    private void TestSound_Click(object sender, RoutedEventArgs e) => Chime.Play(_s.SoundVolume);

    // ------------------------------------------------------------------ hotkeys

    /// <summary>The pressed combination becomes the field's hotkey (at least Ctrl, Shift or Alt plus a key).</summary>
    private void Key_Down(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
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
        parts.Add(key.ToString().StartsWith('D') && key.ToString().Length == 2 ? key.ToString()[1..] : key.ToString());
        var gesture = string.Join("+", parts);

        var box = (TextBox)sender;
        switch (box.Tag as string)
        {
            case "toggle": _s.HotkeyToggle = gesture; break;
            case "reset": _s.HotkeyReset = gesture; break;
            default: _s.HotkeyClickThrough = gesture; break;
        }
        box.Text = Pretty(gesture);
        _s.Save();
        var failed = _app.RegisterHotkeys();
        KeyHint.Text = failed.Count == 0
            ? L.S("Gespeichert ✓", "Saved ✓")
            : L.S("Schon von einem anderen Programm belegt: ", "Already used by another program: ") + string.Join(", ", failed.Select(Pretty));
    }

    private static string Pretty(string gesture) => L.En ? gesture.Replace("+", " + ") : gesture.Replace("Ctrl", "Strg").Replace("+", " + ");

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

    // ------------------------------------------------------------------ updates and links

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Updater is null || App.GitHubRepo.Length == 0)
        {
            UpdateText.Text = L.S("Updates sind noch nicht eingerichtet.", "Updates are not set up yet.");
            return;
        }
        UpdateText.Text = L.S("Suche …", "Checking …");
        try
        {
            UpdateText.Text = await _app.Updater.Check()
                ? L.S($"v{_app.Updater.AvailableVersion} gefunden – siehe Hinweis im Overlay", $"v{_app.Updater.AvailableVersion} found – see the overlay")
                : L.S("Du hast die neueste Version ✓", "You have the latest version ✓");
        }
        catch
        {
            UpdateText.Text = L.S("Keine Verbindung zu GitHub", "Could not reach GitHub");
        }
    }

    private void GitHub_Click(object sender, RoutedEventArgs e) => Open(App.GitHubUrl);
    private void Kofi_Click(object sender, RoutedEventArgs e) => Open(App.KofiUrl);

    private static void Open(string url)
    {
        if (url.Length > 0) try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
