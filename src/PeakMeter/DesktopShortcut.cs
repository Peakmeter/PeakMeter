using System.IO;

namespace PeakMeter;

/// <summary>
/// The desktop shortcut. The installer makes one, but a player who starts PeakMeter.exe straight from the unzipped
/// folder (or whose antivirus stopped the installer) has none: the meter then offers one, once.
/// </summary>
internal static class DesktopShortcut
{
    private static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static string StartMenu => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");

    /// <summary>True when there is something to offer: no PeakMeter shortcut on the desktop, and a real place to point it at.</summary>
    public static bool Missing()
    {
        try
        {
            if (Environment.ProcessPath is not { } exe) return false;
            // Started from inside a ZIP (Windows unpacks it to Temp): a shortcut there would break tomorrow.
            if (exe.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) return false;
            return Directory.Exists(Desktop) && Directory.GetFiles(Desktop, "PeakMeter*.lnk").Length == 0;
        }
        catch { return false; }
    }

    /// <summary>Puts PeakMeter on the desktop and in the start menu, pointing at the running exe. False when Windows says no.</summary>
    public static bool Create()
    {
        try
        {
            var exe = Environment.ProcessPath!;
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return false;
            dynamic shell = Activator.CreateInstance(shellType)!;
            foreach (var folder in new[] { Desktop, StartMenu })
            {
                if (!Directory.Exists(folder)) continue;
                dynamic lnk = shell.CreateShortcut(Path.Combine(folder, "PeakMeter.lnk"));
                lnk.TargetPath = exe;
                lnk.WorkingDirectory = Path.GetDirectoryName(exe);
                lnk.IconLocation = exe + ",0";
                lnk.Description = "PeakMeter - AION 2 DPS-Meter";
                lnk.Save();
            }
            return File.Exists(Path.Combine(Desktop, "PeakMeter.lnk"));
        }
        catch { return false; }
    }
}
