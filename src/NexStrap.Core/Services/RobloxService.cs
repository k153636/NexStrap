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

public record BootstrapperProgress(string Message, double Percent, bool IsIndeterminate = false);

public class RobloxService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    private Process? _robloxProcess;
    private string? _cachedVersionFolder;
    private CancellationTokenSource? _installCts;

    public RobloxStatus Status { get; private set; } = RobloxStatus.Idle;
    public event EventHandler<RobloxStatus>? StatusChanged;
    public event EventHandler<BootstrapperProgress>? BootstrapperProgress;

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

            _installCts = new CancellationTokenSource();
            var installed = await InstallRobloxAsync(_installCts.Token);
            _installCts.Dispose();
            _installCts = null;

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

    public void CancelInstall() => _installCts?.Cancel();

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

    private void ReportProgress(string message, double percent, bool indeterminate = false)
        => BootstrapperProgress?.Invoke(this, new BootstrapperProgress(message, percent, indeterminate));

    private async Task<bool> InstallRobloxAsync(CancellationToken ct = default)
    {
        try
        {
            ReportProgress("Checking for updates...", 0, indeterminate: true);

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

                using var response = await Http.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                await using var input  = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(installerPath);

                var buffer      = new byte[81920];
                long downloaded = 0;
                int  read;

                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;

                    if (totalBytes > 0)
                    {
                        var percent = (double)downloaded / totalBytes * 100.0;
                        var mb      = downloaded / 1_048_576.0;
                        var totalMb = totalBytes / 1_048_576.0;
                        ReportProgress($"Downloading Roblox... {mb:F1} / {totalMb:F1} MB", percent);
                    }
                    else
                    {
                        var mb = downloaded / 1_048_576.0;
                        ReportProgress($"Downloading Roblox... {mb:F1} MB", 0, indeterminate: true);
                    }
                }
            }

            if (ct.IsCancellationRequested) return false;

            ReportProgress("Installing Roblox...", 100, indeterminate: true);

            var process = Process.Start(new ProcessStartInfo(installerPath, "/silent /install")
            {
                UseShellExecute = true
            });

            if (process == null)
                return false;

            try { await process.WaitForExitAsync(ct); }
            catch { }

            if (ct.IsCancellationRequested) return false;

            ReportProgress("Waiting for Roblox...", 100, indeterminate: true);

            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (DateTime.UtcNow < deadline)
            {
                _cachedVersionFolder = null;
                if (IsInstalled())
                    return true;

                await Task.Delay(2000, ct);
            }
        }
        catch (OperationCanceledException) { }
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
