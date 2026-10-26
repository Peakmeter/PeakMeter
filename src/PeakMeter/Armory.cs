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
        // The region from the settings (eu / nae / naw) and the server name give the profile's address.
        if (region is "eu" or "nae" or "naw" && slug.Length > 0 && slug.All(char.IsAsciiLetter))
            return $"{Site}/c/{region}/{slug}/{Uri.EscapeDataString(name)}";
        return $"{Site}/search?q={Uri.EscapeDataString(name)}" + (region == "eu" ? "&region=eu" : "");
    }

    /// <summary>"[Zik]" like the game shows it: the first three letters of a server that is not your own.</summary>
    public static string Tag(string server, string ownServer) =>
        server.Length >= 3 && server.All(char.IsLetter) && !string.Equals(server, ownServer, StringComparison.OrdinalIgnoreCase) ? $" [{server[..3]}]" : "";

    public static void Open(string name, string server, string region)
    {
        if (name.Length == 0) return;
        try { Process.Start(new ProcessStartInfo(Url(name, server, region)) { UseShellExecute = true }); } catch { }
    }
}
