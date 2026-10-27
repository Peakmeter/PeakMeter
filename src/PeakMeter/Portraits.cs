using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PeakMeter;

/// <summary>
/// The character's portrait from NCSoft's official character search (the one on the AION 2 website): the search
/// by name and server gives the image address, the image comes from NCSoft's profile image server.
/// Kept in %AppData%\PeakMeter\portraits and refreshed after a few days, as players change their portrait.
/// Taiwan has no public search, so there it stays the class emblem.
/// </summary>
public static class Portraits
{
    private const string Search = "https://api-search.plaync.com/";
    private const string Images = "https://profileimg.plaync.com";
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(3);
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();
    private static readonly ConcurrentDictionary<string, byte> Loading = new();

    /// <summary>Raised (on the UI thread) when a portrait finished loading.</summary>
    public static event Action? Loaded;

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("PeakMeter");
        return h;
    }

    private static string Dir => Path.Combine(Settings.Dir, "portraits");

    /// <summary>The portrait when it is ready (else null; it loads in the background and <see cref="Loaded"/> fires).</summary>
    public static ImageSource? Get(string? name, int serverId, string region)
    {
        if (string.IsNullOrEmpty(name) || serverId <= 0 || SearchUrl(name, serverId, region) is null) return null;
        var key = $"{serverId}_{name}";
        var file = Path.Combine(Dir, Safe(key) + ".img");
        if (!Cache.TryGetValue(key, out var img) || img is null)
        {
            try { img = File.Exists(file) ? Decode(File.ReadAllBytes(file)) : null; } catch (IOException) { }
            if (img is not null) Cache[key] = img;
        }
        // At most one lookup per character and session (Loading is never cleared).
        var stale = !File.Exists(file) || DateTime.Now - File.GetLastWriteTime(file) > MaxAge;
        if (stale && Loading.TryAdd(key, 0)) _ = Download(key, name, serverId, region, file);
        return img;
    }

    private static string? SearchUrl(string name, int serverId, string region)
    {
        var q = $"keyword={Uri.EscapeDataString(name)}&serverId={serverId}&page=1&size=30";
        return region switch
        {
            "eu" => $"{Search}aion2global/search/v2/character?{q}&region=eu&localeInfo=de-DE",
            "nae" or "naw" => $"{Search}aion2global/search/v2/character?{q}&region={region}&localeInfo=en-US",
            "kr" => $"{Search}aion2/search/v2/character?{q}",
            _ => null,
        };
    }

    private static async Task Download(string key, string name, int serverId, string region, string file)
    {
        try
        {
            var json = await Http.GetStringAsync(SearchUrl(name, serverId, region));
            using var doc = JsonDocument.Parse(json);
            string? path = null;
            foreach (var c in doc.RootElement.GetProperty("list").EnumerateArray())
            {
                // The search marks the matched part with <strong> tags and also finds longer names.
                var found = Regex.Replace(c.GetProperty("name").GetString() ?? "", "<[^>]+>", "");
                if (!string.Equals(found, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (c.TryGetProperty("serverId", out var s) && s.GetInt32() != serverId) continue;
                path = c.GetProperty("profileImageUrl").GetString();
                break;
            }
            if (path is null || !path.StartsWith('/')) return;
            var bytes = await Http.GetByteArrayAsync(Images + path);
            if (Decode(bytes) is not { } img) return;
            Directory.CreateDirectory(Dir);
            await File.WriteAllBytesAsync(file, bytes);
            Cache[key] = img;
            Application.Current?.Dispatcher.BeginInvoke(() => Loaded?.Invoke());
        }
        catch { } // offline, search down or no such character: the class emblem stays
    }

    private static string Safe(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.DecodePixelWidth = 96;
            b.StreamSource = new MemoryStream(bytes);
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch
        {
            return null;
        }
    }
}
