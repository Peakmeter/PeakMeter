using System.Windows.Markup;

namespace PeakMeter;

/// <summary>
/// German / English. Every text is written in both languages right where it is used — <c>L.S("Gruppe", "Party")</c> in
/// code, <c>{local:T De=Gruppe, En=Party}</c> in XAML — so nothing can go missing from a separate table. The language
/// is chosen at start (a switch restarts PeakMeter).
/// </summary>
public static class L
{
    public static bool En { get; set; }

    public static string S(string de, string en) => En ? en : de;

    /// <summary>Dates and weekdays in the interface language.</summary>
    public static System.Globalization.CultureInfo Culture =>
        System.Globalization.CultureInfo.GetCultureInfo(En ? "en-GB" : "de-DE");
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public T() { }

    public T(string de, string en)
    {
        De = de;
        En = en;
    }

    public string De { get; set; } = "";
    public string En { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.S(De, En);
}
