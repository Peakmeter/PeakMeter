using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace PeakMeter;

/// <summary>
/// Updates from the GitHub releases of <see cref="App.GitHubRepo"/>: the newest release's
/// <c>PeakMeter-v*-win-x64.zip</c> is downloaded, checked against the SHA-256 published next to it, unpacked and handed
/// to its own installer, which replaces this copy and starts the new one. Nothing else is sent: one request to
/// api.github.com with the user agent "PeakMeter/&lt;version&gt;".
/// </summary>
public sealed class Updater : IDisposable
{
    private static readonly HttpClient Http = CreateClient();
    private readonly Settings _settings;
    private readonly System.Threading.Timer _timer;
    private bool _installing;

    public Updater(Settings settings)
    {
        _settings = settings;
        // First look 30 s after start (not while the game loads), then every 3 hours.
        _timer = new System.Threading.Timer(_ => _ = Tick(), null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(3));
    }

    public string? AvailableVersion { get; private set; }
    private string? _zipUrl, _shaUrl;

    /// <summary>Raised (worker thread) when a newer version was found.</summary>
    public event Action<string>? Available;

    /// <summary>Raised (worker thread) with a short status line ("Lade Update …", errors).</summary>
    public event Action<string>? Status;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"PeakMeter/{App.Version}");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    private async Task Tick()
    {
        try
        {
            if (await Check() && _settings.AutoUpdate && !GameRunning()) await Install();
        }
        catch { }
    }

    /// <summary>Asks GitHub for the newest release. True when it is newer than this copy.</summary>
    public async Task<bool> Check()
    {
        if (App.GitHubRepo.Length == 0) return false;
        using var resp = await Http.GetAsync($"https://api.github.com/repos/{App.GitHubRepo}/releases/latest");
        if (!resp.IsSuccessStatusCode) return false;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
        if (!Version.TryParse(tag, out var latest) || !Version.TryParse(App.Version, out var mine) || latest <= mine) return false;

        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var url = a.GetProperty("browser_download_url").GetString();
            if (name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)) _zipUrl = url;
            if (name.EndsWith(".sha256.txt", StringComparison.OrdinalIgnoreCase)) _shaUrl = url;
        }
        if (_zipUrl is null) return false;
        var isNew = AvailableVersion != tag;
        AvailableVersion = tag;
        if (isNew) Available?.Invoke(tag);
        return true;
    }

    /// <summary>Downloads, verifies and installs the available version; PeakMeter is closed and restarted by the installer.</summary>
    public async Task Install()
    {
        if (_installing || _zipUrl is null) return;
        _installing = true;
        try
        {
            Status?.Invoke(L.S("Lade Update …", "Downloading update …"));
            var dir = Path.Combine(Path.GetTempPath(), "PeakMeter-update");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, "update.zip");
            await using (var s = await Http.GetStreamAsync(_zipUrl))
            await using (var f = File.Create(zip))
                await s.CopyToAsync(f);

            // The release lists the zip's SHA-256: a damaged or swapped download is never installed.
            if (_shaUrl is not null)
            {
                var expected = (await Http.GetStringAsync(_shaUrl)).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                await using var f = File.OpenRead(zip);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(f));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    Status?.Invoke(L.S("Update beschädigt – nicht installiert", "Update damaged – not installed"));
                    return;
                }
            }

            var files = Path.Combine(dir, "files");
            ZipFile.ExtractToDirectory(zip, files);
            var installer = Path.Combine(files, "install.ps1");
            if (!File.Exists(installer)) return;
            Status?.Invoke(L.S("Installiere Update …", "Installing update …"));
            _settings.Save();
            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{installer}\" -Silent")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            // The installer closes this process and starts the new version.
        }
        catch (Exception ex)
        {
            Status?.Invoke(L.S("Update fehlgeschlagen: ", "Update failed: ") + ex.Message);
        }
        finally
        {
            _installing = false;
        }
    }

    /// <summary>Installs once the game is closed (called every minute by the overlay's clock while one waits).</summary>
    public void InstallWhenIdle()
    {
        if (_settings.AutoUpdate && AvailableVersion is not null && !_installing && !GameRunning()) _ = Install();
    }

    private static bool GameRunning() =>
        Process.GetProcessesByName("AION2").Length > 0 || Process.GetProcessesByName("Aion2").Length > 0;

    public void Dispose() => _timer.Dispose();
}
