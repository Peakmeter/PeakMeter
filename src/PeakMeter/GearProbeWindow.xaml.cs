using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PeakMeter;

/// <summary>
/// Guides a short recording to find where the game sends gear score and combat power outside a party: the player
/// notes the numbers the game shows (own character, then someone inspected) while PeakMeter records.
/// </summary>
public partial class GearProbeWindow : Window
{
    private readonly Meter _meter;
    private readonly DispatcherTimer _timer;
    private int _step;   // 0 idle, 1 own values, 2 open own profile, 3 inspect someone, 4 done

    public GearProbeWindow(Meter meter)
    {
        _meter = meter;
        InitializeComponent();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        Closed += (_, _) => _timer.Stop();
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        if (_step is > 0 and < 4 && !_meter.ProbeRunning) _step = 4; // ran out of time
        InputRow.Visibility = _step is 1 or 3 ? Visibility.Visible : Visibility.Collapsed;
        StepText.Text = _step switch
        {
            0 => L.S("Hilf PeakMeter herauszufinden, wo das Spiel Gearscore und Combat Power schickt – dann geht es bald für deinen Charakter und für inspizierte Spieler automatisch. Dauert 2 Minuten.", "Help PeakMeter find where the game sends gear score and combat power – then it will soon work for your character and inspected players. Takes 2 minutes."),
            1 => L.S("Schritt 1 von 3: Öffne im Spiel dein Charakterfenster und trag DEINEN Gearscore und deine Combat Power hier ein. Dann „Merken“.", "Step 1 of 3: Open your character window in the game and enter YOUR gear score and combat power here. Then “Save”."),
            2 => L.S("Schritt 2 von 3: Schließ dein Charakterfenster und öffne es noch einmal (das Spiel schickt die Werte dann neu). Dann „Weiter“.", "Step 2 of 3: Close your character window and open it again (the game then sends the values again). Then “Next”."),
            3 => L.S("Schritt 3 von 3: Inspiziere im Spiel einen anderen Spieler (Rechtsklick → Charakter ansehen) und trag SEINEN Gearscore und seine Combat Power ein. Dann „Merken“.", "Step 3 of 3: Inspect another player in the game (right-click → view character) and enter THEIR gear score and combat power. Then “Save”."),
            _ => L.S("Danke! Die Aufnahme ist gespeichert. Sag Claude Bescheid, dann wird sie ausgewertet.", "Thanks! The recording is saved."),
        };
        MainLabel.Text = _step switch { 0 => L.S("Aufnahme starten", "Start recording"), 1 or 3 => L.S("Merken", "Save"), 2 => L.S("Weiter", "Next"), _ => L.S("Schließen", "Close") };
        if (_meter.ProbeRunning)
        {
            var left = _meter.ProbeLeft;
            StatusText.Text = L.S("Aufnahme läuft", "Recording") + $" · {(int)left.TotalMinutes}:{left.Seconds:00}";
        }
        else StatusText.Text = _step == 4 ? L.S("gespeichert ✓", "saved ✓") : "";
    }

    private void Main_Click(object sender, RoutedEventArgs e) => Next();

    private void Box_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Next();
    }

    private void Next()
    {
        switch (_step)
        {
            case 0:
                if (!_meter.StartProbe())
                {
                    StatusText.Text = L.S("Erst Aion 2 starten", "Start AION 2 first");
                    return;
                }
                _meter.ProbeMark("gear probe start");
                _step = 1;
                GsBox.Focus();
                break;
            case 1:
            case 3:
                if (!TryNumber(GsBox.Text, out var gs) || !TryNumber(CpBox.Text, out var cp))
                {
                    StatusText.Text = L.S("Bitte beide Zahlen eintragen", "Please enter both numbers");
                    return;
                }
                _meter.ProbeMark($"{(_step == 1 ? "self" : "other")} gs {gs} cp {cp}");
                GsBox.Text = CpBox.Text = "";
                if (_step == 3)
                {
                    _meter.StopProbe();
                    _step = 4;
                }
                else _step = 2;
                break;
            case 2:
                _meter.ProbeMark("self window reopened");
                _step = 3;
                GsBox.Focus();
                break;
            default:
                Close();
                return;
        }
        Refresh();
    }

    /// <summary>"1.859", "82,1K", "82100" → whole numbers (K and M understood).</summary>
    private static bool TryNumber(string text, out long value)
    {
        value = 0;
        var t = text.Trim().ToUpperInvariant().Replace(" ", "");
        var mult = 1.0;
        if (t.EndsWith('K')) { mult = 1_000; t = t[..^1]; }
        else if (t.EndsWith('M')) { mult = 1_000_000; t = t[..^1]; }
        if (mult > 1) t = t.Replace(',', '.');
        else t = t.Replace(".", "").Replace(",", "");
        if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return false;
        value = (long)Math.Round(d * mult);
        return value > 0;
    }

    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_step is > 0 and < 4) _meter.StopProbe();
        Close();
    }
}
