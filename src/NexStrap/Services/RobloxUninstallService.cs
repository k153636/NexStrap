using System.Diagnostics;
using Microsoft.Win32;

namespace NexStrap.Services;

public static class RobloxUninstallService
{
    public static async Task UninstallNexStrapRobloxAsync()
    {
        foreach (var proc in Process.GetProcessesByName("RobloxPlayerBeta"))
            try { proc.Kill(); await proc.WaitForExitAsync(); } catch { }

        var versionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexStrap", "Versions");
        var downloadsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexStrap", "Downloads");

        await Task.Run(() =>
        {
            if (Directory.Exists(versionsDir))
                try { Directory.Delete(versionsDir, recursive: true); } catch { }
            if (Directory.Exists(downloadsDir))
                try { Directory.Delete(downloadsDir, recursive: true); } catch { }
        });
    }

    public static async Task UninstallStockRobloxAsync()
    {
        foreach (var name in new[] { "RobloxPlayerBeta", "RobloxPlayerLauncher", "RobloxStudioBeta" })
            foreach (var proc in Process.GetProcessesByName(name))
                try { proc.Kill(); await proc.WaitForExitAsync(); } catch { }

        await Task.Run(() =>
        {
            var robloxDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");
            if (Directory.Exists(robloxDir))
                try { Directory.Delete(robloxDir, recursive: true); } catch { }

            foreach (var key in new[]
            {
                @"Software\Classes\roblox",
                @"Software\Classes\roblox-player",
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-player",
                @"Software\Roblox",
            })
                try { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); } catch { }
        });
    }
}
