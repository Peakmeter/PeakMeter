using System.Diagnostics;

namespace PeakMeter;

/// <summary>
/// A player's public profile (gear, Daevanion boards, skills, combat power history) on aion2index.com, a fan site
/// built from the official character pages. PeakMeter only opens the link in the browser.
/// </summary>
public static class Armory
{
    private const string Site = "https://aion2index.com";

    public static string Url(string name, string server, string region)
    {
        var slug = server.Trim().ToLowerInvariant();
        // Europe has one region code; North America is split East/West, which the meter cannot tell apart → search.
        if (region == "eu" && slug.Length > 0 && slug.All(char.IsAsciiLetter))
            return $"{Site}/c/eu/{slug}/{Uri.EscapeDataString(name)}";
        return $"{Site}/search?q={Uri.EscapeDataString(name)}" + (region == "eu" ? "&region=eu" : "");
    }

    public static void Open(string name, string server, string region)
    {
        if (name.Length == 0) return;
        try { Process.Start(new ProcessStartInfo(Url(name, server, region)) { UseShellExecute = true }); } catch { }
    }
}
