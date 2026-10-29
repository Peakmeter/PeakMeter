using System.Diagnostics;

namespace PeakMeter;

/// <summary>
/// A player's profile on the PeakMeter website: gear, stats, Daevanion and skills from the official character pages
/// (NCSOFT), together with the player's best kills from the PeakMeter ranking. PeakMeter only opens the link in the
/// browser; the page links on to aion2index.com and the official page.
/// </summary>
public static class Armory
{
    public static string Url(string name, string server, string region)
    {
        var r = Uri.EscapeDataString(region);
        // The region from the settings and the server name give the profile's address; without a server, the search.
        if (server.Trim() is { Length: > 0 } s)
            return $"{Ranking.Url}/player/{r}/{Uri.EscapeDataString(s)}/{Uri.EscapeDataString(name)}";
        return $"{Ranking.Url}/player?region={r}&q={Uri.EscapeDataString(name)}";
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
