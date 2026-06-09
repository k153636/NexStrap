using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace NexStrap.Services;

public class StudioService
{
    private readonly StudioAppSettingsService _appSettings;
    private readonly StudioVersionCleanupService _versionCleanup;
    private readonly StudioVersionManifestService _versionManifest;
    private readonly StudioPackageManifestService _packageManifest;
    private readonly StudioPackageInstallerService _packageInstaller;
    private readonly StudioCdnConnectivityService _cdnConnectivity;

    private static readonly string VersionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "Versions");

    // Roblox 標準パスに固定名で配置（GUID ではないため公式インストーラーと競合しない）
    private static readonly string StudioVersionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "Versions");
    private static readonly string StudioDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "Versions", "WindowsStudio64");

    private static readonly string DownloadsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "Downloads");

    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "studio-state.json");

    private string  _cdnBaseUrl = "https://setup.rbxcdn.com";

    private readonly SemaphoreSlim _installLock = new(1, 1);
    private CancellationTokenSource? _installCts;

    public RobloxStatus Status { get; private set; } = RobloxStatus.Idle;
    public event EventHandler<RobloxStatus>?         StatusChanged;
    public event EventHandler<BootstrapperProgress>? BootstrapperProgress;

    private sealed record StudioStateFile(string VersionGuid, string VersionPath);

    public StudioService(
        StudioAppSettingsService appSettings,
        StudioVersionCleanupService versionCleanup,
        StudioVersionManifestService versionManifest,
        StudioPackageManifestService packageManifest,
        StudioPackageInstallerService packageInstaller,
        StudioCdnConnectivityService cdnConnectivity)
    {
        _appSettings      = appSettings;
        _versionCleanup   = versionCleanup;
        _versionManifest  = versionManifest;
        _packageManifest  = packageManifest;
        _packageInstaller = packageInstaller;
        _cdnConnectivity  = cdnConnectivity;
    }

    // -------------------------------------------------------------------------
    // Public surface
    // -------------------------------------------------------------------------
    public bool IsInstalled() => FindStudioExePath() != null;

    public string? StudioExePath => FindStudioExePath();

    public string? StudioVersionPath
    {
        get
        {
            var exe = FindStudioExePath();
            return exe != null ? Path.GetDirectoryName(exe) : null;
        }
    }

    public string ClientSettingsPath
    {
        get
        {
            var vp = StudioVersionPath;
            return vp == null ? string.Empty : Path.Combine(vp, "ClientSettings");
        }
    }

    private static bool IsVersionComplete(string dir) =>
        File.Exists(Path.Combine(dir, "RobloxStudioBeta.exe"));

    private string? FindStudioExePath()
    {
        // 固定パス (WindowsStudio64) を優先チェック
        if (IsVersionComplete(StudioDir))
            return Path.Combine(StudioDir, "RobloxStudioBeta.exe");

        // state ファイルによるフォールバック
        var state = LoadState();
        if (state != null && IsVersionComplete(state.VersionPath))
            return Path.Combine(state.VersionPath, "RobloxStudioBeta.exe");

        return null;
    }

    // -------------------------------------------------------------------------
    // Launch
    // -------------------------------------------------------------------------
    public async Task<bool> LaunchAsync()
    {
        // インストール済みでも常にアップデートチェックを await してから起動。
        // アップデートなし時は GetLatestVersionGuidCachedAsync のキャッシュが効くためほぼ即時。
        var versionPath = await InstallOrUpdateAsync();
        var exePath = versionPath != null
            ? Path.Combine(versionPath, "RobloxStudioBeta.exe")
            : null;

        if (exePath == null || !File.Exists(exePath)) { SetStatus(RobloxStatus.Idle); return false; }

        SetStatus(RobloxStatus.Launching);
        try
        {
            var studioDir = Path.GetDirectoryName(exePath)!;

            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute  = true,
                WorkingDirectory = studioDir
            };
            Process.Start(psi);
            SetStatus(RobloxStatus.Running);
            return true;
        }
        catch
        {
            SetStatus(RobloxStatus.Idle);
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Font Registration
    // -------------------------------------------------------------------------

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResource(string lpszFilename);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_FONTCHANGE = 0x001D;
    private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

    private static void RegisterStudioFonts(string studioDir)
    {
        var fontsDir = Path.Combine(studioDir, "StudioFonts");
        if (!Directory.Exists(fontsDir)) return;

        // Windows ユーザーフォントフォルダへ永続インストール。
        // GDI・DirectWrite・Qt すべてが参照するため最も確実な方法。
        InstallToUserFonts(fontsDir);

        // AddFontResource（フラグなし）で GDI に登録。
        // FR_NOT_ENUM を使うと EnumFontFamiliesEx から隠れ Qt が検出できなくなるため使わない。
        // .ttc（NotoSansCJK 等）はシステム日本語フォントと競合するため除外する
        var registered = 0;
        try
        {
            foreach (var file in Directory.GetFiles(fontsDir))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".otf" or ".ttf" && AddFontResource(file) > 0)
                    registered++;
            }
        }
        catch { }

        if (registered > 0)
        {
            SendMessage(HWND_BROADCAST, WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero);
            Logger.Instance.Info("Studio", $"フォント {registered} 個を GDI に登録（列挙可能）");
        }
    }

    private static void InstallToUserFonts(string fontsDir)
    {
        var userFontsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Fonts");

        try { Directory.CreateDirectory(userFontsDir); }
        catch { return; }

        const string regPath = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";
        using var regKey = Registry.CurrentUser.OpenSubKey(regPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(regPath);
        if (regKey == null) return;

        var installed = 0;
        foreach (var src in Directory.GetFiles(fontsDir))
        {
            var ext = Path.GetExtension(src).ToLowerInvariant();
            // .ttc（NotoSansCJK 等）はシステム登録するとシステム日本語フォントと競合するため除外。
            // Studio が内部で addApplicationFont() を通じて自分でロードする。
            if (ext is not (".ttf" or ".otf")) continue;

            var fileName = Path.GetFileName(src);
            var dst      = Path.Combine(userFontsDir, fileName);
            if (File.Exists(dst)) continue;

            try
            {
                File.Copy(src, dst);
                var typeSuffix = ext == ".otf" ? " (OpenType)" : " (TrueType)";
                regKey.SetValue(Path.GetFileNameWithoutExtension(fileName) + typeSuffix, dst);
                installed++;
            }
            catch { }
        }

        if (installed > 0)
            Logger.Instance.Info("Studio", $"フォント {installed} 個をユーザーフォントフォルダにインストールしました: {userFontsDir}");
        else
        {
            Logger.Instance.Info("Studio", "ユーザーフォント: 全フォントインストール済み");
        }
    }

    // -------------------------------------------------------------------------
    // Install / Update
    // -------------------------------------------------------------------------
    public async Task<string?> InstallOrUpdateAsync(bool forceReinstall = false)
    {
        var guid = await GetLatestVersionGuidCachedAsync();
        Logger.Instance.Info("Studio", $"最新 GUID: {guid ?? "(取得失敗)"}");
        if (string.IsNullOrWhiteSpace(guid)) return null;

        var currentState  = LoadState();
        var installedGuid = currentState?.VersionGuid;
        Logger.Instance.Info("Studio", $"インストール済み GUID: {installedGuid ?? "(なし)"}");

        if (!forceReinstall && installedGuid == guid && IsVersionComplete(currentState!.VersionPath))
        {
            Logger.Instance.Info("Studio", "最新版インストール済み → スキップ");
            return currentState.VersionPath;
        }

        Logger.Instance.Info("Studio", forceReinstall ? "強制再インストール" : $"アップデート開始: {installedGuid} → {guid}");
        SetStatus(RobloxStatus.Updating);
        return await InstallVersionAsync(guid, forceReinstall);
    }

    public Task<bool> ReinstallAsync() =>
        InstallOrUpdateAsync(forceReinstall: true).ContinueWith(t => t.Result != null);

    private async Task<string?> InstallVersionAsync(string versionGuid, bool forceReinstall = false)
    {
        await _installLock.WaitAsync();
        try
        {
            var versionDir = StudioDir; // WindowsStudio64 固定パス

            if (forceReinstall && Directory.Exists(versionDir))
                try { Directory.Delete(versionDir, recursive: true); } catch { }

            if (IsVersionComplete(versionDir))
            {
                SaveState(versionGuid, versionDir);
                return Path.Combine(versionDir, "RobloxStudioBeta.exe");
            }

            _installCts = new CancellationTokenSource();
            var ok = await DownloadAndInstallAsync(versionGuid, versionDir, _installCts.Token);
            _installCts.Dispose();
            _installCts = null;

            if (!IsVersionComplete(versionDir)) { SetStatus(RobloxStatus.Idle); return null; }

            SaveState(versionGuid, versionDir);
            // 固定パス方式ではバージョン別ディレクトリが存在しないためクリーンアップ不要
            RobloxService.Log($"Studio installation complete: {versionDir}");
            return Path.Combine(versionDir, "RobloxStudioBeta.exe");
        }
        finally
        {
            _installLock.Release();
        }
    }

    public void CancelInstall() => _installCts?.Cancel();

    // -------------------------------------------------------------------------
    // Download & Install
    // -------------------------------------------------------------------------
    private async Task<bool> DownloadAndInstallAsync(string versionGuid, string versionDir,
        CancellationToken ct)
    {
        const double DlStart  = 6.0;
        const double DlEnd    = 88.0;
        const double ExtStart = 88.0;
        const double ExtEnd   = 99.0;

        try
        {
            ReportProgress("Connecting to CDN...", 0);
            _cdnBaseUrl = await _cdnConnectivity.TestConnectivityAsync(ct) ?? "https://setup.rbxcdn.com";

            ReportProgress("Fetching package list...", 3);
            var packages = await FetchManifestAsync(versionGuid, ct);
            if (packages == null || packages.Count == 0)
            {
                ReportProgress("CDN unavailable", 0, indeterminate: true);
                return false;
            }

            if (Directory.Exists(versionDir))
                try { Directory.Delete(versionDir, recursive: true); } catch { }

            Directory.CreateDirectory(versionDir);
            Directory.CreateDirectory(DownloadsDir);

            _packageInstaller.ResetDownloadProgress(packages.Sum(p => p.CompressedSize));

            var downloadStart   = DateTime.UtcNow;
            var downloadedPaths = new List<(string Path, string Name)>();

            var progressTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            var progressTask  = Task.Run(async () =>
            {
                try
                {
                    while (await progressTimer.WaitForNextTickAsync(ct))
                    {
                        var dl      = _packageInstaller.TotalDownloadedBytes;
                        var elapsed = (DateTime.UtcNow - downloadStart).TotalSeconds;
                        var speed   = elapsed > 0.1 ? dl / elapsed : 0;
                        var total   = _packageInstaller.TotalPackedBytes;
                        var ratio   = total > 0 ? dl / (double)total : 0;
                        var overall = DlStart + ratio * (DlEnd - DlStart);
                        var name    = _packageInstaller.CurrentPackageName;
                        ReportProgress(string.IsNullOrEmpty(name) ? "Downloading..." : $"Downloading {name}",
                            overall, detail: FormatSpeed(speed));
                    }
                }
                catch (OperationCanceledException) { }
            });

            foreach (var pkg in packages)
            {
                if (ct.IsCancellationRequested) break;
                _packageInstaller.SetCurrentPackageName(pkg.Name);
                var localPath = Path.Combine(DownloadsDir, pkg.Signature);
                await _packageInstaller.DownloadPackageAsync(pkg, localPath, _cdnBaseUrl, versionGuid, ct);
                downloadedPaths.Add((localPath, pkg.Name));
            }
            _packageInstaller.SetCurrentPackageName(string.Empty);

            progressTimer.Dispose();
            try { await progressTask; } catch { }

            if (ct.IsCancellationRequested) return false;

            await _packageInstaller.CountExtractFilesAsync(downloadedPaths, ct);

            await Task.WhenAll(downloadedPaths.Select(item =>
                Task.Run(() => _packageInstaller.ExtractPackage(
                    item.Path, item.Name, versionDir, ExtStart, ExtEnd, ReportProgress), ct)));

            ReportProgress("Configuring...", 99);
            await _appSettings.WriteAppSettingsAsync(versionDir, ct);

            ReportProgress("Done", 100);
            return File.Exists(Path.Combine(versionDir, "RobloxStudioBeta.exe"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            RobloxService.Log($"Studio DownloadAndInstall failed: {ex.Message}");
            ReportProgress("Installation failed", 0, indeterminate: true);
        }
        return false;
    }

    private async Task<List<RobloxPackage>?> FetchManifestAsync(string versionGuid, CancellationToken ct)
    {
        var manifest = await _packageManifest.FetchManifestAsync(
            versionGuid,
            _cdnBaseUrl,
            _cdnConnectivity.CdnMirrorBaseUrls,
            ct);
        if (manifest != null)
        {
            _cdnBaseUrl = manifest.CdnBaseUrl;
            return manifest.Packages;
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // Version GUID
    // -------------------------------------------------------------------------
    private async Task<string?> GetLatestVersionGuidCachedAsync()
    {
        return await _versionManifest.GetLatestVersionGuidCachedAsync();
    }

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------
    private static StudioStateFile? LoadState()
    {
        try
        {
            if (!File.Exists(StateFilePath)) return null;
            return JsonSerializer.Deserialize<StudioStateFile>(File.ReadAllText(StateFilePath));
        }
        catch { return null; }
    }

    private static void SaveState(string guid, string path)
    {
        try { File.WriteAllText(StateFilePath, JsonSerializer.Serialize(new StudioStateFile(guid, path))); }
        catch { }
    }

    private void CleanupOldVersionDirectories(string keepGuid)
    {
        _versionCleanup.CleanupOldVersionDirectories(StudioVersionsDir, keepGuid);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private void SetStatus(RobloxStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    private void ReportProgress(string message, double percent, bool indeterminate = false, string? detail = null)
    {
        BootstrapperProgress?.Invoke(this, new BootstrapperProgress(message, percent, indeterminate, detail));
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1_048_576) return $"{bytesPerSec / 1_048_576:F1} MB/s";
        if (bytesPerSec >= 1024)      return $"{bytesPerSec / 1024:F1} KB/s";
        return $"{bytesPerSec:F0} B/s";
    }
}
