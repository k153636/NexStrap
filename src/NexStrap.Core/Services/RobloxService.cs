using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace NexStrap.Core.Services;

public enum RobloxStatus { NotInstalled, Idle, Launching, Running, Updating }

public record BootstrapperProgress(string Message, double Percent, bool IsIndeterminate = false, string? Detail = null);

public class RobloxService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly HttpClient ManifestHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    static RobloxService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
        ManifestHttp.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
    }
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "debug.log");

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFilePath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    // CDN mirrors with priority delays — tested in parallel, fastest wins (Bloxstrap pattern)
    private static readonly (string BaseUrl, int DelayMs)[] CdnMirrors =
    [
        ("https://setup.rbxcdn.com",                      0),
        ("https://setup-ak.rbxcdn.com",                 1000),
        ("https://setup-ec2.rbxcdn.com",                2000),
    ];

    // Stock Roblox installation paths
    private static readonly string StockRobloxVersionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "Versions");

    private const int MaxDownloadRetries = 5;    // matches Bloxstrap
    private const int BufferSize         = 4096; // matches Bloxstrap

    private static readonly string VersionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "Versions");

    private static readonly string DownloadsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "Downloads");

    private static readonly string RobloxDownloadsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "Downloads");

    // Package name → subdirectory within the version folder (matches Bloxstrap)
    private static readonly IReadOnlyDictionary<string, string> PackageDirs =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Libraries.zip"]                     = "",
            ["RobloxApp.zip"]                     = "",
            ["redist.zip"]                        = "",
            ["shaders.zip"]                       = "shaders/",
            ["ssl.zip"]                           = "ssl/",
            ["WebView2.zip"]                      = "",
            ["WebView2RuntimeInstaller.zip"]      = "WebView2/",
            ["content-avatar.zip"]                = "content/avatar/",
            ["content-configs.zip"]               = "content/configs/",
            ["content-fonts.zip"]                 = "content/fonts/",
            ["content-sky.zip"]                   = "content/sky/",
            ["content-sounds.zip"]                = "content/sounds/",
            ["content-textures.zip"]              = "content/textures/",
            ["content-textures2.zip"]             = "content/textures/",
            ["content-textures3.zip"]             = "content/textures/",
            ["content-models.zip"]                = "content/models/",
            ["content-terrain.zip"]               = "content/terrain/",
            ["content-platform-fonts.zip"]        = "content/fonts/",
            ["content-platform-dictionaries.zip"] = "PlatformContent/pc/",
            ["extracontent-luapackages.zip"]      = "ExtraContent/LuaPackages/",
            ["extracontent-models.zip"]           = "ExtraContent/models/",
            ["extracontent-places.zip"]           = "ExtraContent/places/",
            ["extracontent-textures.zip"]         = "ExtraContent/textures/",
            ["extracontent-translations.zip"]     = "ExtraContent/translations/",
            ["NPRobloxProxy.zip"]                 = "",
        };

    private string  _cdnBaseUrl           = "https://setup.rbxcdn.com";
    private string? _currentVersionFolder;
    private CancellationTokenSource? _installCts;
    private Process? _launchedRobloxProcess;

    // Shared across DownloadAndInstallAsync and the progress timer task
    private long   _totalDownloadedBytes;
    private long   _totalPackedBytes;
    private string _currentPackageName    = string.Empty;
    private long   _totalExtractFiles;
    private long   _completedExtractFiles;

    public RobloxStatus Status { get; private set; } = RobloxStatus.Idle;
    public event EventHandler<RobloxStatus>?          StatusChanged;
    public event EventHandler<BootstrapperProgress>?  BootstrapperProgress;

    public string? RobloxPlayerPath
    {
        get
        {
            // Only check NexStrap installation
            return FindNexStrapRobloxPlayerPath();
        }
    }

    public string? RobloxVersionPath => FindVersionFolder();

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

    public bool IsInstalled() => RobloxPlayerPath != null;

    public bool IsNexStrapRobloxRunning()
    {
        if (_launchedRobloxProcess == null) return false;
        try
        {
            return !_launchedRobloxProcess.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsVersionComplete(string dir) =>
        File.Exists(Path.Combine(dir, "RobloxPlayerBeta.exe")) &&
        File.Exists(Path.Combine(dir, "WebView2Loader.dll"));

    private string? FindVersionFolder()
    {
        if (_currentVersionFolder != null &&
            Directory.Exists(_currentVersionFolder) &&
            IsVersionComplete(_currentVersionFolder))
            return _currentVersionFolder;

        if (!Directory.Exists(VersionsDir)) return null;

        _currentVersionFolder = Directory.GetDirectories(VersionsDir)
            .Where(IsVersionComplete)
            .OrderByDescending(d => new DirectoryInfo(d).LastWriteTime)
            .FirstOrDefault();

        return _currentVersionFolder;
    }

    private string? FindNexStrapRobloxPlayerPath()
    {
        var versionFolder = FindVersionFolder();
        if (versionFolder == null) return null;

        var playerExe = Path.Combine(versionFolder, "RobloxPlayerBeta.exe");
        return File.Exists(playerExe) ? playerExe : null;
    }

    public async Task<bool> LaunchAsync(string? launchArgs = null, bool autoUpdate = true)
    {
        await CheckAndInstallVcRedistAsync();

        var playerPath = RobloxPlayerPath;

        // Auto-update check
        if (playerPath != null && autoUpdate)
        {
            var latestGuid    = await GetLatestVersionGuidAsync();
            var installedGuid = Path.GetFileName(FindVersionFolder() ?? "");
            if (!string.IsNullOrEmpty(latestGuid) && installedGuid != latestGuid)
            {
                Log($"Update available: {installedGuid} → {latestGuid}");
                SetStatus(RobloxStatus.Updating);
                playerPath = await InstallVersionAsync(latestGuid);
            }
        }

        // First-time install
        if (playerPath == null)
        {
            SetStatus(RobloxStatus.Updating);
            var guid = await GetLatestVersionGuidAsync();
            if (!string.IsNullOrWhiteSpace(guid))
                playerPath = await InstallVersionAsync(guid);
        }

        if (playerPath == null) { SetStatus(RobloxStatus.Idle); return false; }

        Log($"Launching: {playerPath} args={launchArgs ?? "(none)"}");
        SetStatus(RobloxStatus.Launching);
        var proc = TryStartProcess(playerPath, launchArgs);
        if (proc == null) { SetStatus(RobloxStatus.Idle); return false; }

        await Task.Delay(3000);
        if (!proc.HasExited)
            return SetLaunched(proc);

        // Exited immediately — installation is broken, force reinstall and retry once
        Log($"Process exited with code {proc.ExitCode}, reinstalling...");
        SetStatus(RobloxStatus.Updating);
        var retryGuid = await GetLatestVersionGuidAsync();
        if (!string.IsNullOrWhiteSpace(retryGuid))
            playerPath = await InstallVersionAsync(retryGuid, forceReinstall: true);

        if (playerPath == null) { SetStatus(RobloxStatus.Idle); return false; }
        SetStatus(RobloxStatus.Launching);
        proc = TryStartProcess(playerPath, launchArgs);
        if (proc == null) { SetStatus(RobloxStatus.Idle); return false; }
        return SetLaunched(proc);
    }

    // Returns the RobloxPlayerBeta.exe path on success, null on failure.
    // Install priority: already complete → copy from stock → CDN download → official installer + copy
    private async Task<string?> InstallVersionAsync(string versionGuid, bool forceReinstall = false)
    {
        var versionDir = Path.Combine(VersionsDir, versionGuid);

        if (forceReinstall && Directory.Exists(versionDir))
            try { Directory.Delete(versionDir, recursive: true); } catch { }

        // 1. Already installed
        if (IsVersionComplete(versionDir))
        {
            _currentVersionFolder = versionDir;
            return Path.Combine(versionDir, "RobloxPlayerBeta.exe");
        }

        // 2. Copy from stock Roblox (fast path — no download needed)
        var stockFolder = FindStockRobloxVersionFolder();
        if (stockFolder != null)
        {
            Log($"Copying from stock Roblox: {stockFolder}");
            Directory.CreateDirectory(versionDir);
            await CopyDirectoryAsync(stockFolder, versionDir);
        }

        // 3. CDN download
        if (!IsVersionComplete(versionDir))
        {
            _installCts = new CancellationTokenSource();
            var ok = await DownloadAndInstallAsync(versionGuid, versionDir, _installCts.Token);
            _installCts.Dispose();
            _installCts = null;

            if (!ok)
            {
                // 4. Official installer fallback
                await RunOfficialInstallerAsync();
                var newStock = FindStockRobloxVersionFolder();
                if (newStock != null)
                {
                    Log($"Copying from newly installed stock Roblox: {newStock}");
                    Directory.CreateDirectory(versionDir);
                    await CopyDirectoryAsync(newStock, versionDir);
                }
            }
        }

        if (!IsVersionComplete(versionDir)) return null;
        _currentVersionFolder = versionDir;
        Log($"Installation complete: {versionDir}");
        return Path.Combine(versionDir, "RobloxPlayerBeta.exe");
    }

    private static Process? TryStartProcess(string playerPath, string? launchArgs)
        => Process.Start(new ProcessStartInfo(playerPath)
        {
            UseShellExecute  = true,
            WorkingDirectory = Path.GetDirectoryName(playerPath)!,
            Arguments        = launchArgs ?? string.Empty
        });

    private bool SetLaunched(Process proc)
    {
        _launchedRobloxProcess = proc;
        _ = MonitorProcessAsync(proc);
        SetStatus(RobloxStatus.Running);
        Log("Launch successful");
        return true;
    }

    private async Task CopyDirectoryAsync(string source, string dest)
    {
        await Task.Run(() =>
        {
            var allFiles = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            var done     = 0;
            foreach (var file in allFiles)
            {
                var rel      = Path.GetRelativePath(source, file);
                var destFile = Path.Combine(dest, rel);
                var destDir  = Path.GetDirectoryName(destFile);
                if (destDir != null) Directory.CreateDirectory(destDir);
                File.Copy(file, destFile, overwrite: true);
                done++;
                var pct = allFiles.Length > 0 ? done / (double)allFiles.Length * 100.0 : 0;
                ReportProgress($"Copying {Path.GetFileName(file)}", pct);
            }
        });
        Log("Directory copy complete");
    }

    private async Task RunOfficialInstallerAsync()
    {
        Log("CDN download failed, falling back to official installer");
        var installerPath = FindFileInStockRoblox("RobloxPlayerInstaller.exe");

        if (installerPath == null)
        {
            Log("Downloading official installer...");
            var installerDir = Path.Combine(DownloadsDir, "installer");
            Directory.CreateDirectory(installerDir);
            var installerExe = Path.Combine(installerDir, "RobloxPlayerInstaller.exe");
            try
            {
                using var client = new HttpClient();
                var bytes = await client.GetByteArrayAsync("https://setup.rbxcdn.com/RobloxPlayerInstaller.exe");
                await File.WriteAllBytesAsync(installerExe, bytes);
                installerPath = installerExe;
            }
            catch (Exception ex)
            {
                Log($"Failed to download official installer: {ex.Message}");
                return;
            }
        }

        Log($"Running official installer: {installerPath}");
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
            Log($"Official installer exited with code {proc.ExitCode}");
        }
    }

    private static string? FindStockRobloxVersionFolder()
    {
        if (!Directory.Exists(StockRobloxVersionsDir)) return null;
        return Directory.GetDirectories(StockRobloxVersionsDir).FirstOrDefault(IsVersionComplete);
    }

    private static string? FindFileInStockRoblox(string filename)
    {
        if (!Directory.Exists(StockRobloxVersionsDir)) return null;
        return Directory.GetDirectories(StockRobloxVersionsDir)
            .Select(d => Path.Combine(d, filename))
            .FirstOrDefault(File.Exists);
    }

    public void CancelInstall() => _installCts?.Cancel();

    private async Task MonitorProcessAsync(Process process)
    {
        try { await process.WaitForExitAsync(); } catch { }
        SetStatus(RobloxStatus.Idle);
    }

    // -------------------------------------------------------------------------
    // Installation / update — Bloxstrap-compatible download system
    // -------------------------------------------------------------------------

    private async Task<bool> DownloadAndInstallAsync(string versionGuid, string versionDir, CancellationToken ct)
    {
        // Overall progress ranges:
        //   0- 3%  CDN test
        //   3- 6%  manifest fetch
        //   6-88%  download (per-byte within total packed bytes)
        //  88-99%  extraction
        //  99-100% configure
        const double DlStart  = 6.0;
        const double DlEnd    = 88.0;
        const double ExtStart = 88.0;
        const double ExtEnd   = 99.0;

        try
        {
            // Phase 0: CDN test
            ReportProgress("Connecting to CDN...", 0, indeterminate: false);
            _cdnBaseUrl = await TestConnectivityAsync(ct) ?? "https://setup.rbxcdn.com";

            // Phase 1: manifest
            ReportProgress("Fetching package list...", 3, indeterminate: false);
            Log($"Fetching manifest for version: {versionGuid}");
            var packages = await FetchManifestAsync(versionGuid, ct);
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

            // Phase 2: sequential download (no extraction yet)
            _totalDownloadedBytes = 0;
            _totalPackedBytes     = packages.Sum(p => p.CompressedSize);

            var downloadStart    = DateTime.UtcNow;
            var downloadedPaths  = new List<(string Path, string Name)>();

            // Timer refreshes speed/% at 100 ms cadence
            var progressTimer    = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            var progressReporter = Task.Run(async () =>
            {
                try
                {
                    while (await progressTimer.WaitForNextTickAsync(ct))
                    {
                        var dl      = Interlocked.Read(ref _totalDownloadedBytes);
                        var elapsed = (DateTime.UtcNow - downloadStart).TotalSeconds;
                        var speed   = elapsed > 0.1 ? dl / elapsed : 0;
                        var ratio   = _totalPackedBytes > 0 ? dl / (double)_totalPackedBytes : 0;
                        var overall = DlStart + ratio * (DlEnd - DlStart);
                        var name    = _currentPackageName;
                        var msg     = string.IsNullOrEmpty(name) ? "Downloading..." : $"Downloading {name}";
                        ReportProgress(msg, overall, detail: FormatSpeed(speed));
                    }
                }
                catch (OperationCanceledException) { }
            });

            foreach (var package in packages)
            {
                if (ct.IsCancellationRequested) break;

                _currentPackageName = package.Name;
                var localPath = Path.Combine(DownloadsDir, package.Signature);
                await DownloadPackageAsync(package, localPath, versionGuid, ct);

                if (package.Name != "WebView2RuntimeInstaller.zip")
                    downloadedPaths.Add((localPath, package.Name));
            }
            _currentPackageName = string.Empty;

            progressTimer.Dispose();
            try { await progressReporter; } catch { }

            if (ct.IsCancellationRequested) return false;

            // Phase 3: count all zip entries BEFORE starting any extraction task
            _totalExtractFiles    = 0;
            _completedExtractFiles = 0;
            await Task.Run(() =>
            {
                foreach (var (path, _) in downloadedPaths)
                {
                    if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                    try
                    {
                        using var z = ZipFile.OpenRead(path);
                        _totalExtractFiles += z.Entries.Count(e => !string.IsNullOrEmpty(e.Name));
                    }
                    catch { }
                }
            }, ct);

            // Now start all extraction tasks in parallel
            var extractionTasks = downloadedPaths
                .Select(item => Task.Run(() =>
                    ExtractPackageWithProgress(item.Path, item.Name, versionDir, ExtStart, ExtEnd), ct))
                .ToList();

            await Task.WhenAll(extractionTasks);

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
            _currentVersionFolder = versionDir;
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

    private async Task DownloadPackageAsync(RobloxPackage package, string localPath,
        string versionGuid, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        if (File.Exists(localPath))
        {
            if (ComputeMd5(localPath) == package.Signature)
            {
                Interlocked.Add(ref _totalDownloadedBytes, package.CompressedSize);
                return;
            }
            try { File.Delete(localPath); } catch { }
        }

        var robloxCached = Path.Combine(RobloxDownloadsDir, package.Signature);
        if (File.Exists(robloxCached))
        {
            try
            {
                File.Copy(robloxCached, localPath);
                Interlocked.Add(ref _totalDownloadedBytes, package.CompressedSize);
                return;
            }
            catch { }
        }

        var packageUrl = $"{_cdnBaseUrl}/version-{versionGuid}-{package.Name}";
        var buffer     = new byte[BufferSize];

        for (int attempt = 1; attempt <= MaxDownloadRetries; attempt++)
        {
            if (ct.IsCancellationRequested) return;

            long bytesThisAttempt = 0;

            try
            {
                using var resp = await Http.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();

                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(localPath, FileMode.Create,
                    FileAccess.ReadWrite, FileShare.Delete);

                int bytesRead;
                while ((bytesRead = await src.ReadAsync(buffer, ct)) > 0)
                {
                    if (ct.IsCancellationRequested) return;

                    await dst.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    bytesThisAttempt += bytesRead;
                    Interlocked.Add(ref _totalDownloadedBytes, bytesRead);
                }

                dst.Seek(0, SeekOrigin.Begin);
                var hash = ComputeMd5(dst);
                if (hash != package.Signature)
                    throw new InvalidDataException(
                        $"MD5 mismatch for {package.Name}: expected {package.Signature}, got {hash}");

                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Interlocked.Add(ref _totalDownloadedBytes, -bytesThisAttempt);
                try { File.Delete(localPath); } catch { }

                if (attempt >= MaxDownloadRetries) break;

                if (ex is IOException &&
                    packageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    packageUrl = packageUrl.Replace("https://", "http://");
                }

                await Task.Delay(500 * attempt, ct);
            }
        }
    }

    private void ExtractPackageWithProgress(string archivePath, string packageName,
        string versionDir, double extStart, double extEnd)
    {
        try
        {
            if (!archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ReportProgress($"Installing {packageName}", extStart);
                File.Copy(archivePath, Path.Combine(versionDir, packageName), overwrite: true);
                return;
            }

            var sub  = PackageDirs.TryGetValue(packageName, out var d) ? d : "";
            var dest = string.IsNullOrEmpty(sub)
                ? versionDir
                : Path.Combine(versionDir, sub.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dest);

            using var archive = ZipFile.OpenRead(archivePath);
            var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();

            foreach (var entry in entries)
            {
                var done = Interlocked.Increment(ref _completedExtractFiles);
                var pct  = _totalExtractFiles > 0
                    ? extStart + done / (double)_totalExtractFiles * (extEnd - extStart)
                    : extStart;
                ReportProgress($"Extracting {entry.Name}", pct);

                var destPath = Path.Combine(dest, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                var dir = Path.GetDirectoryName(destPath);
                if (dir != null) Directory.CreateDirectory(dir);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }
        catch { }
    }

    private static async Task<string?> TestConnectivityAsync(CancellationToken ct)
    {
        using var cts   = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var       tasks = new List<Task<string>>();

        foreach (var (baseUrl, delayMs) in CdnMirrors)
        {
            var url   = baseUrl;
            var delay = delayMs;
            tasks.Add(Task.Run(async () =>
            {
                if (delay > 0)
                    await Task.Delay(delay, cts.Token);
                await Http.GetAsync($"{url}/version",
                    HttpCompletionOption.ResponseHeadersRead, cts.Token);
                return url;
            }, cts.Token));
        }

        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks);
            tasks.Remove(completed);
            try
            {
                var winner = await completed;
                await cts.CancelAsync();
                return winner;
            }
            catch { }
        }

        return null;
    }

    private async Task<List<RobloxPackage>?> FetchManifestAsync(string versionGuid, CancellationToken ct)
    {
        var urls = new[] { _cdnBaseUrl }
            .Concat(CdnMirrors.Select(m => m.BaseUrl).Where(u => u != _cdnBaseUrl));

        foreach (var baseUrl in urls)
        {
            try
            {
                var text = await ManifestHttp.GetStringAsync(
                    $"{baseUrl}/version-{versionGuid}-rbxPkgManifest.txt", ct);
                var pkgs = ParseManifest(text);
                if (pkgs.Count > 0) return pkgs;
            }
            catch (Exception ex)
            {
                Log($"Failed to fetch manifest from {baseUrl}: {ex.Message}");
            }
        }
        return null;
    }

    private static List<RobloxPackage> ParseManifest(string text)
    {
        using var reader = new StringReader(text);

        var version = reader.ReadLine();
        if (version != "v0") return [];

        var result = new List<RobloxPackage>();
        while (true)
        {
            var name      = reader.ReadLine();
            var signature = reader.ReadLine();
            var rawPacked = reader.ReadLine();
            var rawSize   = reader.ReadLine();

            if (string.IsNullOrEmpty(name)      || string.IsNullOrEmpty(signature) ||
                string.IsNullOrEmpty(rawPacked)  || string.IsNullOrEmpty(rawSize))
                break;

            if (name == "RobloxPlayerLauncher.exe") break;

            long packedSize = long.TryParse(rawPacked, out var s) ? s : 0;
            result.Add(new RobloxPackage(name, packedSize, signature));
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // Version GUID
    // -------------------------------------------------------------------------

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
                if (doc.RootElement.TryGetProperty("clientVersionUpload", out var v))
                {
                    var version = v.GetString();
                    if (version == null) continue;
                    // Strip "version-" prefix if present (Roblox API sometimes returns it with prefix)
                    if (version.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
                        version = version.Substring(8);
                    Log($"Fetched version GUID: {version} from {url}");
                    return version;
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to fetch version GUID from {url}: {ex.Message}");
            }
        }
        Log("Failed to fetch version GUID from all sources");
        return null;
    }

    // -------------------------------------------------------------------------
    // First-time setup (called on app startup in a clean environment)
    // -------------------------------------------------------------------------

    public bool NeedsSetup() => !IsVcRedistInstalled();

    // Called by UpdateService to feed progress into the shared BootstrapperProgress event
    public void BroadcastProgress(BootstrapperProgress p)
        => BootstrapperProgress?.Invoke(this, p);

    public async Task RunSetupAsync()
    {
        // No SetStatus calls — caller manages window lifecycle
        await CheckAndInstallVcRedistAsync();
    }

    // -------------------------------------------------------------------------
    // VC++ redistributable check & auto-install
    // -------------------------------------------------------------------------

    private static bool IsVcRedistInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64");
            return key?.GetValue("Installed") is int v && v == 1;
        }
        catch { return false; }
    }

    private async Task CheckAndInstallVcRedistAsync()
    {
        if (IsVcRedistInstalled()) return;

        Log("VC++ 2015-2022 x64 not found, downloading...");
        ReportProgress("Downloading vc_redist.x64.exe", 0, indeterminate: false);

        var tempExe = Path.Combine(Path.GetTempPath(), "vc_redist.x64.exe");
        try
        {
            using var resp = await Http.GetAsync(
                "https://aka.ms/vs/17/release/vc_redist.x64.exe",
                HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            var total     = resp.Content.Headers.ContentLength ?? 0;
            var startTime = DateTime.UtcNow;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(tempExe);

            var buf  = new byte[BufferSize];
            long got = 0;
            int  n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                got += n;
                var pct     = total > 0 ? got / (double)total * 100.0 : 0;
                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                var speed   = elapsed > 0.1 ? got / elapsed : 0;
                ReportProgress($"Downloading vc_redist.x64.exe ({got / 1024:N0} KB)", pct,
                    detail: FormatSpeed(speed));
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to download VC++ redist: {ex.Message}");
            return;
        }

        ReportProgress("Installing vc_redist.x64.exe", 100, indeterminate: true);
        try
        {
            var proc = Process.Start(new ProcessStartInfo(tempExe)
            {
                Arguments       = "/install /quiet /norestart",
                UseShellExecute = true
            });
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                Log($"VC++ redist install exited with code {proc.ExitCode}");
            }
        }
        catch (Exception ex) { Log($"Failed to install VC++ redist: {ex.Message}"); }
        finally { try { File.Delete(tempExe); } catch { } }
    }

    // -------------------------------------------------------------------------
    // Uninstall
    // -------------------------------------------------------------------------

    public async Task UninstallNexStrapRobloxAsync()
    {
        // Kill any running NexStrap-managed Roblox process
        foreach (var proc in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            try { proc.Kill(); await proc.WaitForExitAsync(); } catch { }
        }

        await Task.Run(() =>
        {
            if (Directory.Exists(VersionsDir))
                try { Directory.Delete(VersionsDir, recursive: true); } catch { }

            if (Directory.Exists(DownloadsDir))
                try { Directory.Delete(DownloadsDir, recursive: true); } catch { }
        });

        SetStatus(RobloxStatus.NotInstalled);
    }

    public async Task UninstallStockRobloxAsync()
    {
        // Kill any running stock Roblox processes
        foreach (var name in new[] { "RobloxPlayerBeta", "RobloxPlayerLauncher", "RobloxStudioBeta" })
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try { proc.Kill(); await proc.WaitForExitAsync(); } catch { }
            }
        }

        await Task.Run(() =>
        {
            var robloxDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");
            if (Directory.Exists(robloxDir))
                try { Directory.Delete(robloxDir, recursive: true); } catch { }

            // Remove registry URL handlers and uninstall entries
            var keysToDelete = new[]
            {
                @"Software\Classes\roblox",
                @"Software\Classes\roblox-player",
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-player",
                @"Software\Roblox",
            };
            foreach (var key in keysToDelete)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); } catch { }
            }
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ComputeMd5(string filePath)
    {
        using var md5    = MD5.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string ComputeMd5(Stream stream)
    {
        using var md5 = MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }

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

// Signature = MD5 hash, used as cache filename (matches Bloxstrap's Package.Signature)
internal record RobloxPackage(string Name, long CompressedSize, string Signature);
