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
    private string? _cachedVersionFolder;

    public RobloxStatus Status { get; private set; } = RobloxStatus.Idle;
    public event EventHandler<RobloxStatus>? StatusChanged;

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
        if (_cachedVersionFolder != null &&
            Directory.Exists(_cachedVersionFolder) &&
            File.Exists(Path.Combine(_cachedVersionFolder, "RobloxPlayerBeta.exe")))
            return _cachedVersionFolder;

        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var robloxVersions = Path.Combine(localApp, "Roblox", "Versions");
        if (!Directory.Exists(robloxVersions)) return null;

        _cachedVersionFolder = Directory.GetDirectories(robloxVersions)
            .Where(d => File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe")))
            .OrderByDescending(d => new DirectoryInfo(d).LastWriteTime)
            .FirstOrDefault();

        return _cachedVersionFolder;
    }

    public Task<bool> LaunchAsync(string? launchArgs = null)
    {
        var playerPath = RobloxPlayerPath;
        if (playerPath == null) return Task.FromResult(false);

        SetStatus(RobloxStatus.Launching);

        var startInfo = new ProcessStartInfo(playerPath)
        {
            UseShellExecute = true,
            Arguments = launchArgs ?? string.Empty
        };

        _robloxProcess = Process.Start(startInfo);
        if (_robloxProcess == null) { SetStatus(RobloxStatus.Idle); return Task.FromResult(false); }

        _ = MonitorProcessAsync(_robloxProcess);
        SetStatus(RobloxStatus.Running);
        return Task.FromResult(true);
    }

    private async Task MonitorProcessAsync(Process process)
    {
        await Task.Run(() => process.WaitForExit());
        SetStatus(RobloxStatus.Idle);
        _robloxProcess = null;
    }

    public bool IsInstalled() => RobloxPlayerPath != null;

    private void SetStatus(RobloxStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
