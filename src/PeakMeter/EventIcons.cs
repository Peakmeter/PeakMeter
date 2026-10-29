using System.Windows;
using System.Windows.Media;

namespace PeakMeter;

/// <summary>
/// PeakMeter's own little vector icons (24 × 24 design grid), drawn once and frozen: one per kind of event and
/// ticket, each with its colour. Used by the alert cards, the timer list and the tickets window.
/// </summary>
public static class EventIcons
{
    private sealed record Spec(string Fill, string? Stroke, Color From, Color To);

    private static readonly Dictionary<string, Spec> Specs = new()
    {
        // events
        ["shugo"] = new("M4 11h16v9H4z M3 7h18v4H3z M10.9 7h2.2v13h-2.2z M12 7c-1.5-4-6-4.5-5.5-1.2C6.8 7 9.5 7 12 7z M12 7c1.5-4 6-4.5 5.5-1.2C17.2 7 14.5 7 12 7z",
            null, C("#FFD36E"), C("#FF8A5B")),
        ["invasion"] = new("M2 15c5-.6 9.5-3.8 12.5-9.5 1.2 4 .3 7-1.6 9.2 3-.9 6.2-.7 9.1 1.2-4 .7-7.3 2.8-9.4 6.1-1.3-3.4-4.7-5.6-10.6-7z",
            null, C("#9AD8FF"), C("#5B7CFF")),
        ["rift"] = new("", "M12 3a9 9 0 1 1-0.01 0 M12 6.5a5.5 5.5 0 1 1-0.01 0 M12 10a2 2 0 1 1-0.01 0", C("#C79BFF"), C("#6B4FD8")),
        ["siege"] = new("M4 21V8h3v3h2V8h2v3h2V8h2v3h2V8h3v13h-6.5v-5a1.5 1.5 0 0 0-3 0v5z", null, C("#FFC27A"), C("#E0703C")),
        ["boss"] = new("M3 17.5 4.8 7.5l4.7 4.6L12 4.5l2.5 7.6 4.7-4.6L21 17.5z M3 19h18v2.2H3z", null, C("#FFE08A"), C("#FF9F59")),
        ["battle"] = new("", "M4 4l10 10 M20 4L10 14 M6.5 13.5l4 4 M17.5 13.5l-4 4 M5 19l2.5-2.5 M19 19l-2.5-2.5", C("#FF8A8A"), C("#E0445E")),
        ["reset"] = new("", "M19.5 12a7.5 7.5 0 1 1-2.2-5.3 M19.5 4v4.8h-4.8", C("#9EF0C8"), C("#3FD18B")),
        ["energy"] = new("M13.5 2 4.5 14h7l-1 8 9-12h-7z", null, C("#E2B8FF"), C("#9B6CFF")),
        ["update"] = new("", "M12 3v12 M7 10.5l5 5 5-5 M5 20h14", C("#9EF0C8"), C("#3FD18B")),
        ["rank"] = new("M7 3h10v2h3.5v2.5A4.5 4.5 0 0 1 16.4 12a5 5 0 0 1-3.4 2.9V18h3.5v3h-9v-3H11v-3.1A5 5 0 0 1 7.6 12 4.5 4.5 0 0 1 3.5 7.5V5H7z M5.5 7v.5a2.5 2.5 0 0 0 1.6 2.3A9 9 0 0 1 7 8V7z M18.5 7H17v1a9 9 0 0 1-.1 1.8 2.5 2.5 0 0 0 1.6-2.3z",
            null, C("#FFE08A"), C("#F2C26B")),
        ["info"] = new("M12 3a6 6 0 0 0-6 6v4l-2 3h16l-2-3V9a6 6 0 0 0-6-6z M9.5 18a2.5 2.5 0 0 0 5 0z", null, C("#BFEFFF"), C("#7FD8F2")),
        // tickets
        ["odyle"] = new("M13.5 2 4.5 14h7l-1 8 9-12h-7z", null, C("#E2B8FF"), C("#9B6CFF")),
        ["nightmare"] = new("M15 3a9 9 0 1 0 6 15.6A8 8 0 0 1 15 3z", null, C("#D3B8FF"), C("#7E5BEF")),
        ["ascension"] = new("M12 2l2.6 6.6L21.5 9l-5.3 4.5 1.7 6.9L12 16.8 6.1 20.4l1.7-6.9L2.5 9l6.9-.4z", null, C("#FFE6A0"), C("#F5B83D")),
        ["daily"] = new("M4 21V10a8 8 0 0 1 16 0v11h-4.5v-7a3.5 3.5 0 0 0-7 0v7z", null, C("#A8E6FF"), C("#4FA8E0")),
        ["shugokey"] = new("M8 4a4.5 4.5 0 1 0 2.8 8l1.2 1.2V15h2v2h2v2h3v-3l-6.7-6.7A4.5 4.5 0 0 0 8 4zm-.5 2.5a1.6 1.6 0 1 1 0 3.2 1.6 1.6 0 0 1 0-3.2z", null, C("#FFD36E"), C("#FF8A5B")),
        ["invasionkey"] = new("M8 4a4.5 4.5 0 1 0 2.8 8l1.2 1.2V15h2v2h2v2h3v-3l-6.7-6.7A4.5 4.5 0 0 0 8 4zm-.5 2.5a1.6 1.6 0 1 1 0 3.2 1.6 1.6 0 0 1 0-3.2z", null, C("#9AD8FF"), C("#5B7CFF")),
    };

    private static readonly Dictionary<string, ImageSource> Cache = new();

    /// <summary>Which icon an event id from timers.json uses.</summary>
    public static string ForEvent(string id) => id switch
    {
        "shugo" => "shugo",
        "invasion" => "invasion",
        "rift" => "rift",
        "siege" or "siegebosses" or "nahma" => "siege",
        "kaira" => "boss",
        "battlefield" => "battle",
        "daily" or "weekly" => "reset",
        _ => "info",
    };

    public static Color Accent(string key) => Specs.TryGetValue(key, out var s) ? s.To : C("#7FD8F2");

    /// <summary>The icon as an image (24 × 24 units), gradient-filled or stroked.</summary>
    public static ImageSource Get(string key)
    {
        if (Cache.TryGetValue(key, out var img)) return img;
        var s = Specs.GetValueOrDefault(key) ?? Specs["info"];
        var brush = new LinearGradientBrush(s.From, s.To, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        var group = new DrawingGroup();
        // a transparent 24 × 24 frame keeps every icon the same size and centred
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        if (s.Fill.Length > 0)
            group.Children.Add(new GeometryDrawing(brush, null, Geometry.Parse(s.Fill)));
        if (s.Stroke is { } stroke)
        {
            var pen = new Pen(brush, 2.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            group.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse(stroke)));
        }
        group.Freeze();
        img = new DrawingImage(group);
        img.Freeze();
        Cache[key] = img;
        return img;
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
