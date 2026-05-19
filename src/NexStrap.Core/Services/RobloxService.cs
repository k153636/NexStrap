using System.Diagnostics;
using Microsoft.Win32;

namespace NexStrap.Core.Services;

public enum RobloxStatus
{
    NotInstalled,
    Idle,
    Launching,
    Running,
    Updating
}

public class RobloxService
{
    private Process? _robloxProcess;
    private FileSystemWatcher? _watcher;

    public RobloxStatus Status { get; private set; } = RobloxStatus.Idle;
    public event EventHandler<RobloxStatus>? StatusChanged;
    public event EventHandler<int>? FpsChanged;

    public string? RobloxPlayerPath => FindRobloxPath();
    public string? RobloxVersionPath => FindVersionFolder();

    public string ClientSettingsPath
    {
        get
        {
            var versionPath = FindVersionFolder();
            if (versionPath == null) return string.Empty;
            return Path.Combine(versionPath, "ClientSettings");
        }
    }

    public string ContentPath
    {
        get
        {
            var versionPath = FindVersionFolder();
            if (versionPath == null) return string.Empty;
            return Path.Combine(versionPath, "content");
        }
    }

    private string? FindRobloxPath()
    {
        var versions = FindVersionFolder();
        if (versions == null) return null;
        var exe = Path.Combine(versions, "RobloxPlayerBeta.exe");
        return File.Exists(exe) ? exe : null;
    }

    private string? FindVersionFolder()
    {
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var robloxVersions = Path.Combine(localApp, "Roblox", "Versions");
        if (!Directory.Exists(robloxVersions)) return null;

        var dirs = Directory.GetDirectories(robloxVersions)
            .Where(d => File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe")))
            .OrderByDescending(d => new DirectoryInfo(d).LastWriteTime)
            .ToArray();

        return dirs.FirstOrDefault();
    }

    public async Task<bool> LaunchAsync(string? launchArgs = null)
    {
        var playerPath = RobloxPlayerPath;
        if (playerPath == null) return false;

        SetStatus(RobloxStatus.Launching);

        var startInfo = new ProcessStartInfo(playerPath)
        {
            UseShellExecute = true,
            Arguments = launchArgs ?? string.Empty
        };

        _robloxProcess = Process.Start(startInfo);
        if (_robloxProcess == null) { SetStatus(RobloxStatus.Idle); return false; }

        _ = MonitorProcessAsync(_robloxProcess);
        SetStatus(RobloxStatus.Running);
        return true;
    }

    public async Task LaunchMultipleInstanceAsync()
    {
        await SetMultiInstanceRegistryAsync(true);
        await LaunchAsync();
    }

    private async Task SetMultiInstanceRegistryAsync(bool enable)
    {
        await Task.Run(() =>
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", true);
                var playerPath = RobloxPlayerPath;
                if (playerPath == null) return;

                if (enable)
                    key?.SetValue(playerPath, "~ DISABLEUSERCALLBACKEXCEPTION");
                else
                    key?.DeleteValue(playerPath, false);
            }
            catch { }
        });
    }

    private async Task MonitorProcessAsync(Process process)
    {
        await Task.Run(() => process.WaitForExit());
        SetStatus(RobloxStatus.Idle);
        _robloxProcess = null;
    }

    public bool IsRobloxRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("RobloxPlayerBeta");
            return processes.Any(p => !p.HasExited);
        }
        catch { return false; }
    }

    public bool IsInstalled() => RobloxPlayerPath != null;

    private void SetStatus(RobloxStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
