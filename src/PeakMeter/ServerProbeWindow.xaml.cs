using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PeakMeter;

/// <summary>
/// Guided recording to find other players' servers in the game data: in a raid or town with players from other
/// servers, the player notes a few names with the tag the game shows ("RipHaku [Zik]").
/// </summary>
public partial class ServerProbeWindow : Window
{
    private readonly Meter _meter;
    private readonly DispatcherTimer _timer;
    private int _saved;
    private bool _done;

    public ServerProbeWindow(Meter meter)
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
            MainIcon.Text = "";
            MainLabel.Text = _saved > 0 ? L.S("Fertig", "Done") : L.S("Abbrechen", "Cancel");
            StepText.Text = _saved == 0
                ? L.S("Aufnahme läuft. Bleib in der Nähe der anderen Spieler (Raid, Stadt) und schau, wer im Spiel ein Server-Kürzel in Klammern hat, z. B. „RipHaku [Zik]“. Trag 3–5 davon unten ein und klick „Spieler merken“.",
                      "Recording. Stay near the other players (raid, town) and look who shows a server tag in brackets in the game, e.g. “RipHaku [Zik]”. Enter 3–5 of them below and click “Save players”.")
                : L.S("Super! Gern noch ein paar mehr eintragen – oder auf „Fertig“.", "Great! Add a few more if you like – or click “Done”.");
            var left = _meter.ProbeLeft;
            StatusText.Text = L.S("Aufnahme läuft", "Recording") + $" · {(int)left.TotalMinutes}:{left.Seconds:00}"
                              + (_saved > 0 ? L.S($" · {_saved} gemerkt", $" · {_saved} saved") : "");
        }
        else
        {
            MainIcon.Text = "";
            MainLabel.Text = _done ? L.S("Nochmal aufnehmen", "Record again") : L.S("Aufnahme starten", "Start recording");
            StepText.Text = _done && _saved > 0
                ? L.S("Danke! Die Aufnahme ist gespeichert. Sag Claude Bescheid, dann wird sie ausgewertet – danach steht der Server hinter allen Namen.",
                      "Thanks! The recording is saved. Once it has been analysed, the server shows after every name.")
                : L.S("PeakMeter kennt den Server anderer Spieler bisher nur aus deiner Gruppe. Hilf ihm, ihn in den Spieldaten zu finden: Starte die Aufnahme im Raid oder in der Stadt, wo Spieler von anderen Servern sind. Dauert 2 Minuten und bleibt auf deinem PC.",
                      "PeakMeter only knows other players' servers from your party so far. Help it find them in the game data: start the recording in a raid or town with players from other servers. Takes 2 minutes and stays on your PC.");
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
            _saved = 0;
            _done = false;
            if (!_meter.StartProbe("servers", wide: true))
            {
                StatusText.Text = L.S("Erst AION 2 starten", "Start AION 2 first");
                return;
            }
            Input.Focus();
        }
        Refresh();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var pairs = Input.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Replace("[", " ").Replace("]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 2).Select(p => $"{p[0]}={p[^1]}").ToList();
        if (pairs.Count == 0)
        {
            StatusText.Text = L.S("Schreib pro Zeile einen Namen und das Kürzel, z. B. RipHaku Zik", "Write a name and the tag per line, e.g. RipHaku Zik");
            return;
        }
        if (!_meter.ProbeRunning)
        {
            StatusText.Text = L.S("Starte zuerst die Aufnahme.", "Start the recording first.");
            return;
        }
        _meter.ProbeMark("servers " + string.Join(' ', pairs));
        _saved += pairs.Count;
        Input.Clear();
        Refresh();
    }

    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
