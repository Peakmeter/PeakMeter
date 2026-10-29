using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionMeter.Core;
using AionMeter.Core.Events;

namespace PeakMeter;

/// <summary>
/// A picture of a fight to paste into Discord (or anywhere): title, time, and the players with bars in their class
/// colours. Drawn off screen and put on the clipboard as an image.
/// </summary>
public static class DiscordCard
{
    public sealed record Row(string Name, GameClass Class, double Value, double Share, int Deaths, bool IsSelf);

    private const double CardWidth = 480;

    /// <summary>Puts the card on the clipboard; false when that failed (the clipboard can be busy).</summary>
    public static bool Copy(string title, string sub, string clock, string valueLabel, IReadOnlyList<Row> rows, IReadOnlyList<string>? notes = null)
    {
        try
        {
            var bmp = Render(title, sub, clock, valueLabel, rows, notes);
            Clipboard.SetImage(bmp);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static BitmapSource Render(string title, string sub, string clock, string valueLabel, IReadOnlyList<Row> rows, IReadOnlyList<string>? notes = null)
    {
        var font = Application.Current?.TryFindResource("UiFont") as FontFamily ?? new FontFamily("Segoe UI");
        var text = Brush(0xE8, 0xEC, 0xF2);
        var soft = Brush(0xC2, 0xC9, 0xD6);
        var dim = Brush(0x8A, 0x94, 0xA6);
        var accent = Brush(0xF2, 0xC2, 0x6B);

        TextBlock T(string s, double size, Brush b, FontWeight? w = null) => new()
        {
            Text = s, FontSize = size, Foreground = b, FontFamily = font, FontWeight = w ?? FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };

        var stack = new StackPanel();

        // header: title and clock, then the sub line
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(T(title, 19, text, FontWeights.SemiBold));
        var clk = T(clock, 19, accent, FontWeights.SemiBold);
        clk.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(clk, 1);
        head.Children.Add(clk);
        stack.Children.Add(head);
        var subLine = T(sub, 12.5, dim);
        subLine.Margin = new Thickness(0, 2, 0, 12);
        stack.Children.Add(subLine);

        var top = rows.Count > 0 ? Math.Max(1, rows.Max(r => r.Value)) : 1;
        var barMax = CardWidth - 38;
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var cell = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 3) };
            cell.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = Brush(0x1C, 0x22, 0x2C) });
            cell.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(6, barMax * r.Value / top), Background = ClassStyle.Bar(r.Class, r.IsSelf ? 0.55 : 0.38),
            });
            var line = new Grid { Margin = new Thickness(10, 0, 10, 0) };
            foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, new GridLength(52) })
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            var rank = T($"{i + 1}", 13, dim, FontWeights.SemiBold);
            rank.Width = 20;
            line.Children.Add(rank);
            if (ClassStyle.Icon(r.Class) is { } icon)
            {
                var img = new Image { Source = icon, Width = 17, Height = 17, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                Grid.SetColumn(img, 1);
                line.Children.Add(img);
            }
            var name = T(r.Name, 14, r.IsSelf ? Brushes.White : soft, r.IsSelf ? FontWeights.SemiBold : FontWeights.Normal);
            Grid.SetColumn(name, 2);
            line.Children.Add(name);
            if (r.Deaths > 0)
            {
                var dead = T($"☠ {r.Deaths}", 12, Brush(0xFF, 0x8A, 0x8A));
                dead.Margin = new Thickness(8, 0, 8, 0);
                Grid.SetColumn(dead, 3);
                line.Children.Add(dead);
            }
            var val = T(Format.Compact(r.Value), 14, text, FontWeights.SemiBold);
            val.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(val, 4);
            line.Children.Add(val);
            var share = T(Format.Share(r.Share), 12, dim);
            share.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(share, 5);
            line.Children.Add(share);
            cell.Children.Add(line);
            stack.Children.Add(cell);
        }

        if (notes is { Count: > 0 })
        {
            var box = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var n in notes)
            {
                var t = T(n, 12, soft);
                t.TextTrimming = TextTrimming.None;
                t.TextWrapping = TextWrapping.Wrap;
                t.Margin = new Thickness(0, 2, 0, 0);
                box.Children.Add(t);
            }
            stack.Children.Add(box);
        }

        var foot = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        foot.Children.Add(T($"{valueLabel} · " + L.S("Anteil", "share"), 11, dim));
        var brand = T("PeakMeter · peakmeter-ranking.peakmeter.workers.dev", 11, dim);
        brand.HorizontalAlignment = HorizontalAlignment.Right;
        foot.Children.Add(brand);
        stack.Children.Add(foot);

        var card = new Border
        {
            Width = CardWidth, Padding = new Thickness(18, 16, 18, 14), Background = Brush(0x0F, 0x12, 0x17),
            BorderBrush = Brush(0x25, 0x2C, 0x38), BorderThickness = new Thickness(1), Child = stack,
        };
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Ideal);
        card.Measure(new Size(CardWidth, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        card.UpdateLayout();

        const double scale = 2; // sharp on high-DPI screens and when Discord zooms
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth * scale), (int)Math.Ceiling(card.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(card);
        bmp.Freeze();
        return bmp;
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var x = new SolidColorBrush(Color.FromRgb(r, g, b));
        x.Freeze();
        return x;
    }
}
