using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PeakMeter;

public partial class EnergyWindow : Window
{
    private static CultureInfo De => L.Culture;
    private readonly Meter _meter;
    private readonly Odyle _odyle;
    private readonly Settings _settings;
    private readonly DispatcherTimer _timer;

    public EnergyWindow(Meter meter, Settings settings)
    {
        _meter = meter;
        _odyle = meter.Odyle;
        _settings = settings;
        InitializeComponent();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        Closed += (_, _) => _timer.Stop();
        Loaded += (_, _) =>
        {
            Refresh();
            Input.Focus();
        };
    }

    private int _marks;        // values entered during this recording
    private bool _probeDone;

    /// <summary>Step-by-step guide while the recording runs; it stops by itself after three values.</summary>
    private void RefreshProbe()
    {
        if (_meter.ProbeRunning)
        {
            ProbeLabel.Text = L.S("Abbrechen", "Cancel");
            ProbeIcon.Text = "";
            ProbeStep.Text = _marks switch
            {
                0 => L.S("Schritt 1 von 3: Schau im Spiel nach, wie viel Odyle-Energie du hast, und trag den Wert oben ein (Enter).", "Step 1 of 3: Check your Odyle energy in the game and enter it above (Enter)."),
                1 => L.S("Schritt 2 von 3: Öffne jetzt im Spiel eine Belohnung, die Energie kostet (oder nimm einen Energie-Trank). Dann den NEUEN Wert oben eintragen.", "Step 2 of 3: Now open a reward that costs energy in the game (or use an energy potion). Then enter the NEW value above."),
                _ => L.S("Schritt 3 von 3: Super! Noch einmal Energie ändern und den neuen Wert eintragen – dann bin ich mir sicher. Oder auf „Fertig“.", "Step 3 of 3: Great! Change the energy once more and enter the new value – or click “Done”."),
            };
            if (_marks >= 2) ProbeLabel.Text = L.S("Fertig", "Done");
            var left = _meter.ProbeLeft;
            ProbeStatus.Text = L.S("Aufnahme läuft", "Recording") + $" · {(int)left.TotalMinutes}:{left.Seconds:00}";
        }
        else
        {
            ProbeIcon.Text = "";
            if (_probeDone && _marks >= 2)
            {
                ProbeLabel.Text = L.S("Nochmal", "Again");
                ProbeStep.Text = L.S("Danke! Die Aufnahme ist gespeichert. Sag Claude Bescheid, dann wird sie ausgewertet.", "Thanks! The recording is saved.");
                ProbeStatus.Text = L.S("gespeichert ✓", "saved ✓");
            }
            else
            {
                ProbeLabel.Text = L.S("Los geht's", "Let's go");
                ProbeStep.Text = _probeDone
                    ? L.S("Die Aufnahme hatte zu wenige Werte. Bitte nochmal – PeakMeter führt dich durch.", "The recording had too few values. Please try again – PeakMeter guides you.")
                    : L.S("Hilf PeakMeter, die Energie im Spiel zu finden. Dauert 2 Minuten, bleibt auf deinem PC.", "Help PeakMeter find the energy in the game data. Takes 2 minutes, stays on your PC.");
                ProbeStatus.Text = "";
            }
        }
    }

    private void Probe_Click(object sender, RoutedEventArgs e)
    {
        if (_meter.ProbeRunning)
        {
            _meter.StopProbe();
            _probeDone = true;
        }
        else
        {
            _marks = 0;
            _probeDone = false;
            if (!_meter.StartProbe())
            {
                ProbeStatus.Text = L.S("Erst Aion 2 starten", "Start AION 2 first");
                return;
            }
            Input.Focus();
        }
        RefreshProbe();
    }

    /// <summary>A value entered while recording: one step further; three values are plenty.</summary>
    private void Marked()
    {
        if (!_meter.ProbeRunning) return;
        _marks++;
        if (_marks >= 3)
        {
            _meter.StopProbe();
            _probeDone = true;
        }
    }

    private void Refresh()
    {
        RefreshProbe();
        var now = DateTimeOffset.Now;
        CapText.Text = $"/ {_odyle.Cap}";
        QuaiCheck.Text = _settings.OdyleQuai ? "" : "";
        if (!_odyle.Known)
        {
            ValueText.Text = "?";
            Fill.Width = 0;
            InfoText.Text = L.S("Noch kein Wert. Schau im Spiel nach und trag ihn unten ein.", "No value yet. Check the game and enter it below.");
            ClaimsText.Text = "";
            return;
        }
        var cur = _odyle.Current(now);
        var pool = _odyle.Pool(now);
        ValueText.Text = cur.ToString(De);
        CapText.Text = _odyle.Bonus > 0 ? $"= {pool} / {_odyle.Cap}  + {_odyle.Bonus} Bonus" : $"/ {_odyle.Cap}";
        Fill.Width = Math.Max(0, Track.ActualWidth * Math.Min(1, (double)pool / _odyle.Cap));

        // Where the number comes from: the game itself, or the player's last entry.
        if (_odyle.FromGameAt is { } fromGame)
        {
            SourceText.Text = L.S($"✓ Vom Spiel erkannt (zuletzt {fromGame.ToLocalTime().ToString("HH:mm", De)}), dazwischen hochgerechnet", $"✓ Read from the game (last {fromGame.ToLocalTime().ToString("HH:mm", De)}), counted forward in between");
            SourceText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3F, 0xD1, 0x8B));
            ProbeBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            SourceText.Text = L.S("Von Hand eingetragen – wird automatisch, sobald sich die Energie im Spiel ändert", "Entered by hand – becomes automatic as soon as the energy changes in the game");
            SourceText.Foreground = (System.Windows.Media.Brush)FindResource("TextDim");
        }

        if (_odyle.Full(now))
            InfoText.Text = L.S("Aufladung voll – ab jetzt verfällt die Regeneration. Zeit zum Raiden!", "Recharge full – regeneration is being wasted. Time to raid!");
        else
        {
            var next = _odyle.NextTick(now)!.Value;
            var full = _odyle.FullAt(now)!.Value;
            InfoText.Text = L.S($"+{_odyle.Regen} in {TimersWindow.Clock(next - now)}  ·  voll {When(full)}", $"+{_odyle.Regen} in {TimersWindow.Clock(next - now)}  ·  full {When(full)}");
        }
        var claims = cur / Odyle.Claim;
        ClaimsText.Text = claims == 1 ? L.S("Reicht für 1 Belohnung (je 40)", "Enough for 1 reward (40 each)") : L.S($"Reicht für {claims} Belohnungen (je 40)", $"Enough for {claims} rewards (40 each)");
    }

    private static string When(DateTimeOffset t)
    {
        var local = t.ToLocalTime();
        return local.Date == DateTime.Today ? L.S("heute ", "today ") + local.ToString("HH:mm", De)
            : local.Date == DateTime.Today.AddDays(1) ? L.S("morgen ", "tomorrow ") + local.ToString("HH:mm", De)
            : local.ToString("ddd HH:mm", De);
    }

    private void Spend_Click(object sender, RoutedEventArgs e) => Change(-Odyle.Claim);
    private void Plus10_Click(object sender, RoutedEventArgs e) => Change(10);
    private void Plus40_Click(object sender, RoutedEventArgs e) => Change(40);

    private void Change(int delta)
    {
        _odyle.Adjust(delta);
        _meter.ProbeMark($"change {delta:+0;-0} -> {_odyle.Current(DateTimeOffset.Now)}");
        Marked();
        Refresh();
    }
    private void Quai_Click(object sender, RoutedEventArgs e) { _odyle.SetQuai(!_settings.OdyleQuai); Refresh(); }

    private void Apply_Click(object sender, RoutedEventArgs e) => Apply();

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Apply();
        if (e.Key == Key.Escape) Close();
    }

    private void Apply()
    {
        if (!int.TryParse(Input.Text.Trim().Replace(".", ""), out var v)) return;
        _odyle.Set(v);
        _meter.ProbeMark($"value {v}");
        Marked();
        Input.Text = "";
        Refresh();
    }

    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
