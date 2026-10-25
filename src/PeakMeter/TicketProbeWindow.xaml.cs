using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PeakMeter;

/// <summary>
/// Guided recording to find the ticket counters in the game data: the player re-logs (the server then sends every
/// counter), enters what the game shows, uses one entry and enters the new values. The values are also kept for the
/// tickets window right away, so they are useful even before PeakMeter can read them by itself.
/// </summary>
public partial class TicketProbeWindow : Window
{
    private readonly Meter _meter;
    private readonly DispatcherTimer _timer;
    private int _marks;
    private bool _done;

    public TicketProbeWindow(Meter meter)
    {
        _meter = meter;
        InitializeComponent();
        IcoNightmare.Source = EventIcons.Get("nightmare");
        IcoAscension.Source = EventIcons.Get("ascension");
        IcoDaily.Source = EventIcons.Get("daily");
        IcoShugo.Source = EventIcons.Get("shugokey");
        IcoInvasion.Source = EventIcons.Get("invasionkey");
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        Closed += (_, _) => _timer.Stop();
        Loaded += (_, _) =>
        {
            Prefill();
            Refresh();
        };
    }

    private (string Key, TextBox Box)[] Fields =>
    [
        ("nightmare", InNightmare), ("ascension", InAscension), ("daily", InDaily), ("shugokey", InShugo), ("invasionkey", InInvasion),
    ];

    /// <summary>Last known values of the current character, so only the changed ones need typing.</summary>
    private void Prefill()
    {
        var name = _meter.Tracker.SelfName ?? "";
        if (_meter.Characters.Get(name, _meter.Tracker.SelfServerId) is not { } c) return;
        foreach (var (key, box) in Fields)
            if (c.Tickets.TryGetValue(key, out var v)) box.Text = v.ToString(L.Culture);
    }

    private void Refresh()
    {
        var who = _meter.Tracker.SelfName;
        if (_meter.ProbeRunning)
        {
            MainLabel.Text = _marks >= 2 ? L.S("Fertig", "Done") : L.S("Abbrechen", "Cancel");
            MainIcon.Text = "";
            StepText.Text = _marks switch
            {
                0 => L.S("Schritt 1 von 3: Geh im Spiel zur Charakterauswahl und logge dich wieder ein (dabei schickt das Spiel alle Zähler). Dann trag unten ein, was das Spiel bei den Tickets anzeigt, und klick „Werte merken“.",
                         "Step 1 of 3: In the game, go to character select and log in again (the game then sends every counter). Then enter what the game shows for the tickets and click “Save values”."),
                1 => L.S("Schritt 2 von 3: Verbrauche jetzt einen Eintritt (z. B. Täglichen Dungeon oder Albtraum betreten). Dann die NEUEN Werte eintragen und „Werte merken“.",
                         "Step 2 of 3: Now use one entry (e.g. enter the daily dungeon or nightmare). Then enter the NEW values and click “Save values”."),
                _ => L.S("Schritt 3 von 3: Super! Noch einmal etwas verbrauchen oder den Charakter wechseln und die Werte eintragen – oder auf „Fertig“.",
                         "Step 3 of 3: Great! Use one more entry or switch character and enter the values – or click “Done”."),
            };
            var left = _meter.ProbeLeft;
            StatusText.Text = L.S("Aufnahme läuft", "Recording") + $" · {(int)left.TotalMinutes}:{left.Seconds:00}"
                              + (who is null ? "" : " · " + who);
        }
        else
        {
            MainIcon.Text = "";
            MainLabel.Text = _done ? L.S("Nochmal aufnehmen", "Record again") : L.S("Aufnahme starten", "Start recording");
            StepText.Text = _done && _marks >= 2
                ? L.S("Danke! Die Aufnahme ist gespeichert. Sag Claude Bescheid, dann wird sie ausgewertet – danach liest PeakMeter die Tickets selbst.",
                      "Thanks! The recording is saved. Once it has been analysed, PeakMeter reads the tickets by itself.")
                : _done
                    ? L.S("Die Aufnahme hatte zu wenige Werte. Bitte nochmal – PeakMeter führt dich durch.",
                          "The recording had too few values. Please try again – PeakMeter guides you.")
                    : L.S("PeakMeter kennt die Ticket-Zähler noch nicht. Hilf ihm, sie in den Spieldaten zu finden: dauert 3 Minuten und bleibt auf deinem PC. Du kannst die Werte auch einfach so eintragen – sie erscheinen dann bis zum nächsten Reset im Tickets-Fenster.",
                          "PeakMeter does not know the ticket counters yet. Help it find them in the game data: takes 3 minutes and stays on your PC. You can also just enter the values – they show in the tickets window until the next reset.");
            if (!_done) StatusText.Text = who is null ? L.S("Kein Charakter erkannt – erst im Spiel einloggen.", "No character detected – log in first.") : who;
        }
        SaveLabel.Text = L.S("Werte merken", "Save values");
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
            _marks = 0;
            _done = false;
            if (!_meter.StartProbe("tickets"))
            {
                StatusText.Text = L.S("Erst AION 2 starten", "Start AION 2 first");
                return;
            }
            InNightmare.Focus();
        }
        Refresh();
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Save_Click(sender, e);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var values = new Dictionary<string, int>();
        foreach (var (key, box) in Fields)
        {
            var t = box.Text.Trim();
            if (t.Length == 0) continue;
            if (!int.TryParse(t, out var v) || v < 0 || v > 999)
            {
                StatusText.Text = L.S("Bitte nur Zahlen eintragen.", "Numbers only, please.");
                box.Focus();
                return;
            }
            values[key] = v;
        }
        if (values.Count == 0)
        {
            StatusText.Text = L.S("Trag mindestens einen Wert ein.", "Enter at least one value.");
            return;
        }
        var name = _meter.Tracker.SelfName ?? "";
        if (name.Length == 0)
        {
            StatusText.Text = L.S("Kein Charakter erkannt – erst im Spiel einloggen.", "No character detected – log in first.");
            return;
        }
        _meter.Characters.SetTickets(name, _meter.Tracker.SelfServerId, values);
        if (_meter.ProbeRunning)
        {
            _meter.ProbeMark("tickets " + string.Join(' ', values.Select(kv => $"{kv.Key}={kv.Value}")) + $" char={name}");
            _marks++;
            if (_marks >= 3)
            {
                _meter.StopProbe();
                _done = true;
            }
        }
        Refresh();
        if (!_meter.ProbeRunning || _marks < 3)
            StatusText.Text = L.S("Gemerkt ✓", "Saved ✓") + (_meter.ProbeRunning ? "  ·  " + StatusText.Text : "");
    }

    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
