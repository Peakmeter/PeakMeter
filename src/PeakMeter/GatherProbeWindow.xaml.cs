using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PeakMeter;

/// <summary>
/// Guided recording to find gathering and crafting in the game data: the player marks "gathering now" or "crafting
/// now" right before doing it, and notes what came out ("Iron ore 2"). The recording stays on this PC and is analysed
/// by hand; afterwards the meter can count gathering and crafting by itself.
/// </summary>
public partial class GatherProbeWindow : Window
{
    private readonly Meter _meter;
    private readonly DispatcherTimer _timer;
    private int _saved, _actions;
    private bool _done;

    public GatherProbeWindow(Meter meter)
    {
        _meter = meter;
        InitializeComponent();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        Closed += (_, _) => _timer.Stop();
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        if (_meter.ProbeRunning)
        {
            MainLabel.Text = _saved > 0 ? L.S("Fertig", "Done") : L.S("Abbrechen", "Cancel");
            StepText.Text = L.S(
                "Aufnahme läuft. Klick „⛏ Ich sammle jetzt“, sammle 3–4 Mal (Erz, Pflanzen …) und trag danach ein, was du bekommen hast. Wenn du kannst: einmal „⚒ Ich stelle jetzt her“ und etwas herstellen. Deine Sammel-Stufe aus dem Spiel hilft auch.",
                "Recording. Click “⛏ Gathering now”, gather 3–4 times (ore, plants …) and then enter what you got. If you can: click “⚒ Crafting now” once and craft something. Your gathering level from the game helps too.");
            var left = _meter.ProbeLeft;
            StatusText.Text = L.S("Aufnahme läuft", "Recording") + $" · {(int)left.TotalMinutes}:{left.Seconds:00}"
                              + (_actions > 0 ? L.S($" · {_actions} markiert", $" · {_actions} marked") : "")
                              + (_saved > 0 ? L.S($" · {_saved} Ergebnisse", $" · {_saved} results") : "");
        }
        else
        {
            MainLabel.Text = _done ? L.S("Nochmal aufnehmen", "Record again") : L.S("Aufnahme starten", "Start recording");
            StepText.Text = _done && _saved > 0
                ? L.S("Danke! Die Aufnahme ist gespeichert. Sag Claude Bescheid, dann wird sie ausgewertet – danach zählt PeakMeter Sammeln und Handwerk selbst.",
                      "Thanks! The recording is saved. Once it has been analysed, PeakMeter counts gathering and crafting by itself.")
                : L.S("PeakMeter soll Sammeln und Handwerk zählen. Dafür muss es die Spieldaten erst kennenlernen: Stell dich an ein paar Sammelpunkte, starte die Aufnahme und folge den Schritten. Dauert 3–5 Minuten und bleibt auf deinem PC.",
                      "PeakMeter is to count gathering and crafting. For that it first has to learn the game data: stand near a few gathering spots, start the recording and follow the steps. Takes 3–5 minutes and stays on your PC.");
            if (!_done) StatusText.Text = "";
        }
    }

    private void Main_Click(object sender, RoutedEventArgs e)
    {
        if (_meter.ProbeRunning)
        {
            _meter.StopProbe();
            _done = true;
        }
        else
        {
            _saved = _actions = 0;
            _done = false;
            if (!_meter.StartProbe("gather", wide: true))
            {
                StatusText.Text = L.S("Erst AION 2 starten", "Start AION 2 first");
                return;
            }
        }
        Refresh();
    }

    private bool Recording()
    {
        if (_meter.ProbeRunning) return true;
        StatusText.Text = L.S("Starte zuerst die Aufnahme.", "Start the recording first.");
        return false;
    }

    private void Gather_Click(object sender, RoutedEventArgs e)
    {
        if (!Recording()) return;
        _meter.ProbeMark("gather start");
        _actions++;
        Refresh();
    }

    private void Craft_Click(object sender, RoutedEventArgs e)
    {
        if (!Recording()) return;
        _meter.ProbeMark("craft start");
        _actions++;
        Refresh();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var text = Input.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length == 0)
        {
            StatusText.Text = L.S("Schreib, was du bekommen hast, z. B. Eisenerz 2", "Write what you got, e.g. Iron ore 2");
            return;
        }
        if (!Recording()) return;
        _meter.ProbeMark("result " + text);
        _saved++;
        Input.Clear();
        Refresh();
    }

    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
