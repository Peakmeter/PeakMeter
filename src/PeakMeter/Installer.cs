using System.Diagnostics;
using System.IO;

namespace PeakMeter;

/// <summary>
/// PeakMeter installs and updates itself: no script, no PowerShell. Started from the unzipped folder it offers to copy
/// itself to %LocalAppData%\Programs\PeakMeter (no admin rights) with a desktop and start menu shortcut. The updater
/// starts the new version's exe with <c>--install-update</c>, which waits for the old one to close, copies itself over
/// it and starts the installed copy.
/// </summary>
internal static class Installer
{
    /// <summary>Test aid: install into this folder instead, with no shortcuts and no start (never set for players).</summary>
    private static readonly string? TestDir = Environment.GetEnvironmentVariable("PEAKMETER_INSTALL_TEST");

    public static string Dir => TestDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PeakMeter");
    public static string InstalledExe => Path.Combine(Dir, "PeakMeter.exe");

    private static readonly string[] Items = ["PeakMeter.exe", "data", "LICENSE", "README.txt"];

    /// <summary>The folder holds the game data (boss, dungeon and skill names) the meter needs next to its exe.</summary>
    public static bool HasData(string folder) =>
        File.Exists(Path.Combine(folder, "data", "npcs", "en.json")) && File.Exists(Path.Combine(folder, "data", "dungeons", "en.json"));

    /// <summary>True when this exe is the installed copy.</summary>
    public static bool IsInstalledCopy() =>
        Environment.ProcessPath is { } exe && string.Equals(Path.GetFullPath(exe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>Started from inside a ZIP (Windows unpacks it to Temp): installing from there works, but the folder vanishes.</summary>
    public static bool RunsFromTemp() =>
        Environment.ProcessPath is { } exe && exe.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Copies this exe's folder (exe, data, license, readme) to <see cref="Dir"/>, makes the shortcuts and starts the
    /// installed copy. <paramref name="waitPid"/>: the old meter that is closing (0 = none). False when the files could
    /// not be replaced.
    /// </summary>
    public static bool Install(int waitPid, bool startAfter)
    {
        var src = AppContext.BaseDirectory;
        if (IsInstalledCopy()) return true;
        if (!HasData(src)) return false; // never install an exe without its boss and dungeon data
        CloseOldCopy(waitPid);

        Directory.CreateDirectory(Dir);
        var copied = false;
        for (var attempt = 0; attempt < 20 && !copied; attempt++)
        {
            try
            {
                foreach (var item in Items)
                {
                    var from = Path.Combine(src, item);
                    var to = Path.Combine(Dir, item);
                    if (File.Exists(from)) File.Copy(from, to, overwrite: true);
                    else if (Directory.Exists(from)) CopyFolder(from, to);
                }
                // The German-only readme of older versions: README.txt (English and German) replaces it.
                var old = Path.Combine(Dir, "LIESMICH.txt");
                if (File.Exists(old)) File.Delete(old);
                copied = true;
            }
            catch (IOException) { Thread.Sleep(750); }
            catch (UnauthorizedAccessException) { Thread.Sleep(750); }
        }
        if (!copied) return false;

        if (TestDir is not null) return true;
        DesktopShortcut.Create(InstalledExe);
        if (startAfter)
            Process.Start(new ProcessStartInfo(InstalledExe, "--restart") { UseShellExecute = false, WorkingDirectory = Dir });
        return true;
    }

    /// <summary>The meter being replaced: the one that asked for the update, and any copy running from the install folder.</summary>
    private static void CloseOldCopy(int waitPid)
    {
        var self = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("PeakMeter"))
        {
            using (p)
            {
                if (p.Id == self) continue;
                try
                {
                    var path = p.MainModule?.FileName;
                    var isOld = p.Id == waitPid ||
                                (path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase));
                    if (!isOld) continue;
                    // Give it a moment to close by itself (it saves its settings first), then end it.
                    if (!p.WaitForExit(p.Id == waitPid ? 15_000 : 0))
                    {
                        p.Kill();
                        p.WaitForExit(10_000);
                    }
                }
                catch { }
            }
        }
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(from)) CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
