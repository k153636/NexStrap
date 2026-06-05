using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace NexStrap.Core.Services;

public class StudioService
{
    private static readonly HttpClient Http         = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly HttpClient ManifestHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    static StudioService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
        ManifestHttp.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
    }

    private static readonly (string BaseUrl, int DelayMs)[] CdnMirrors =
    [
        ("https://setup.rbxcdn.com",                     0),
        ("https://setup-aws.rbxcdn.com",              2000),
        ("https://setup-ak.rbxcdn.com",               2000),
        ("https://roblox-setup.cachefly.net",         2000),
        ("https://s3.amazonaws.com/setup.roblox.com", 4000),
    ];

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

    private const int  MaxDownloadRetries = 5;
    private const int  BufferSize         = 65536;
    private const int  MaxSegments        = 4;
    private const long MinSegmentBytes    = 2 * 1024 * 1024;

    private string  _cdnBaseUrl = "https://setup.rbxcdn.com";
    private string? _cachedLatestGuid;
    private DateTime _lastVersionCheck = DateTime.MinValue;
    private static readonly TimeSpan VersionCheckInterval = TimeSpan.FromHours(4);

    private long   _totalDownloadedBytes;
    private long   _totalPackedBytes;
    private string _currentPackageName    = string.Empty;
    private long   _totalExtractFiles;
    private long   _completedExtractFiles;

    private readonly SemaphoreSlim _installLock = new(1, 1);
    private CancellationTokenSource? _installCts;

    public RobloxStatus Status { get; private set; } = RobloxStatus.Idle;
    public event EventHandler<RobloxStatus>?         StatusChanged;
    public event EventHandler<BootstrapperProgress>? BootstrapperProgress;

    private sealed record StudioStateFile(string VersionGuid, string VersionPath);

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
            _cdnBaseUrl = await TestConnectivityAsync(ct) ?? "https://setup.rbxcdn.com";

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

            _totalDownloadedBytes = 0;
            _totalPackedBytes     = packages.Sum(p => p.CompressedSize);

            var downloadStart   = DateTime.UtcNow;
            var downloadedPaths = new List<(string Path, string Name)>();

            var progressTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            var progressTask  = Task.Run(async () =>
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
                        ReportProgress(string.IsNullOrEmpty(name) ? "Downloading..." : $"Downloading {name}",
                            overall, detail: FormatSpeed(speed));
                    }
                }
                catch (OperationCanceledException) { }
            });

            foreach (var pkg in packages)
            {
                if (ct.IsCancellationRequested) break;
                _currentPackageName = pkg.Name;
                var localPath = Path.Combine(DownloadsDir, pkg.Signature);
                await DownloadPackageAsync(pkg, localPath, versionGuid, ct);
                downloadedPaths.Add((localPath, pkg.Name));
            }
            _currentPackageName = string.Empty;

            progressTimer.Dispose();
            try { await progressTask; } catch { }

            if (ct.IsCancellationRequested) return false;

            _totalExtractFiles     = 0;
            _completedExtractFiles = 0;
            await Task.Run(() =>
            {
                foreach (var (path, name) in downloadedPaths)
                {
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                        continue;
                    try
                    {
                        using var z = ZipFile.OpenRead(path);
                        _totalExtractFiles += z.Entries.Count(e => !string.IsNullOrEmpty(e.Name));
                    }
                    catch { }
                }
            }, ct);

            await Task.WhenAll(downloadedPaths.Select(item =>
                Task.Run(() => ExtractPackage(item.Path, item.Name, versionDir, ExtStart, ExtEnd), ct)));

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

    // -------------------------------------------------------------------------
    // Package download
    // -------------------------------------------------------------------------
    private async Task DownloadPackageAsync(RobloxPackage package, string localPath,
        string versionGuid, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        if (File.Exists(localPath) && ComputeMd5(localPath) == package.Signature)
        {
            Interlocked.Add(ref _totalDownloadedBytes, package.CompressedSize);
            return;
        }
        try { if (File.Exists(localPath)) File.Delete(localPath); } catch { }

        var url = $"{_cdnBaseUrl}/version-{versionGuid}-{package.Name}";

        for (int attempt = 1; attempt <= MaxDownloadRetries; attempt++)
        {
            if (ct.IsCancellationRequested) return;
            long bytesThisAttempt = 0;

            try
            {
                if (package.CompressedSize >= MinSegmentBytes)
                {
                    if (await TryDownloadMultipartAsync(url, localPath, package, ct))
                        return;
                }

                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();

                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(localPath, FileMode.Create,
                    FileAccess.ReadWrite, FileShare.Delete);

                var buffer = new byte[BufferSize];
                int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    bytesThisAttempt += n;
                    Interlocked.Add(ref _totalDownloadedBytes, n);
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

                RobloxService.Log($"Studio DL attempt {attempt}/{MaxDownloadRetries} failed for {package.Name}: {ex.Message}");
                if (attempt >= MaxDownloadRetries) break;

                if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    url = "http://" + url[8..];

                await Task.Delay(500 * attempt, ct);
            }
        }
    }

    private async Task<bool> TryDownloadMultipartAsync(string url, string localPath,
        RobloxPackage package, CancellationToken ct)
    {
        var bytesAtStart = Interlocked.Read(ref _totalDownloadedBytes);
        try
        {
            long contentLength = package.CompressedSize;
            int  segs          = (int)Math.Min(MaxSegments, Math.Max(1, contentLength / MinSegmentBytes));
            long segLen        = contentLength / segs;

            var tempFiles = Enumerable.Range(0, segs).Select(_ => Path.GetTempFileName()).ToArray();
            try
            {
                await Task.WhenAll(Enumerable.Range(0, segs).Select(async i =>
                {
                    long from = i * segLen;
                    long to   = i == segs - 1 ? contentLength - 1 : from + segLen - 1;

                    using var req  = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);
                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();

                    await using var src = await resp.Content.ReadAsStreamAsync(ct);
                    await using var dst = new FileStream(tempFiles[i], FileMode.Create, FileAccess.Write, FileShare.Delete);
                    var buf = new byte[BufferSize];
                    int n;
                    while ((n = await src.ReadAsync(buf, ct)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n), ct);
                        Interlocked.Add(ref _totalDownloadedBytes, n);
                    }
                }));

                await using var dst = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.Delete);
                foreach (var tmp in tempFiles)
                {
                    await using var src = File.OpenRead(tmp);
                    await src.CopyToAsync(dst, ct);
                }

                dst.Seek(0, SeekOrigin.Begin);
                var hash = ComputeMd5(dst);
                if (hash != package.Signature)
                {
                    Interlocked.Add(ref _totalDownloadedBytes,
                        bytesAtStart - Interlocked.Read(ref _totalDownloadedBytes));
                    return false;
                }
                return true;
            }
            finally
            {
                foreach (var tmp in tempFiles)
                    try { File.Delete(tmp); } catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            Interlocked.Exchange(ref _totalDownloadedBytes, bytesAtStart);
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Extraction
    // -------------------------------------------------------------------------
    private static readonly IReadOnlyDictionary<string, string> StudioPackageDirs =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Libraries.zip"]                     = "",
            ["redist.zip"]                        = "",
            ["RobloxStudio.zip"]                  = "",
            ["WebView2.zip"]                      = "",
            ["WebView2RuntimeInstaller.zip"]      = "WebView2/",
            ["shaders.zip"]                       = "shaders/",
            ["ssl.zip"]                           = "ssl/",
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
            ["content-platform-fonts.zip"]        = "PlatformContent/pc/fonts/",
            ["content-platform-dictionaries.zip"] = "PlatformContent/pc/",
            ["extracontent-luapackages.zip"]      = "ExtraContent/LuaPackages/",
            ["extracontent-models.zip"]           = "ExtraContent/models/",
            ["extracontent-places.zip"]           = "ExtraContent/places/",
            ["extracontent-textures.zip"]         = "ExtraContent/textures/",
            ["extracontent-translations.zip"]     = "ExtraContent/translations/",
            ["BuiltInPlugins.zip"]                = "BuiltInPlugins/",
            ["BuiltInStandalonePlugins.zip"]      = "BuiltInStandalonePlugins/",
            ["ApplicationConfig.zip"]             = "ApplicationConfig/",
            ["NPRobloxProxy.zip"]                 = "",
            ["Plugins.zip"]                       = "Plugins/",
            ["StudioFonts.zip"]                   = "StudioFonts/",
            ["LibrariesQt5.zip"]                  = "",
            ["content-qt_translations.zip"]       = "content/qt_translations/",
            ["content-studio_svg_textures.zip"]   = "content/studio_svg_textures/",
            ["content-api-docs.zip"]              = "content/api_docs/",
            ["extracontent-scripts.zip"]          = "ExtraContent/scripts/",
            ["studiocontent-models.zip"]          = "StudioContent/models/",
            ["studiocontent-textures.zip"]        = "StudioContent/textures/",
        };

    private void ExtractPackage(string localPath, string packageName, string destDir,
        double extStart, double extEnd)
    {
        if (!File.Exists(localPath)) return;
        if (!packageName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return;

        StudioPackageDirs.TryGetValue(packageName, out var subDir);
        subDir ??= "";
        var dest = string.IsNullOrEmpty(subDir)
            ? destDir
            : Path.Combine(destDir, subDir.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            using var archive = ZipFile.OpenRead(localPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                Interlocked.Increment(ref _completedExtractFiles);
                var total   = Interlocked.Read(ref _totalExtractFiles);
                var pct     = total > 0 ? extStart + Interlocked.Read(ref _completedExtractFiles) / (double)total * (extEnd - extStart) : extStart;
                ReportProgress($"Extracting {entry.Name}", pct);

                var destPath = Path.Combine(dest, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                var dir      = Path.GetDirectoryName(destPath);
                if (dir != null) Directory.CreateDirectory(dir);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }
        catch { }
    }

    // -------------------------------------------------------------------------
    // CDN
    // -------------------------------------------------------------------------
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
                if (delay > 0) await Task.Delay(delay, cts.Token);
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
                if (pkgs.Count > 0)
                {
                    _cdnBaseUrl = baseUrl;
                    return pkgs;
                }
            }
            catch (Exception ex) { RobloxService.Log($"Studio manifest fetch failed ({baseUrl}): {ex.Message}"); }
        }
        return null;
    }

    private static List<RobloxPackage> ParseManifest(string text)
    {
        using var reader = new StringReader(text);
        if (reader.ReadLine() != "v0") return [];

        var result = new List<RobloxPackage>();
        while (true)
        {
            var name      = reader.ReadLine();
            var signature = reader.ReadLine();
            var rawPacked = reader.ReadLine();
            var rawSize   = reader.ReadLine();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(signature) ||
                string.IsNullOrEmpty(rawPacked) || string.IsNullOrEmpty(rawSize))
                break;

            long packed = long.TryParse(rawPacked, out var s) ? s : 0;
            result.Add(new RobloxPackage(name, packed, signature));
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // Version GUID
    // -------------------------------------------------------------------------
    private async Task<string?> GetLatestVersionGuidCachedAsync()
    {
        if (_cachedLatestGuid != null && DateTime.UtcNow - _lastVersionCheck < VersionCheckInterval)
            return _cachedLatestGuid;

        var guid = await GetLatestStudioVersionGuidAsync();
        if (guid != null)
        {
            _cachedLatestGuid = guid;
            _lastVersionCheck = DateTime.UtcNow;
        }
        return guid;
    }

    private static async Task<string?> GetLatestStudioVersionGuidAsync()
    {
        foreach (var url in new[]
        {
            "https://clientsettingscdn.roblox.com/v2/client-version/WindowsStudio64",
            "https://clientsettings.roblox.com/v2/client-version/WindowsStudio64",
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
                    if (version.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
                        version = version[8..];
                    RobloxService.Log($"Studio version GUID: {version}");
                    return version;
                }
            }
            catch (Exception ex) { RobloxService.Log($"Studio version fetch failed from {url}: {ex.Message}"); }
        }
        return null;
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
        if (!Directory.Exists(StudioVersionsDir)) return;
        foreach (var dir in Directory.GetDirectories(StudioVersionsDir))
        {
            if (string.Equals(Path.GetFileName(dir), keepGuid, StringComparison.OrdinalIgnoreCase))
                continue;
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
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

    private static string ComputeMd5(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        return ComputeMd5(fs);
    }

    private static string ComputeMd5(Stream stream)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1_048_576) return $"{bytesPerSec / 1_048_576:F1} MB/s";
        if (bytesPerSec >= 1024)      return $"{bytesPerSec / 1024:F1} KB/s";
        return $"{bytesPerSec:F0} B/s";
    }
}
