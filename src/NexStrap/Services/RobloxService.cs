using System.Diagnostics;

namespace NexStrap.Services;

public enum RobloxStatus { NotInstalled, Idle, Launching, Running, Updating }

public record BootstrapperProgress(string Message, double Percent, bool IsIndeterminate = false, string? Detail = null);

/// <summary>Options passed to LaunchAsync that control post-launch behavior.</summary>
public record LaunchOptions(
    bool    MultiInstance        = false,
    bool    SuppressCrashHandler = false,
    int     CpuCoreLimit         = 0,
    bool    MemoryOptimization   = false,
    bool    CleanupOldVersions   = true,
    string? CookieToInject       = null,
    bool    StretchResolution    = false,
    int     StretchWidth         = 1280,
    int     StretchHeight        = 960
);

public class RobloxService
{
    private readonly RobloxVersionManifestService _versionManifest;
    private readonly RobloxPackageManifestService _packageManifest;
    private readonly RobloxPackageInstallerService _packageInstaller;
    private readonly RobloxDisplayStretchService _displayStretch;
    private readonly RobloxSetupService _setup;
    private readonly RobloxMultiInstanceMutexService _multiInstanceMutex;
    private readonly RobloxInstallStateService _installState;
    private readonly RobloxStockInstallFallbackService _stockFallback;
    private readonly RobloxCookieSessionService _cookieSession;

    // -------------------------------------------------------------------------
    // Logging
    // -------------------------------------------------------------------------
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "debug.log");

    public static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFilePath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    // -------------------------------------------------------------------------
    // Paths
    // -------------------------------------------------------------------------
    private static readonly string VersionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "Versions");

    private static readonly string DownloadsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "Downloads");

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------
    private string _cdnBaseUrl = RobloxPackageManifestService.DefaultCdnBaseUrl;

    // インスト�Eル多重実行防止 (Bloxstrap の mutex に相彁E
    private readonly SemaphoreSlim _installLock = new(1, 1);

    private CancellationTokenSource? _installCts;
    private Process? _launchedRobloxProcess;

    // マルチインスタンス: プロセスID ↁEスロチE��インチE��クス
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _pidToSlot = new();
    private int _launchSlotCounter = 0;

    /// <summary>初回インスト�Eル後�E起動前に呼ばれる  EFastFlags/Mods の書き込みに使ぁE��E/summary>
    public Func<Task>? PreLaunchAsync { get; set; }

    public RobloxStatus Status { get; private set; } = RobloxStatus.Idle;
    public event EventHandler<RobloxStatus>?         StatusChanged;
    public event EventHandler<BootstrapperProgress>? BootstrapperProgress;

    public RobloxService()
        : this(CreateDefaultServices())
    {
    }

    private RobloxService((
        RobloxVersionManifestService VersionManifest,
        RobloxPackageManifestService PackageManifest,
        RobloxPackageInstallerService PackageInstaller,
        RobloxDisplayStretchService DisplayStretch,
        RobloxSetupService Setup,
        RobloxMultiInstanceMutexService MultiInstanceMutex,
        RobloxInstallStateService InstallState,
        RobloxStockInstallFallbackService StockFallback,
        RobloxCookieSessionService CookieSession) services)
        : this(
            services.VersionManifest,
            services.PackageManifest,
            services.PackageInstaller,
            services.DisplayStretch,
            services.Setup,
            services.MultiInstanceMutex,
            services.InstallState,
            services.StockFallback,
            services.CookieSession)
    {
    }

    private static (
        RobloxVersionManifestService VersionManifest,
        RobloxPackageManifestService PackageManifest,
        RobloxPackageInstallerService PackageInstaller,
        RobloxDisplayStretchService DisplayStretch,
        RobloxSetupService Setup,
        RobloxMultiInstanceMutexService MultiInstanceMutex,
        RobloxInstallStateService InstallState,
        RobloxStockInstallFallbackService StockFallback,
        RobloxCookieSessionService CookieSession) CreateDefaultServices()
    {
        var installState = new RobloxInstallStateService();
        return (
            new RobloxVersionManifestService(),
            new RobloxPackageManifestService(),
            new RobloxPackageInstallerService(),
            new RobloxDisplayStretchService(),
            new RobloxSetupService(),
            new RobloxMultiInstanceMutexService(),
            installState,
            new RobloxStockInstallFallbackService(installState),
            new RobloxCookieSessionService());
    }

    public RobloxService(
        RobloxVersionManifestService versionManifest,
        RobloxPackageManifestService packageManifest,
        RobloxPackageInstallerService packageInstaller,
        RobloxDisplayStretchService displayStretch,
        RobloxSetupService setup,
        RobloxMultiInstanceMutexService multiInstanceMutex,
        RobloxInstallStateService installState,
        RobloxStockInstallFallbackService stockFallback,
        RobloxCookieSessionService cookieSession)
    {
        _versionManifest    = versionManifest;
        _packageManifest    = packageManifest;
        _packageInstaller   = packageInstaller;
        _displayStretch     = displayStretch;
        _setup              = setup;
        _multiInstanceMutex = multiInstanceMutex;
        _installState       = installState;
        _stockFallback      = stockFallback;
        _cookieSession      = cookieSession;
    }

    // -------------------------------------------------------------------------
    // Public surface
    // -------------------------------------------------------------------------
    public string? RobloxPlayerPath    => FindNexStrapRobloxPlayerPath();
    public string? RobloxVersionPath   => FindVersionFolder();

    public string ClientSettingsPath
    {
        get
        {
            var vp = RobloxVersionPath;
            return vp == null ? string.Empty : Path.Combine(vp, "ClientSettings");
        }
    }

    public string ContentPath
    {
        get
        {
            var vp = RobloxVersionPath;
            return vp == null ? string.Empty : Path.Combine(vp, "content");
        }
    }

    public bool IsInstalled()             => RobloxPlayerPath != null;
    public bool IsNexStrapRobloxRunning() =>
        _launchedRobloxProcess != null &&
        !_launchedRobloxProcess.HasExited;

    // -------------------------------------------------------------------------
    // Version folder detection
    // -------------------------------------------------------------------------
    private static bool IsVersionComplete(string dir) =>
        RobloxInstallStateService.IsVersionComplete(dir);

    private RobloxInstallStateFile? LoadState()
        => _installState.LoadState();

    private void SaveState(string guid, string path)
        => _installState.SaveState(guid, path);

    private string? FindVersionFolder()
        => _installState.FindVersionFolder();

    private string? FindNexStrapRobloxPlayerPath()
        => _installState.FindPlayerPath();

    // -------------------------------------------------------------------------
    // Launch
    // -------------------------------------------------------------------------
    public async Task<bool> LaunchAsync(string? launchArgs = null, bool autoUpdate = true,
        LaunchOptions? options = null)
    {
        options ??= new LaunchOptions();

        await CheckAndInstallVcRedistAsync();

        // マルチインスタンス: NexStrap ぁEROBLOX_singletonMutex を保持することで
        // 新しい Roblox インスタンスがシングルトンチェチE��をパスできる
        if (options.MultiInstance)
            AcquireRobloxSingletonMutex();

        var playerPath = RobloxPlayerPath;

        // Auto-update
        if (playerPath != null && autoUpdate)
        {
            var latestGuid    = await GetLatestVersionGuidCachedAsync();
            var state         = LoadState();
            var folderName    = Path.GetFileName(FindVersionFolder() ?? "");
            var installedGuid = state?.VersionGuid
                                ?? (folderName.StartsWith("version-", StringComparison.OrdinalIgnoreCase)
                                    ? folderName[8..] : folderName);

            if (!string.IsNullOrEmpty(latestGuid) && installedGuid != latestGuid)
            {
                Log($"Update available: {installedGuid} ↁE{latestGuid}");
                SetStatus(RobloxStatus.Updating);
                var updatedPath = await InstallVersionAsync(latestGuid);
                if (updatedPath != null)
                {
                    playerPath = updatedPath;
                    UpdateVersionCache(latestGuid);
                    SaveState(latestGuid, Path.GetDirectoryName(playerPath)!);
                    if (options.CleanupOldVersions)
                        CleanupOldVersionDirectories(latestGuid);
                }
                else
                {
                    Log($"Update failed, launching existing version: {playerPath}");
                }
            }
        }

        // 初回インスト�Eル
        if (playerPath == null)
        {
            SetStatus(RobloxStatus.Updating);
            var guid = await GetLatestVersionGuidCachedAsync();
            if (!string.IsNullOrWhiteSpace(guid))
            {
                playerPath = await InstallVersionAsync(guid);
                if (playerPath != null)
                {
                    UpdateVersionCache(guid);
                    SaveState(guid, Path.GetDirectoryName(playerPath)!);
                    if (PreLaunchAsync != null)
                        await PreLaunchAsync();
                }
            }
        }

        if (playerPath == null) { SetStatus(RobloxStatus.Idle); return false; }

        // 起動直前にクチE��ーを注入�E�タイミングを最小化�E�E
        if (options.CookieToInject != null)
        {
            var ok = _cookieSession.InjectAccountCookie(options.CookieToInject);
            Log(ok ? "Cookie injected successfully before launch" : "Cookie injection failed (file may be locked)");
        }

        // Stretch Resolution  ERoblox 起動前に解像度を変更
        if (options.StretchResolution)
            ApplyStretchResolution(options.StretchWidth, options.StretchHeight);

        Log($"Launching: {playerPath} args={launchArgs ?? "(none)"}");
        SetStatus(RobloxStatus.Launching);
        var proc = TryStartProcess(playerPath, launchArgs);
        if (proc == null) { SetStatus(RobloxStatus.Idle); return false; }

        await Task.Delay(3000);
        if (!proc.HasExited)
            return SetLaunched(proc, options);

        // 即終亁E E壊れてぁE��ので強制再インスト�Eルして一度だけリトライ
        Log($"Process exited immediately (code {proc.ExitCode}), force reinstalling...");
        SetStatus(RobloxStatus.Updating);
        var retryGuid = await GetLatestVersionGuidCachedAsync();
        if (!string.IsNullOrWhiteSpace(retryGuid))
        {
            playerPath = await InstallVersionAsync(retryGuid, forceReinstall: true);
            if (playerPath != null)
                SaveState(retryGuid, Path.GetDirectoryName(playerPath)!);
        }

        if (playerPath == null) { SetStatus(RobloxStatus.Idle); return false; }
        if (options.CookieToInject != null)
        {
            var ok = _cookieSession.InjectAccountCookie(options.CookieToInject);
            Log(ok ? "Cookie injected (retry path)" : "Cookie injection failed (retry path)");
        }
        SetStatus(RobloxStatus.Launching);
        proc = TryStartProcess(playerPath, launchArgs);
        if (proc == null) { SetStatus(RobloxStatus.Idle); return false; }
        return SetLaunched(proc, options);
    }

    private bool SetLaunched(Process proc, LaunchOptions opts)
    {
        _launchedRobloxProcess = proc;
        var slot = _launchSlotCounter++;
        _pidToSlot[proc.Id] = slot;
        _ = MonitorProcessAsync(proc);
        _ = PostLaunchAsync(proc, opts);
        SetStatus(RobloxStatus.Running);
        Log($"Launch successful (slot={slot}, pid={proc.Id})");
        return true;
    }

    public bool TryGetSlotForPid(int pid, out int slot) => _pidToSlot.TryGetValue(pid, out slot);
    public IEnumerable<int> GetTrackedRobloxPids()      => _pidToSlot.Keys;

    /// <summary>CPU アフィニティ・メモリ上限・クラチE��ュハンドラ抑制を起動後に適用する、E/summary>
    public async Task PostLaunchAsync(Process proc, LaunchOptions opts)
    {
        await Task.Delay(1500); // Roblox の初期化を少し征E��

        // CPU アフィニティ
        if (opts.CpuCoreLimit > 0)
        {
            try
            {
                int cores = Math.Clamp(opts.CpuCoreLimit, 1, Environment.ProcessorCount);
                long mask = cores >= 64 ? -1L : (1L << cores) - 1;
                proc.ProcessorAffinity = (nint)mask;
                Log($"CPU affinity set: {cores}/{Environment.ProcessorCount} cores (mask=0x{mask:X})");
            }
            catch (Exception ex) { Log($"CPU affinity failed: {ex.Message}"); }
        }

        // メモリ上限 (RAM の半�E or 4GB の小さぁE��ぁE
        // 2GB 上限では現代の Roblox が頻繁にペ�Eジアウトしパフォーマンスが低下するためE4GB に変更
        if (opts.MemoryOptimization)
        {
            try
            {
                var info  = GC.GetGCMemoryInfo();
                long maxWs = Math.Min(4L * 1024 * 1024 * 1024,
                                      info.TotalAvailableMemoryBytes / 2);
                proc.MaxWorkingSet = new IntPtr(maxWs);
                Log($"MaxWorkingSet set to {maxWs / 1_048_576}MB");
            }
            catch (Exception ex) { Log($"Memory optimization failed: {ex.Message}"); }
        }

        // RobloxCrashHandler 抑制 (起動後に出現するため最大3回リトライ)
        if (opts.SuppressCrashHandler)
        {
            var hasWindow = await WaitForMainWindowAsync(proc, TimeSpan.FromSeconds(10));
            if (!hasWindow)
            {
                Log("Skipped RobloxCrashHandler suppression because Roblox has no main window");
                return;
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                await Task.Delay(800);
                var handlers = Process.GetProcessesByName("RobloxCrashHandler");
                foreach (var h in handlers)
                {
                    try
                    {
                        if (!h.CloseMainWindow()) h.Kill(entireProcessTree: true);
                        Log($"Suppressed RobloxCrashHandler (PID {h.Id})");
                    }
                    catch { }
                }
                if (handlers.Length > 0) break;
            }
        }
    }

    private static async Task<bool> WaitForMainWindowAsync(Process proc, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (proc.HasExited) return false;
                proc.Refresh();
                if (proc.MainWindowHandle != IntPtr.Zero)
                    return true;
            }
            catch
            {
                return false;
            }

            await Task.Delay(500);
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Account cookie injection  ERobloxCookies.dat に対象アカウントを書き込む
    // -------------------------------------------------------------------------
    public static void ClearRobloxCookies()
    {
        new RobloxCookieSessionService().ClearRobloxCookies();
    }

    /// <summary>
    /// appStorage.json のセチE��ョン関連フィールドをクリアする、E
    /// Roblox が保存済みセチE��ョンを使わず auth ticket を使ぁE��ぁE��するため、E
    /// </summary>
    public static void ClearAppStorageSession()
    {
        new RobloxCookieSessionService().ClearAppStorageSession();
    }

    public static bool InjectAccountCookie(string robloSecurityCookie, string? targetPath = null)
    {
        return new RobloxCookieSessionService().InjectAccountCookie(robloSecurityCookie, targetPath);
    }

    // -------------------------------------------------------------------------
    // Multi-instance
    // -------------------------------------------------------------------------
    private void AcquireRobloxSingletonMutex()
        => _multiInstanceMutex.AcquireRobloxSingletonMutex();

    public void ReleaseRobloxSingletonMutex()
        => _multiInstanceMutex.ReleaseRobloxSingletonMutex();
    // -------------------------------------------------------------------------
    // Install
    // -------------------------------------------------------------------------
    private async Task<string?> InstallVersionAsync(string versionGuid, bool forceReinstall = false)
    {
        // 同時インスト�Eル防止
        await _installLock.WaitAsync();
        try
        {
            return await InstallVersionInternalAsync(versionGuid, forceReinstall);
        }
        finally
        {
            _installLock.Release();
        }
    }

    private async Task<string?> InstallVersionInternalAsync(string versionGuid, bool forceReinstall)
    {
        var versionDir = Path.Combine(VersionsDir, versionGuid);

        if (forceReinstall && Directory.Exists(versionDir))
            try { Directory.Delete(versionDir, recursive: true); } catch { }

        // 1. 既にインスト�Eル済み
        if (IsVersionComplete(versionDir))
        {
            _installState.SetCurrentVersionFolder(versionDir);
            return Path.Combine(versionDir, "RobloxPlayerBeta.exe");
        }

        // 2. ストック Roblox の正確なバ�Eジョンからコピ�E (CDN 不要�E高速パス)
        var stockFolder = FindStockRobloxVersionFolder(versionGuid);
        if (stockFolder != null)
        {
            Log($"Copying from stock Roblox: {stockFolder}");
            Directory.CreateDirectory(versionDir);
            await _stockFallback.CopyDirectoryAsync(stockFolder, versionDir, ReportProgress);
        }

        // 3. CDN ダウンローチE
        if (!IsVersionComplete(versionDir))
        {
            _installCts = new CancellationTokenSource();
            var ok = await DownloadAndInstallAsync(versionGuid, versionDir, _installCts.Token);
            _installCts.Dispose();
            _installCts = null;

            if (!ok)
            {
                // CDN 完�E失敁E E正確なバ�Eジョンのストック Roblox があれ�Eコピ�E
                var stockFallback = FindStockRobloxVersionFolder(versionGuid);
                if (stockFallback != null)
                {
                    Log($"CDN failed, copying from stock Roblox: {stockFallback}");
                    Directory.CreateDirectory(versionDir);
                    await _stockFallback.CopyDirectoryAsync(stockFallback, versionDir, ReportProgress);
                }
                else
                {
                    // 最終手段: 公式インスト�Eラーで正確なバ�Eジョンを取得後コピ�E
                    await _stockFallback.RunOfficialInstallerAsync();
                    var newStock = FindStockRobloxVersionFolder(versionGuid);
                    if (newStock != null)
                    {
                        Log($"Copying from newly installed stock Roblox: {newStock}");
                        Directory.CreateDirectory(versionDir);
                        await _stockFallback.CopyDirectoryAsync(newStock, versionDir, ReportProgress);
                    }
                }
            }
        }

        if (!IsVersionComplete(versionDir)) return null;
        _installState.SetCurrentVersionFolder(versionDir);
        Log($"Installation complete: {versionDir}");
        return Path.Combine(versionDir, "RobloxPlayerBeta.exe");
    }

    // -------------------------------------------------------------------------
    // Old version cleanup
    // -------------------------------------------------------------------------
    private void CleanupOldVersionDirectories(string keepGuid)
    {
        if (!Directory.Exists(VersionsDir)) return;
        foreach (var dir in Directory.GetDirectories(VersionsDir))
        {
            if (string.Equals(Path.GetFileName(dir), keepGuid, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                Log($"Cleaned up old version: {Path.GetFileName(dir)}");
            }
            catch { }
        }
    }

    public bool IsStretchActive => _displayStretch.IsStretchActive;

    public bool ApplyStretchResolution(int width, int height)
        => _displayStretch.ApplyStretchResolution(width, height);

    public void RestoreResolution()
        => _displayStretch.RestoreResolution();
    // -------------------------------------------------------------------------
    // Process management helpers
    // -------------------------------------------------------------------------
    private static Process? TryStartProcess(string playerPath, string? launchArgs, string? isolatedDataDir = null)
    {
        var psi = new ProcessStartInfo(playerPath)
        {
            WorkingDirectory = Path.GetDirectoryName(playerPath)!,
            Arguments        = launchArgs ?? string.Empty
        };

        if (isolatedDataDir != null)
        {
            // UseShellExecute = false で環墁E��数を上書きできる
            psi.UseShellExecute = false;
            foreach (System.Collections.DictionaryEntry kv in System.Environment.GetEnvironmentVariables())
                psi.Environment[(string)kv.Key] = (string?)kv.Value ?? "";
            psi.Environment["LOCALAPPDATA"] = isolatedDataDir;
        }
        else
        {
            psi.UseShellExecute = true;
        }

        return Process.Start(psi);
    }

    private async Task MonitorProcessAsync(Process process)
    {
        try { await process.WaitForExitAsync(); } catch { }
        _pidToSlot.TryRemove(process.Id, out _);
        RestoreResolution(); // Stretch Resolution を使ってぁE��場合に復允E
        SetStatus(RobloxStatus.Idle);
    }

    private string? FindStockRobloxVersionFolder(string? targetGuid = null)
        => _stockFallback.FindStockRobloxVersionFolder(targetGuid);

    public void CancelInstall() => _installCts?.Cancel();

    // -------------------------------------------------------------------------
    // Download & Install (Bloxstrap-compatible)
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
            _cdnBaseUrl = await _packageManifest.TestConnectivityAsync(ct) ?? RobloxPackageManifestService.DefaultCdnBaseUrl;
            Log($"CDN winner: {_cdnBaseUrl}");

            ReportProgress("Fetching package list...", 3);
            Log($"Fetching manifest for: {versionGuid}");
            var manifest = await _packageManifest.FetchManifestAsync(versionGuid, _cdnBaseUrl, ct);
            if (manifest != null)
                _cdnBaseUrl = manifest.CdnBaseUrl;
            var packages = manifest?.Packages;
            if (packages == null || packages.Count == 0)
            {
                Log("Manifest fetch returned no packages");
                ReportProgress("CDN unavailable", 0, indeterminate: true);
                return false;
            }

            if (Directory.Exists(versionDir))
                try { Directory.Delete(versionDir, recursive: true); } catch { }

            Directory.CreateDirectory(versionDir);
            Directory.CreateDirectory(DownloadsDir);

            _packageInstaller.ResetDownloadProgress(packages.Sum(p => p.CompressedSize));

            var downloadStart = DateTime.UtcNow;
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
                if (pkg.Name != "WebView2RuntimeInstaller.zip")
                    downloadedPaths.Add((localPath, pkg.Name));
            }
            _packageInstaller.SetCurrentPackageName(string.Empty);

            progressTimer.Dispose();
            try { await progressTask; } catch { }

            if (ct.IsCancellationRequested) return false;

            // 展開ファイル数を�Eに雁E��E(進捗精度のため)
            await _packageInstaller.CountExtractFilesAsync(downloadedPaths, ct);

            // 全パッケージを並列展開
            await Task.WhenAll(downloadedPaths.Select(item =>
                Task.Run(() => _packageInstaller.ExtractPackageWithProgress(
                    item.Path, item.Name, versionDir, ExtStart, ExtEnd, ReportProgress), ct)));

            ReportProgress("Configuring...", 99);
            await File.WriteAllTextAsync(
                Path.Combine(versionDir, "AppSettings.xml"),
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Settings>
                	<ContentFolder>content</ContentFolder>
                	<BaseUrl>http://www.roblox.com</BaseUrl>
                </Settings>
                """, ct);

            ReportProgress("Done", 100);
            _installState.SetCurrentVersionFolder(versionDir);
            return File.Exists(Path.Combine(versionDir, "RobloxPlayerBeta.exe"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"DownloadAndInstallAsync failed: {ex.Message}");
            ReportProgress("Installation failed", 0, indeterminate: true);
        }
        return false;
    }

    // -------------------------------------------------------------------------
    // Version GUID
    // -------------------------------------------------------------------------
    private async Task<string?> GetLatestVersionGuidCachedAsync()
        => await _versionManifest.GetLatestVersionGuidCachedAsync();

    private void UpdateVersionCache(string guid)
        => _versionManifest.UpdateVersionCache(guid);

    // -------------------------------------------------------------------------
    // Protocol handler registration  Eroblox:// / roblox-player://
    // -------------------------------------------------------------------------
    /// <summary>
    /// 起動�Eた�Eに現在の EXE パスで roblox:// プロトコルを�E登録する、E
    /// Debug / Release / 移動後など、どのパスで起動してめEWeb 経由が機�Eするようにする、E
    /// </summary>
    public static void RegisterProtocolHandler()
        => RobloxProtocolRegistrationService.RegisterProtocolHandler();
    // -------------------------------------------------------------------------
    // Setup
    // -------------------------------------------------------------------------
    public bool NeedsSetup() => !_setup.IsVcRedistInstalled();
    public void BroadcastProgress(BootstrapperProgress p) => BootstrapperProgress?.Invoke(this, p);
    public async Task RunSetupAsync() => await CheckAndInstallVcRedistAsync();

    private async Task CheckAndInstallVcRedistAsync()
        => await _setup.CheckAndInstallVcRedistAsync(ReportProgress);
    // -------------------------------------------------------------------------
    // Uninstall
    // -------------------------------------------------------------------------
    public async Task UninstallNexStrapRobloxAsync()
    {
        await RobloxUninstallService.UninstallNexStrapRobloxAsync();
        SetStatus(RobloxStatus.NotInstalled);
    }

    public async Task UninstallStockRobloxAsync()
    {
        await RobloxUninstallService.UninstallStockRobloxAsync();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1_048_576) return $"{bytesPerSec / 1_048_576:F1} MB/s";
        if (bytesPerSec >= 1024)      return $"{bytesPerSec / 1024:F0} KB/s";
        return $"{(int)bytesPerSec} B/s";
    }

    private void SetStatus(RobloxStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
        if (status == RobloxStatus.Launching)
            ReportProgress("Launching Roblox...", 100, indeterminate: true);
    }

    private void ReportProgress(string message, double percent,
        bool indeterminate = false, string? detail = null)
        => BootstrapperProgress?.Invoke(this,
            new BootstrapperProgress(message, percent, indeterminate, detail));
}
