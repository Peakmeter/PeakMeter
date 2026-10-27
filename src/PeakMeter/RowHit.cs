using System.Windows;

namespace PeakMeter;

internal static class RowHit
{
    /// <summary>The row under a click. A click on a name lands on a text run, which is not a FrameworkElement.</summary>
    public static RowVm? RowOf(object? source) => source switch
    {
        FrameworkElement { DataContext: RowVm r } => r,
        FrameworkContentElement { DataContext: RowVm r } => r,
        _ => null,
    };
}
