using System.Diagnostics;

namespace NexStrap.Services;

public sealed class RobloxStockInstallFallbackService(RobloxInstallStateService installState)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static readonly string StockRobloxVersionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "Versions");

    private static readonly string DownloadsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "Downloads");

    static RobloxStockInstallFallbackService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
    }

    internal string? FindStockRobloxVersionFolder(string? targetGuid = null)
        => installState.FindStockRobloxVersionFolder(targetGuid);

    internal async Task CopyDirectoryAsync(
        string source,
        string dest,
        RobloxProgressReporter reportProgress)
    {
        await Task.Run(() =>
        {
            var allFiles = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            var done = 0;
            foreach (var file in allFiles)
            {
                var rel      = Path.GetRelativePath(source, file);
                var destFile = Path.Combine(dest, rel);
                var destDir  = Path.GetDirectoryName(destFile);
                if (destDir != null) Directory.CreateDirectory(destDir);
                File.Copy(file, destFile, overwrite: true);
                done++;
                var pct = allFiles.Length > 0 ? done / (double)allFiles.Length * 100.0 : 0;
                reportProgress($"Copying {Path.GetFileName(file)}", pct);
            }
        });
        RobloxService.Log("Directory copy complete");
    }

    internal async Task RunOfficialInstallerAsync()
    {
        RobloxService.Log("CDN download failed, falling back to official installer");
        var installerPath = FindFileInStockRoblox("RobloxPlayerInstaller.exe");

        if (installerPath == null)
        {
            RobloxService.Log("Downloading official installer...");
            var installerDir = Path.Combine(DownloadsDir, "installer");
            Directory.CreateDirectory(installerDir);
            var installerExe = Path.Combine(installerDir, "RobloxPlayerInstaller.exe");
            try
            {
                var bytes = await Http.GetByteArrayAsync("https://setup.rbxcdn.com/RobloxPlayerInstaller.exe");
                await File.WriteAllBytesAsync(installerExe, bytes);
                installerPath = installerExe;
            }
            catch (Exception ex)
            {
                RobloxService.Log($"Failed to download official installer: {ex.Message}");
                return;
            }
        }

        var existingPids = Process.GetProcessesByName("RobloxPlayerBeta")
            .Select(p => p.Id).ToHashSet();

        RobloxService.Log($"Running official installer: {installerPath}");
        var proc = Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute  = false,
            WorkingDirectory = Path.GetDirectoryName(installerPath)!,
            CreateNoWindow   = true,
            WindowStyle      = ProcessWindowStyle.Hidden
        });
        if (proc != null)
        {
            await proc.WaitForExitAsync();
            RobloxService.Log($"Official installer exited with code {proc.ExitCode}");
        }

        foreach (var roblox in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            if (existingPids.Contains(roblox.Id)) continue;
            try
            {
                roblox.Kill();
                RobloxService.Log($"Killed installer-spawned Roblox (PID {roblox.Id})");
            }
            catch { }
        }
    }

    private static string? FindFileInStockRoblox(string filename)
    {
        if (!Directory.Exists(StockRobloxVersionsDir)) return null;
        return Directory.GetDirectories(StockRobloxVersionsDir)
            .Select(d => Path.Combine(d, filename))
            .FirstOrDefault(File.Exists);
    }
}
