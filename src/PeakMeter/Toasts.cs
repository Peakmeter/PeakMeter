using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PeakMeter;

/// <summary>
/// Small cards that slide in from the right edge of the screen ("Shugo Festival in 1 min"), stay a few seconds and
/// slide out again. Several stack downwards. They never take focus from the game; a click closes one.
/// </summary>
public static class Toasts
{
    private const double CardWidth = 300, Gap = 8, TopOffset = 90, Margin = 16;
    private static readonly List<ToastWindow> Open = [];

    /// <param name="kind">"event", "boss", "energy", "update" or "info": picks icon and accent colour.</param>
    public static void Show(string title, string text, string kind = "info", int seconds = 8, Action? onClick = null)
    {
        var card = new ToastWindow(title, text, kind, onClick);
        Open.Add(card);
        card.Closed += (_, _) =>
        {
            Open.Remove(card);
            Restack();
        };
        card.Show();
        Restack();
        card.SlideIn(seconds);
    }

    private static void Restack()
    {
        var area = SystemParameters.WorkArea;
        var y = area.Top + TopOffset;
        foreach (var c in Open)
        {
            c.TargetTop = y;
            c.Top = y;
            y += c.ActualHeight > 0 ? c.ActualHeight + Gap : 74 + Gap;
        }
    }

    internal static double RestLeft => SystemParameters.WorkArea.Right - CardWidth - Margin;
    internal static double OutLeft => SystemParameters.WorkArea.Right + 8;
    internal static double Width => CardWidth;
}

internal sealed class ToastWindow : Window
{
    private readonly DispatcherTimer _anim = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly Action? _onClick;
    private double _from, _to;
    private long _start;
    private bool _closing;

    public double TargetTop { get; set; }

    public ToastWindow(string title, string text, string kind, Action? onClick)
    {
        _onClick = onClick;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = Toasts.Width;
        Left = Toasts.OutLeft;
        Top = SystemParameters.WorkArea.Top + 90;
        Cursor = Cursors.Hand;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var (glyph, accent) = kind switch
        {
            "event" => ("", Color.FromRgb(0x7F, 0xD8, 0xF2)),
            "boss" => ("", Color.FromRgb(0xFF, 0x9F, 0x59)),
            "energy" => ("", Color.FromRgb(0xB0, 0x7C, 0xF0)),
            "update" => ("", Color.FromRgb(0x3F, 0xD1, 0x8B)),
            _ => ("", Color.FromRgb(0x7F, 0xD8, 0xF2)),
        };
        var accentBrush = new SolidColorBrush(accent);
        var font = (FontFamily)Application.Current.FindResource("UiFont");
        var iconFont = (FontFamily)Application.Current.FindResource("IconFont");

        var icon = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)),
            Child = new TextBlock { Text = glyph, FontFamily = iconFont, FontSize = 15, Foreground = accentBrush,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Top,
        };
        var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, FontFamily = font, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8)), TextTrimming = TextTrimming.CharacterEllipsis });
        if (text.Length > 0)
            texts.Children.Add(new TextBlock { Text = text, FontFamily = font, FontSize = 11, Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCE, 0xD9)), TextWrapping = TextWrapping.Wrap });
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(texts);

        var card = new Grid();
        card.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x0D, 0x0F, 0x15)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 11, 14, 11),
            Child = row,
        });
        card.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 12),
            CornerRadius = new CornerRadius(0, 2, 2, 0), Background = accentBrush });
        Content = card;

        MouseLeftButtonUp += (_, _) =>
        {
            _onClick?.Invoke();
            SlideOut();
        };
        _anim.Tick += (_, _) => Step();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.SetExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW, true);
    }

    public void SlideIn(int seconds)
    {
        Animate(Toasts.OutLeft, Toasts.RestLeft);
        var stay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(3, seconds)) };
        stay.Tick += (_, _) =>
        {
            stay.Stop();
            if (IsMouseOver) { stay.Start(); return; } // reading it: keep it
            SlideOut();
        };
        stay.Start();
    }

    private void SlideOut()
    {
        if (_closing) return;
        _closing = true;
        Animate(Left, Toasts.OutLeft);
    }

    private void Animate(double from, double to)
    {
        _from = from;
        _to = to;
        _start = Environment.TickCount64;
        _anim.Start();
    }

    private void Step()
    {
        var t = Math.Min(1, (Environment.TickCount64 - _start) / 260.0);
        var eased = 1 - Math.Pow(1 - t, 3); // ease-out: fast in, soft landing
        Left = _from + (_to - _from) * eased;
        if (t < 1) return;
        _anim.Stop();
        if (_closing) Close();
    }
}
