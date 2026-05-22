using System.Diagnostics;
using System.Text.Json;
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
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

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

    public async Task<bool> LaunchAsync(string? launchArgs = null)
    {
        var playerPath = RobloxPlayerPath;
        if (playerPath == null)
        {
            SetStatus(RobloxStatus.Updating);

            var installed = await InstallRobloxAsync();
            if (!installed)
            {
                OpenDownloadPage();
                SetStatus(RobloxStatus.NotInstalled);
                return false;
            }

            playerPath = RobloxPlayerPath;
            if (playerPath == null)
            {
                OpenDownloadPage();
                SetStatus(RobloxStatus.NotInstalled);
                return false;
            }
        }

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

    private async Task<bool> InstallRobloxAsync()
    {
        try
        {
            var versionGuid = await GetLatestVersionGuidAsync();
            if (string.IsNullOrWhiteSpace(versionGuid))
                return false;

            var downloadsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NexStrap",
                "Downloads");
            Directory.CreateDirectory(downloadsDir);

            var installerPath = Path.Combine(downloadsDir, $"{versionGuid}-RobloxPlayerInstaller.exe");
            if (!File.Exists(installerPath))
            {
                var installerUrl = $"https://setup.rbxcdn.com/{versionGuid}-RobloxPlayerInstaller.exe";
                using var response = await Http.GetAsync(installerUrl);
                response.EnsureSuccessStatusCode();

                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = File.Create(installerPath);
                await input.CopyToAsync(output);
            }

            var process = Process.Start(new ProcessStartInfo(installerPath, "/silent /install")
            {
                UseShellExecute = true
            });

            if (process == null)
                return false;

            try
            {
                await process.WaitForExitAsync();
            }
            catch { }

            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (DateTime.UtcNow < deadline)
            {
                _cachedVersionFolder = null;
                if (IsInstalled())
                    return true;

                await Task.Delay(2000);
            }
        }
        catch { }

        return false;
    }

    private static async Task<string?> GetLatestVersionGuidAsync()
    {
        foreach (var url in new[]
                 {
                     "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer",
                     "https://clientsettings.roblox.com/v2/client-version/WindowsPlayer"
                 })
        {
            try
            {
                var json = await Http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("clientVersionUpload", out var versionProp))
                    return versionProp.GetString();
            }
            catch { }
        }

        return null;
    }

    private static void OpenDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.roblox.com/download",
                UseShellExecute = true
            });
        }
        catch { }
    }
}
