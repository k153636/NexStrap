using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

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
    private readonly RobloxPackageInstallerService _packageInstaller;

    // -------------------------------------------------------------------------
    // Win32 — multi-instance mutex control
    // -------------------------------------------------------------------------
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateMutex(IntPtr lpAttr, bool bInitialOwner, string lpName);
    [DllImport("kernel32.dll")] private static extern bool ReleaseMutex(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(uint infoClass, IntPtr buffer, uint bufferSize, out uint returnLength);
    [DllImport("ntdll.dll")]
    private static extern uint NtDuplicateObject(IntPtr srcProcess, IntPtr srcHandle, IntPtr dstProcess, out IntPtr dstHandle, uint access, uint attrs, uint options);
    [DllImport("ntdll.dll")]
    private static extern uint NtQueryObject(IntPtr handle, uint objInfoClass, IntPtr buffer, uint bufSize, out uint returnLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemHandleEntry
    {
        public uint   Pid;
        public byte   ObjectType;
        public byte   Flags;
        public ushort Handle;
        public IntPtr Object;
        public uint   Access;
    }

    private const uint SysHandleInfo       = 16;
    private const uint DupCloseSource      = 0x1;
    private const uint DupSameAccess       = 0x2;
    private const uint ProcDupHandle       = 0x0040;
    private const uint StatusInfoLenMismatch = 0xC0000004;

    // -------------------------------------------------------------------------
    // HTTP clients
    // -------------------------------------------------------------------------
    private static readonly HttpClient Http         = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly HttpClient ManifestHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    static RobloxService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
        ManifestHttp.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
    }

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
    // CDN — Bloxstrap と同一の5鏡構成
    // -------------------------------------------------------------------------
    private static readonly (string BaseUrl, int DelayMs)[] CdnMirrors =
    [
        ("https://setup.rbxcdn.com",                     0),
        ("https://setup-aws.rbxcdn.com",              2000),
        ("https://setup-ak.rbxcdn.com",               2000),
        ("https://roblox-setup.cachefly.net",         2000),
        ("https://s3.amazonaws.com/setup.roblox.com", 4000),
    ];

    // -------------------------------------------------------------------------
    // Paths
    // -------------------------------------------------------------------------
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "roblox-state.json");

    private static readonly string StockRobloxVersionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "Versions");

    private static readonly string VersionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "Versions");

    private static readonly string DownloadsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "Downloads");

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------
    private const int BufferSize = 65536;   // 64 KB (より速い)

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------
    private string  _cdnBaseUrl           = "https://setup.rbxcdn.com";
    private string? _currentVersionFolder;

    // インストール多重実行防止 (Bloxstrap の mutex に相当)
    private readonly SemaphoreSlim _installLock = new(1, 1);

    // マルチインスタンス用 Win32 ミューテックスハンドル
    private IntPtr _multiInstanceMutex = IntPtr.Zero;

    private CancellationTokenSource? _installCts;
    private Process? _launchedRobloxProcess;

    // マルチインスタンス: プロセスID → スロットインデックス
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _pidToSlot = new();
    private int _launchSlotCounter = 0;

    /// <summary>初回インストール後・起動前に呼ばれる — FastFlags/Mods の書き込みに使う。</summary>
    public Func<Task>? PreLaunchAsync { get; set; }

    public RobloxStatus Status { get; private set; } = RobloxStatus.Idle;
    public event EventHandler<RobloxStatus>?         StatusChanged;
    public event EventHandler<BootstrapperProgress>? BootstrapperProgress;

    public RobloxService()
        : this(new RobloxVersionManifestService(), new RobloxPackageInstallerService())
    {
    }

    public RobloxService(
        RobloxVersionManifestService versionManifest,
        RobloxPackageInstallerService packageInstaller)
    {
        _versionManifest  = versionManifest;
        _packageInstaller = packageInstaller;
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
        File.Exists(Path.Combine(dir, "RobloxPlayerBeta.exe"));

    private sealed record RobloxStateFile(string VersionGuid, string VersionPath);

    private static RobloxStateFile? LoadState()
    {
        try
        {
            if (!File.Exists(StateFilePath)) return null;
            return JsonSerializer.Deserialize<RobloxStateFile>(File.ReadAllText(StateFilePath));
        }
        catch { return null; }
    }

    private static void SaveState(string guid, string path)
    {
        try { File.WriteAllText(StateFilePath, JsonSerializer.Serialize(new RobloxStateFile(guid, path))); }
        catch { }
    }

    private string? FindVersionFolder()
    {
        if (_currentVersionFolder != null &&
            Directory.Exists(_currentVersionFolder) &&
            IsVersionComplete(_currentVersionFolder))
            return _currentVersionFolder;

        var state = LoadState();
        if (state != null && IsVersionComplete(state.VersionPath))
        {
            _currentVersionFolder = state.VersionPath;
            return _currentVersionFolder;
        }

        if (Directory.Exists(VersionsDir))
        {
            var found = Directory.GetDirectories(VersionsDir)
                .Where(IsVersionComplete)
                .OrderByDescending(d => new DirectoryInfo(d).LastWriteTime)
                .FirstOrDefault();
            if (found != null)
            {
                _currentVersionFolder = found;
                return _currentVersionFolder;
            }
        }

        _currentVersionFolder = FindStockRobloxVersionFolder();
        return _currentVersionFolder;
    }

    private string? FindNexStrapRobloxPlayerPath()
    {
        var versionFolder = FindVersionFolder();
        if (versionFolder == null) return null;
        var playerExe = Path.Combine(versionFolder, "RobloxPlayerBeta.exe");
        return File.Exists(playerExe) ? playerExe : null;
    }

    // -------------------------------------------------------------------------
    // Launch
    // -------------------------------------------------------------------------
    public async Task<bool> LaunchAsync(string? launchArgs = null, bool autoUpdate = true,
        LaunchOptions? options = null)
    {
        options ??= new LaunchOptions();

        await CheckAndInstallVcRedistAsync();

        // マルチインスタンス: NexStrap が ROBLOX_singletonMutex を保持することで
        // 新しい Roblox インスタンスがシングルトンチェックをパスできる
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
                Log($"Update available: {installedGuid} → {latestGuid}");
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

        // 初回インストール
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

        // 起動直前にクッキーを注入（タイミングを最小化）
        if (options.CookieToInject != null)
        {
            var ok = InjectAccountCookie(options.CookieToInject);
            Log(ok ? "Cookie injected successfully before launch" : "Cookie injection failed (file may be locked)");
        }

        // Stretch Resolution — Roblox 起動前に解像度を変更
        if (options.StretchResolution)
            ApplyStretchResolution(options.StretchWidth, options.StretchHeight);

        Log($"Launching: {playerPath} args={launchArgs ?? "(none)"}");
        SetStatus(RobloxStatus.Launching);
        var proc = TryStartProcess(playerPath, launchArgs);
        if (proc == null) { SetStatus(RobloxStatus.Idle); return false; }

        await Task.Delay(3000);
        if (!proc.HasExited)
            return SetLaunched(proc, options);

        // 即終了 — 壊れているので強制再インストールして一度だけリトライ
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
            var ok = InjectAccountCookie(options.CookieToInject);
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

    /// <summary>CPU アフィニティ・メモリ上限・クラッシュハンドラ抑制を起動後に適用する。</summary>
    public async Task PostLaunchAsync(Process proc, LaunchOptions opts)
    {
        await Task.Delay(1500); // Roblox の初期化を少し待つ

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

        // メモリ上限 (RAM の半分 or 4GB の小さいほう)
        // 2GB 上限では現代の Roblox が頻繁にページアウトしパフォーマンスが低下するため 4GB に変更
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
    // Account cookie injection — RobloxCookies.dat に対象アカウントを書き込む
    // -------------------------------------------------------------------------
    private static readonly string RobloxCookiesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "LocalStorage", "RobloxCookies.dat");

    public static void ClearRobloxCookies()
    {
        try { File.Delete(RobloxCookiesPath); } catch { }
    }

    private static readonly string AppStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "LocalStorage", "appStorage.json");

    /// <summary>
    /// appStorage.json のセッション関連フィールドをクリアする。
    /// Roblox が保存済みセッションを使わず auth ticket を使うようにするため。
    /// </summary>
    public static void ClearAppStorageSession()
    {
        if (!File.Exists(AppStoragePath)) return;
        try
        {
            var json = File.ReadAllText(AppStoragePath);
            var obj  = Newtonsoft.Json.Linq.JObject.Parse(json);
            // セッション関連フィールドを空にする
            obj["CredentialValue"] = "";
            obj["AccountBlob"]     = "";
            if (obj.ContainsKey("WebLogin")) obj["WebLogin"] = Newtonsoft.Json.Linq.JValue.CreateNull();
            File.WriteAllText(AppStoragePath, obj.ToString(Newtonsoft.Json.Formatting.None));
            Log("AppStorage session cleared for multi-account launch");
        }
        catch (Exception ex) { Log($"ClearAppStorageSession failed: {ex.Message}"); }
    }

    public static bool InjectAccountCookie(string robloSecurityCookie, string? targetPath = null)
    {
        var cookiesFilePath = targetPath ?? RobloxCookiesPath;
        try
        {
            string cookiesJson;
            if (File.Exists(cookiesFilePath))
            {
                cookiesJson = File.ReadAllText(cookiesFilePath);
                var obj     = System.Text.Json.JsonDocument.Parse(cookiesJson).RootElement;
                if (!obj.TryGetProperty("CookiesData", out var cookiesDataElem)) goto write_fresh;
                var encB64  = cookiesDataElem.GetString();
                if (encB64 == null) goto write_fresh;

                var encrypted = Convert.FromBase64String(encB64);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                var text      = System.Text.Encoding.UTF8.GetString(decrypted);

                // Netscape cookie 形式: フィールドはタブ区切り、6番目が name、7番目が value
                var lines  = text.Split('\n').ToList();
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    var parts = lines[i].Split('\t');
                    if (parts.Length >= 7 && parts[5] == ".ROBLOSECURITY")
                    {
                        parts[6]  = robloSecurityCookie;
                        lines[i]  = string.Join("\t", parts);
                        found     = true;
                        break;
                    }
                }

                if (!found)
                {
                    // エントリがなければ追加
                    lines.Add($"#HttpOnly_.roblox.com\tTRUE\t/\tTRUE\t0\t.ROBLOSECURITY\t{robloSecurityCookie}");
                }

                var newText      = string.Join("\n", lines);
                var newBytes     = System.Text.Encoding.UTF8.GetBytes(newText);
                var newEncrypted = ProtectedData.Protect(newBytes, null, DataProtectionScope.CurrentUser);
                var newJson      = $"{{\"CookiesVersion\":\"1\",\"CookiesData\":\"{Convert.ToBase64String(newEncrypted)}\"}}";
                File.WriteAllText(cookiesFilePath, newJson);
                return true;
            }

            write_fresh:
            {
                var lines    = new[] { $"#HttpOnly_.roblox.com\tTRUE\t/\tTRUE\t0\t.ROBLOSECURITY\t{robloSecurityCookie}" };
                var bytes    = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines));
                var enc      = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                var json     = $"{{\"CookiesVersion\":\"1\",\"CookiesData\":\"{Convert.ToBase64String(enc)}\"}}";
                Directory.CreateDirectory(Path.GetDirectoryName(cookiesFilePath)!);
                File.WriteAllText(cookiesFilePath, json);
                return true;
            }
        }
        catch (Exception ex)
        {
            Log($"InjectAccountCookie failed: {ex.Message}");
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Multi-instance: Roblox プロセス内の singletonMutex ハンドルを強制クローズ
    // -------------------------------------------------------------------------
    private void AcquireRobloxSingletonMutex()
    {
        // 既存 Roblox が持つ mutex ハンドルを強制クローズして次の起動が通るようにする
        CloseRobloxSingletonMutexHandles();

        // NexStrap が mutex を再取得して次の Roblox インスタンスがスタートアップ後に
        // 解放されたことを検知できるようにする
        if (_multiInstanceMutex != IntPtr.Zero) return;
        _multiInstanceMutex = CreateMutex(IntPtr.Zero, true, "ROBLOX_singletonMutex");
        Log("Multi-instance mutex acquired");
    }

    public void ReleaseRobloxSingletonMutex()
    {
        if (_multiInstanceMutex == IntPtr.Zero) return;
        ReleaseMutex(_multiInstanceMutex);
        CloseHandle(_multiInstanceMutex);
        _multiInstanceMutex = IntPtr.Zero;
        Log("Multi-instance mutex released");
    }

    private void CloseRobloxSingletonMutexHandles()
    {
        var robloxPids = new HashSet<uint>(
            Process.GetProcessesByName("RobloxPlayerBeta")
                   .Concat(Process.GetProcessesByName("RobloxPlayer"))
                   .Where(p => !p.HasExited)
                   .Select(p => (uint)p.Id));

        if (robloxPids.Count == 0) return;

        uint bufSize = 4 * 1024 * 1024;
        IntPtr buf = IntPtr.Zero;
        try
        {
            uint needed;
            uint status;
            do
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                buf = Marshal.AllocHGlobal((int)bufSize);
                status = NtQuerySystemInformation(SysHandleInfo, buf, bufSize, out needed);
                bufSize = Math.Max(needed + 4096, bufSize * 2);
            }
            while (status == StatusInfoLenMismatch);

            if (status != 0) return;

            int count    = Marshal.ReadInt32(buf);
            int entSz    = Marshal.SizeOf<SystemHandleEntry>();
            IntPtr start = IntPtr.Add(buf, sizeof(uint));
            var self     = Process.GetCurrentProcess().Handle;

            for (int i = 0; i < count; i++)
            {
                var e = Marshal.PtrToStructure<SystemHandleEntry>(IntPtr.Add(start, i * entSz));
                if (!robloxPids.Contains(e.Pid)) continue;

                IntPtr robloxProc = OpenProcess(ProcDupHandle, false, e.Pid);
                if (robloxProc == IntPtr.Zero) continue;
                try
                {
                    IntPtr dup;
                    if (NtDuplicateObject(robloxProc, (IntPtr)e.Handle, self, out dup, 0, 0, DupSameAccess) != 0)
                        continue;
                    try
                    {
                        if (!IsHandleNamedMutex(dup, "ROBLOX_singletonMutex")) continue;
                        NtDuplicateObject(robloxProc, (IntPtr)e.Handle, IntPtr.Zero, out _, 0, 0, DupCloseSource);
                        Log($"Closed ROBLOX_singletonMutex handle in Roblox PID {e.Pid}");
                    }
                    finally { CloseHandle(dup); }
                }
                finally { CloseHandle(robloxProc); }
            }
        }
        catch (Exception ex) { Log($"CloseRobloxSingletonMutexHandles: {ex.Message}"); }
        finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
    }

    private static bool IsHandleNamedMutex(IntPtr handle, string targetName)
    {
        const int bufSize = 1024;
        IntPtr buf = Marshal.AllocHGlobal(bufSize);
        try
        {
            uint ret;
            if (NtQueryObject(handle, 1 /*ObjectNameInformation*/, buf, bufSize, out ret) != 0)
                return false;
            // OBJECT_NAME_INFORMATION: UNICODE_STRING (Length, MaxLength, Buffer*)
            ushort len = (ushort)Marshal.ReadInt16(buf);
            if (len == 0) return false;
            IntPtr strPtr = Marshal.ReadIntPtr(IntPtr.Add(buf, IntPtr.Size == 8 ? 8 : 4));
            string name = Marshal.PtrToStringUni(strPtr, len / 2);
            return name.EndsWith(targetName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // -------------------------------------------------------------------------
    // Install
    // -------------------------------------------------------------------------
    private async Task<string?> InstallVersionAsync(string versionGuid, bool forceReinstall = false)
    {
        // 同時インストール防止
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

        // 1. 既にインストール済み
        if (IsVersionComplete(versionDir))
        {
            _currentVersionFolder = versionDir;
            return Path.Combine(versionDir, "RobloxPlayerBeta.exe");
        }

        // 2. ストック Roblox の正確なバージョンからコピー (CDN 不要の高速パス)
        var stockFolder = FindStockRobloxVersionFolder(versionGuid);
        if (stockFolder != null)
        {
            Log($"Copying from stock Roblox: {stockFolder}");
            Directory.CreateDirectory(versionDir);
            await CopyDirectoryAsync(stockFolder, versionDir);
        }

        // 3. CDN ダウンロード
        if (!IsVersionComplete(versionDir))
        {
            _installCts = new CancellationTokenSource();
            var ok = await DownloadAndInstallAsync(versionGuid, versionDir, _installCts.Token);
            _installCts.Dispose();
            _installCts = null;

            if (!ok)
            {
                // CDN 完全失敗 — 正確なバージョンのストック Roblox があればコピー
                var stockFallback = FindStockRobloxVersionFolder(versionGuid);
                if (stockFallback != null)
                {
                    Log($"CDN failed, copying from stock Roblox: {stockFallback}");
                    Directory.CreateDirectory(versionDir);
                    await CopyDirectoryAsync(stockFallback, versionDir);
                }
                else
                {
                    // 最終手段: 公式インストーラーで正確なバージョンを取得後コピー
                    await RunOfficialInstallerAsync();
                    var newStock = FindStockRobloxVersionFolder(versionGuid);
                    if (newStock != null)
                    {
                        Log($"Copying from newly installed stock Roblox: {newStock}");
                        Directory.CreateDirectory(versionDir);
                        await CopyDirectoryAsync(newStock, versionDir);
                    }
                }
            }
        }

        if (!IsVersionComplete(versionDir)) return null;
        _currentVersionFolder = versionDir;
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

    // -------------------------------------------------------------------------
    // Display Scaling — SetDisplayConfig で GPU/モニター設定を上書き
    // DISPLAYCONFIG_SCALING_STRETCHED(3) = 黒帯なし強制引き伸ばし
    // -------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    private struct ScLuid { public uint L; public int H; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ScRational { public uint N; public uint D; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScPathSrc { public ScLuid adapterId; public uint id, modeIdx, flags; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ScPathTgt {
        public ScLuid adapterId;                           // 8
        public uint id, modeIdx, outTech, rotation;        // 4×4=16
        public uint scaling;                               // 4  ← DISPLAYCONFIG_SCALING
        public ScRational refreshRate;                     // 8
        public uint scanLineOrder;                         // 4
        public byte available;                             // 1  ← BOOLEAN は BYTE (1バイト)
        public byte _pad0, _pad1, _pad2;                   // 3  padding
        public uint statusFlags;                           // 4
    }   // total = 48 bytes

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ScPath { public ScPathSrc src; public ScPathTgt tgt; public uint flags; }
    // total = 20 + 48 + 4 = 72 bytes

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct ScMode {
        // DISPLAYCONFIG_MODE_INFO = 4(infoType)+4(id)+8(luid)+48(union) = 64 bytes
        [FieldOffset( 0)] public uint infoType;
        [FieldOffset( 4)] public uint id;
        [FieldOffset( 8)] public ScLuid adapterId;
        [FieldOffset(16)] public long _u0; [FieldOffset(24)] public long _u1;
        [FieldOffset(32)] public long _u2; [FieldOffset(40)] public long _u3;
        [FieldOffset(48)] public long _u4; [FieldOffset(56)] public long _u5;
    }

    [DllImport("user32.dll")] private static extern uint GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    // IntPtr 版 — struct marshaling を完全にバイパスしてオフセット直接操作
    [DllImport("user32.dll", EntryPoint = "QueryDisplayConfig")]
    private static extern uint QueryDisplayConfigPtr(uint f, ref uint np, IntPtr pa, ref uint nm, IntPtr ma, IntPtr id);
    [DllImport("user32.dll", EntryPoint = "SetDisplayConfig")]
    private static extern uint SetDisplayConfigPtr(uint np, IntPtr pa, uint nm, IntPtr ma, uint flags);

    private const uint QDC_ACTIVE   = 0x2;
    private const uint QDC_VMAWARE  = 0x10;
    private const uint SDC_APPLY    = 0x200;
    private const uint SDC_SUPPLIED = 0x10;
    private const uint SDC_CHANGES  = 0x1000;
    private const uint SDC_NO_OPT   = 0x400;
    private const uint SDC_VMAWARE  = 0x80000;
    private const uint SC_STRETCHED = 3;
    private const uint SC_ASPECT    = 4;
    private uint _origScaling = SC_ASPECT;

    // DISPLAYCONFIG_PATH_INFO 内のフィールドオフセット
    // sourceInfo(20) + targetInfo: adapterId(8)+id(4)+modeIdx(4)+outTech(4)+rot(4)+scaling(4)=offset 44
    private const int PATH_SIZE       = 72;
    private const int PATH_SRC_MIDX   = 12;  // sourceInfo.modeInfoIdx
    private const int PATH_TGT_MIDX   = 32;  // targetInfo.modeInfoIdx  (20+12)
    private const int PATH_TGT_OTECH  = 36;  // targetInfo.outputTechnology (20+16)
    private const int PATH_TGT_SCALE  = 44;  // targetInfo.scaling (20+24)

    /// <summary>
    /// SetDisplayConfig でソースモードの解像度を変更する。
    /// ChangeDisplaySettings と違い完全モード再設定を行うため
    /// Intel/NVIDIA ドライバーのデフォルトパネルスケール（フルパネル）が適用され黒帯が出ない。
    /// </summary>
    private const uint INTERNAL_DISPLAY = 0x80000000; // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL

    /// <summary>
    /// QueryDisplayConfig で現在の設定を取得し、無変更で SetDisplayConfig に渡す。
    /// CCD パスを通ることで Intel/NVIDIA ドライバーがフルモード再設定を行い
    /// フルパネルスケール（引き伸ばし）が適用される。
    /// Windows Settings でリフレッシュレートを変更するのと同じ効果。
    /// </summary>
    /// <summary>
    /// 1280×960 で利用可能な「現在とは異なる Hz」を探して一時的に切り替え、すぐ戻す。
    /// Windows Settings でリフレッシュレートを変更すると黒帯が消える現象を自動再現。
    /// </summary>
    private void TriggerFullModeSetByHzToggle(int width, int height)
    {
        try
        {
            var cur = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref cur)) return;
            int origHz = cur.dmDisplayFrequency;
            Log($"TriggerHzToggle: current Hz={origHz} at {width}x{height}");

            // 現在と違う Hz を探す
            int altHz = -1;
            for (int n = 0; ; n++)
            {
                var m = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (!EnumDisplaySettings(null, n, ref m)) break;
                if (m.dmPelsWidth == width && m.dmPelsHeight == height &&
                    m.dmDisplayFrequency != origHz && m.dmDisplayFrequency > 0)
                {
                    altHz = m.dmDisplayFrequency;
                    break;
                }
            }

            if (altHz < 0) { Log("TriggerHzToggle: no alternate Hz found"); return; }

            Log($"TriggerHzToggle: switching {origHz}Hz → {altHz}Hz → {origHz}Hz");
            const int DM_FREQ = 0x400000;

            // 別 Hz に切り替え（Intel ドライバーのフルモード再設定を起動）
            var dm1 = cur;
            dm1.dmPelsWidth = width; dm1.dmPelsHeight = height;
            dm1.dmDisplayFrequency = altHz;
            dm1.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_FREQ;
            var r1 = ChangeDisplaySettings(ref dm1, 0);
            Log($"TriggerHzToggle switch to {altHz}Hz: {r1}");

            // 元の Hz に戻す
            var dm2 = cur;
            dm2.dmPelsWidth = width; dm2.dmPelsHeight = height;
            dm2.dmDisplayFrequency = origHz;
            dm2.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_FREQ;
            var r2 = ChangeDisplaySettings(ref dm2, 0);
            Log($"TriggerHzToggle restore to {origHz}Hz: {r2}");
        }
        catch (Exception ex) { Log($"TriggerHzToggle: {ex.Message}"); }
    }

    /// <summary>
    /// QDC_ONLY_ACTIVE_PATHS で取得した target mode の Hz を 1 フレーム分だけ変えて
    /// SetDisplayConfig で適用する。Windows Settings の Hz 変更と同じ操作。
    /// </summary>
    private void TrySetDisplayConfigWithHzNudge()
    {
        try
        {
            const uint QF = QDC_ACTIVE; // NO virtual-mode-aware → tgtModeIdx が直接インデックス
            if (GetDisplayConfigBufferSizes(QF, out uint np, out uint nm) != 0) return;

            const int MODE_SIZE = 64;
            var pBuf = Marshal.AllocHGlobal(PATH_SIZE * (int)np);
            var mBuf = Marshal.AllocHGlobal(MODE_SIZE * (int)nm);
            try
            {
                uint np2 = np, nm2 = nm;
                if (QueryDisplayConfigPtr(QF, ref np2, pBuf, ref nm2, mBuf, IntPtr.Zero) != 0) return;

                // 内蔵ディスプレイの path を探して tgtModeIdx を取得
                for (int i = 0; i < np2; i++)
                {
                    uint ot = (uint)Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_TGT_OTECH);
                    if (ot != INTERNAL_DISPLAY) continue;

                    int tgtIdx = Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_TGT_MIDX);
                    Log($"HzNudge: internal path[{i}] tgtModeIdx={tgtIdx} nm={nm2}");
                    if (tgtIdx < 0 || tgtIdx >= nm2) { Log("HzNudge: invalid tgtIdx"); break; }

                    // target mode: DISPLAYCONFIG_VIDEO_SIGNAL_INFO の pixelRate は offset 16
                    // hSyncFreq(N=20,D=24), vSyncFreq(N=28,D=32)
                    int mBase = tgtIdx * MODE_SIZE + 16; // union start in MODE_INFO
                    uint pixRateHi = (uint)Marshal.ReadInt32(mBuf, mBase + 4); // high 32 of UINT64
                    uint vNumer   = (uint)Marshal.ReadInt32(mBuf, mBase + 20);
                    uint vDenom   = (uint)Marshal.ReadInt32(mBuf, mBase + 24);
                    Log($"HzNudge: vSyncFreq={vNumer}/{vDenom} ({(vDenom>0?(double)vNumer/vDenom:0):F2}Hz)");

                    // Hz を +1 してから元に戻す（各 SetDisplayConfig を独立して試みる）
                    uint origNumer = vNumer;
                    Marshal.WriteInt32(mBuf, mBase + 20, (int)(vNumer + vDenom)); // +1Hz
                    var r1 = SetDisplayConfigPtr(np2, pBuf, nm2, mBuf,
                        SDC_SUPPLIED | SDC_APPLY | SDC_CHANGES | SDC_NO_OPT);
                    Log($"HzNudge +1Hz result={r1}");

                    Marshal.WriteInt32(mBuf, mBase + 20, (int)origNumer); // restore
                    var r2 = SetDisplayConfigPtr(np2, pBuf, nm2, mBuf,
                        SDC_SUPPLIED | SDC_APPLY | SDC_CHANGES | SDC_NO_OPT);
                    Log($"HzNudge restore result={r2}");
                    break;
                }
            }
            finally { Marshal.FreeHGlobal(pBuf); Marshal.FreeHGlobal(mBuf); }
        }
        catch (Exception ex) { Log($"TrySetDisplayConfigWithHzNudge: {ex.Message}"); }
    }

    private void ReapplyCurrentConfigViaSetDisplayConfig()
    {
        try
        {
            const uint SDC_USE_DB_CURRENT = 0xF; // SDC_TOPOLOGY_INTERNAL|CLONE|EXTEND|EXTERNAL
            const uint SDC_SAVE_DB        = 0x800;

            // 試行0: QDC_ONLY_ACTIVE_PATHS (no VMAWARE) で target mode の Hz を微調整して再適用
            // → Windows Settings が Hz 変更に使う SetDisplayConfig と同等の操作
            TrySetDisplayConfigWithHzNudge();

            // 試行1: データベースの現在トポロジを再適用（パスなし）
            // Windows Settings が内部的に行うことと同等
            var r0 = SetDisplayConfigPtr(0, IntPtr.Zero, 0, IntPtr.Zero,
                SDC_USE_DB_CURRENT | SDC_APPLY);
            Log($"ReapplyUseDbCurrent result={r0}");
            if (r0 == 0) return;

            // 試行2: オーバーサイズバッファで QueryDisplayConfig → SetDisplayConfig
            // PATH_SIZE の計算ミスによるバッファオーバーフロー回避のため 256 バイト/要素で確保
            foreach (uint qf in new uint[] { QDC_ACTIVE, QDC_ACTIVE | QDC_VMAWARE })
            {
                if (GetDisplayConfigBufferSizes(qf, out uint np, out uint nm) != 0) continue;

                const int OVERSIZED_PATH = 256;
                const int OVERSIZED_MODE = 256;
                var pBuf = Marshal.AllocHGlobal(OVERSIZED_PATH * (int)np);
                var mBuf = Marshal.AllocHGlobal(OVERSIZED_MODE * (int)nm);
                try
                {
                    // ゼロ初期化
                    for (int i = 0; i < OVERSIZED_PATH * np; i++) Marshal.WriteByte(pBuf, i, 0);
                    for (int i = 0; i < OVERSIZED_MODE * nm; i++) Marshal.WriteByte(mBuf, i, 0);

                    uint np2 = np, nm2 = nm;
                    var qr = QueryDisplayConfigPtr(qf, ref np2, pBuf, ref nm2, mBuf, IntPtr.Zero);
                    Log($"QueryDisplayConfig qf=0x{qf:X} oversized: result={qr} np={np2} nm={nm2}");
                    if (qr != 0) continue;

                    foreach (uint flags in new uint[] {
                        SDC_SUPPLIED | SDC_APPLY,
                        SDC_SUPPLIED | SDC_APPLY | SDC_CHANGES,
                        SDC_SUPPLIED | SDC_APPLY | SDC_NO_OPT | SDC_CHANGES,
                        SDC_SUPPLIED | SDC_APPLY | SDC_NO_OPT | SDC_VMAWARE | SDC_CHANGES,
                        SDC_SUPPLIED | SDC_APPLY | SDC_SAVE_DB | SDC_CHANGES,
                    })
                    {
                        var r = SetDisplayConfigPtr(np2, pBuf, nm2, mBuf, flags);
                        Log($"Reapply oversized qf=0x{qf:X} flags=0x{flags:X} result={r}");
                        if (r == 0) return;
                    }
                }
                finally { Marshal.FreeHGlobal(pBuf); Marshal.FreeHGlobal(mBuf); }
            }
        }
        catch (Exception ex) { Log($"ReapplyCurrentConfig: {ex.Message}"); }
    }

    private bool ApplyResolutionViaSetDisplayConfig(int width, int height)
    {
        try
        {
            uint qf = QDC_ACTIVE | QDC_VMAWARE;
            if (GetDisplayConfigBufferSizes(qf, out uint np, out uint nm) != 0) return false;

            foreach (int modeSize in new[] { 64, 80 })
            {
                var pBuf = Marshal.AllocHGlobal(PATH_SIZE * (int)np);
                var mBuf = Marshal.AllocHGlobal(modeSize * (int)nm);
                try
                {
                    uint np2 = np, nm2 = nm;
                    if (QueryDisplayConfigPtr(qf, ref np2, pBuf, ref nm2, mBuf, IntPtr.Zero) != 0) continue;

                    // 内蔵パネル (INTERNAL) のパスが参照するソースモードインデックスを収集
                    // デュアルモニター時に外部モニター非対応解像度で全体が失敗するのを防ぐ
                    var internalSrcIdx = new System.Collections.Generic.HashSet<int>();
                    for (int i = 0; i < np2; i++)
                    {
                        uint outTech  = (uint)Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_TGT_OTECH);
                        int srcModeIdx = Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_SRC_MIDX);
                        int tgtModeIdx = Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_TGT_MIDX);
                        int pathFlags  = Marshal.ReadInt32(pBuf, i * PATH_SIZE + (PATH_SIZE - 4));
                        Log($"  path[{i}] outTech=0x{outTech:X} srcIdx={srcModeIdx} tgtIdx={tgtModeIdx} pathFlags=0x{pathFlags:X}");
                        if (outTech == INTERNAL_DISPLAY)
                        {
                            internalSrcIdx.Add(srcModeIdx & 0xFFFF);
                        }
                    }

                    if (internalSrcIdx.Count == 0) { Log("No internal display found"); continue; }

                    // 内蔵パネルのソースモードのみ変更
                    bool changed = false;
                    for (int i = 0; i < nm2; i++)
                    {
                        int baseOff = i * modeSize;
                        if ((uint)Marshal.ReadInt32(mBuf, baseOff) != 1) continue; // SOURCE mode only
                        if (!internalSrcIdx.Contains(i)) continue; // internal only

                        int origW = Marshal.ReadInt32(mBuf, baseOff + 16);
                        int origH = Marshal.ReadInt32(mBuf, baseOff + 20);
                        Log($"  Modifying mode[{i}] modeSize={modeSize} {origW}x{origH} → {width}x{height}");
                        Marshal.WriteInt32(mBuf, baseOff + 16, width);
                        Marshal.WriteInt32(mBuf, baseOff + 20, height);
                        changed = true;
                    }
                    if (!changed) { Log($"No internal SOURCE mode found (modeSize={modeSize})"); continue; }

                    uint flags = SDC_SUPPLIED | SDC_APPLY | SDC_CHANGES | SDC_NO_OPT | SDC_VMAWARE;
                    var r = SetDisplayConfigPtr(np2, pBuf, nm2, mBuf, flags);
                    Log($"SetDisplayConfig internal resolution {width}x{height} (modeSize={modeSize}) result={r}");
                    if (r == 0) return true;
                }
                finally { Marshal.FreeHGlobal(pBuf); Marshal.FreeHGlobal(mBuf); }
            }
        }
        catch (Exception ex) { Log($"ApplyResolutionViaSetDisplayConfig: {ex.Message}"); }
        return false;
    }

    private void ApplyDisplayScaling(bool stretch)
    {
        try
        {
            uint qf = QDC_ACTIVE | QDC_VMAWARE;
            if (GetDisplayConfigBufferSizes(qf, out uint np, out uint nm) != 0) { Log("GetDisplayConfigBufferSizes failed"); return; }
            Log($"DisplayConfig buffers: paths={np} modes={nm}");

            // sizeof(DISPLAYCONFIG_MODE_INFO): 64 or 80 depending on SDK/Windows version → try both
            foreach (int modeSize in new[] { 64, 80 })
            {
                var pBuf = Marshal.AllocHGlobal(PATH_SIZE * (int)np);
                var mBuf = Marshal.AllocHGlobal(modeSize * (int)nm);
                try
                {
                    uint np2 = np, nm2 = nm;
                    var qr = QueryDisplayConfigPtr(qf, ref np2, pBuf, ref nm2, mBuf, IntPtr.Zero);
                    if (qr != 0) { Log($"QueryDisplayConfig(modeSize={modeSize}) fail={qr}"); continue; }

                    for (int i = 0; i < np2; i++)
                    {
                        int curScale = Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_TGT_SCALE);
                        int outTech  = Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_TGT_OTECH);
                        Log($"  path[{i}] modeSize={modeSize} scaling={curScale} outTech=0x{outTech:X}");
                        if (stretch && i == 0) _origScaling = (uint)curScale;
                    }

                    uint target = stretch ? SC_STRETCHED : _origScaling;
                    for (int i = 0; i < np2; i++)
                        Marshal.WriteInt32(pBuf, i * PATH_SIZE + PATH_TGT_SCALE, (int)target);

                    // 試行A: modes ありで適用
                    uint fA = SDC_SUPPLIED | SDC_APPLY | SDC_CHANGES | SDC_NO_OPT | SDC_VMAWARE;
                    var rA = SetDisplayConfigPtr(np2, pBuf, nm2, mBuf, fA);
                    Log($"SetDisplayConfig A (modeSize={modeSize}) scaling={target} result={rA}");
                    if (rA == 0) return;

                    // 試行B: modeIdx を INVALID に設定して modes なし
                    for (int i = 0; i < np2; i++)
                    {
                        Marshal.WriteInt32(pBuf, i * PATH_SIZE + PATH_SRC_MIDX, -1);
                        Marshal.WriteInt32(pBuf, i * PATH_SIZE + PATH_TGT_MIDX, -1);
                    }
                    uint fB = SDC_SUPPLIED | SDC_APPLY | SDC_CHANGES | SDC_VMAWARE;
                    var rB = SetDisplayConfigPtr(np2, pBuf, 0, IntPtr.Zero, fB);
                    Log($"SetDisplayConfig B (modeSize={modeSize},noModes) scaling={target} result={rB}");
                    if (rB == 0) return;
                }
                finally { Marshal.FreeHGlobal(pBuf); Marshal.FreeHGlobal(mBuf); }
            }
            Log("All SetDisplayConfig attempts failed");
        }
        catch (Exception ex) { Log($"ApplyDisplayScaling: {ex.Message}"); }
    }

    // -------------------------------------------------------------------------
    // Stretched Resolution — ChangeDisplaySettings (Win32)
    // -------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int   dmFields;
        public int   dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int   dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int   dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public int   dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll")]
    private static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DM_PELSWIDTH  = 0x80000;
    private const int DM_PELSHEIGHT = 0x100000;
    private const int DISP_CHANGE_SUCCESSFUL = 0;

    private DEVMODE _originalDevMode;
    private bool    _stretchActive;
    private bool    _originalFullscreen;

    public bool IsStretchActive => _stretchActive;

    // GlobalBasicSettings_13.xml のパス（全 Roblox アカウント共通）
    private static readonly string GlobalBasicSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "GlobalBasicSettings_13.xml");

    /// <summary>
    /// GlobalBasicSettings の bool 値を書き換える。
    /// name="Fullscreen" → true/false
    /// </summary>
    private static bool SetGlobalBasicBool(string name, bool value)
    {
        try
        {
            if (!File.Exists(GlobalBasicSettingsPath)) return false;
            var xml = File.ReadAllText(GlobalBasicSettingsPath);
            var updated = System.Text.RegularExpressions.Regex.Replace(
                xml,
                $@"<bool name=""{name}"">(true|false)</bool>",
                $"<bool name=\"{name}\">{(value ? "true" : "false")}</bool>");
            File.WriteAllText(GlobalBasicSettingsPath, updated);
            return true;
        }
        catch { return false; }
    }

    private static bool GetGlobalBasicBool(string name)
    {
        try
        {
            if (!File.Exists(GlobalBasicSettingsPath)) return false;
            var xml = File.ReadAllText(GlobalBasicSettingsPath);
            var m = System.Text.RegularExpressions.Regex.Match(
                xml, $@"<bool name=""{name}"">(true|false)</bool>");
            return m.Success && m.Groups[1].Value == "true";
        }
        catch { return false; }
    }

    public bool ApplyStretchResolution(int width, int height)
    {
        // 既に適用済みなら二重適用しない
        if (_stretchActive) return true;

        // 現在の解像度を保存
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)) return false;
        _originalDevMode = dm;

        // 1. Roblox を強制フルスクリーンに設定（ウィンドウモードでは GPU stretch が効かない）
        _originalFullscreen = GetGlobalBasicBool("Fullscreen");
        SetGlobalBasicBool("Fullscreen", true);
        Log($"Roblox Fullscreen set to true (was {_originalFullscreen})");

        // 2. SetDisplayConfig でソース解像度を変更（完全モード再設定 → ドライバーがフルパネルスケールを適用）
        //    失敗時は従来の ChangeDisplaySettings にフォールバック
        bool ok;
        if (ApplyResolutionViaSetDisplayConfig(width, height))
        {
            ok = true;
            _stretchActive = true;
            Log($"Stretch resolution applied via SetDisplayConfig: {width}x{height}");
        }
        else
        {
            // SetDisplayConfig は NVIDIA Optimus + Intel iGPU 環境で全パターン result=87
            // → ChangeDisplaySettings で解像度のみ変更（黒帯はユーザーが Intel GCC で設定）
            dm.dmPelsWidth  = width;
            dm.dmPelsHeight = height;
            dm.dmFields     = DM_PELSWIDTH | DM_PELSHEIGHT;
            ok = ChangeDisplaySettings(ref dm, 0) == DISP_CHANGE_SUCCESSFUL;
            if (ok) _stretchActive = true;
            Log(ok ? $"Stretch resolution applied: {width}x{height}" : $"Stretch resolution failed: {width}x{height}");
        }
        return ok;
    }

    public void RestoreResolution()
    {
        if (!_stretchActive) return;

        // 表示解像度を復元
        ChangeDisplaySettings(ref _originalDevMode, 0);
        _stretchActive = false;
        Log("Display resolution restored");

        // Windows スケーリングを元に戻す
        ApplyDisplayScaling(false);

        // Roblox のフルスクリーン設定を元に戻す
        SetGlobalBasicBool("Fullscreen", _originalFullscreen);
        Log($"Roblox Fullscreen restored to {_originalFullscreen}");
    }

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
            // UseShellExecute = false で環境変数を上書きできる
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
        RestoreResolution(); // Stretch Resolution を使っていた場合に復元
        SetStatus(RobloxStatus.Idle);
    }

    // -------------------------------------------------------------------------
    // Directory copy
    // -------------------------------------------------------------------------
    private async Task CopyDirectoryAsync(string source, string dest)
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
                ReportProgress($"Copying {Path.GetFileName(file)}", pct);
            }
        });
        Log("Directory copy complete");
    }

    // -------------------------------------------------------------------------
    // Official installer fallback
    // -------------------------------------------------------------------------
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
                var bytes = await Http.GetByteArrayAsync("https://setup.rbxcdn.com/RobloxPlayerInstaller.exe");
                await File.WriteAllBytesAsync(installerExe, bytes);
                installerPath = installerExe;
            }
            catch (Exception ex)
            {
                Log($"Failed to download official installer: {ex.Message}");
                return;
            }
        }

        // インストーラー実行前に存在する Roblox プロセスを記録
        var existingPids = Process.GetProcessesByName("RobloxPlayerBeta")
            .Select(p => p.Id).ToHashSet();

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

        // インストーラーが自動起動した Roblox を終了させる
        foreach (var roblox in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            if (existingPids.Contains(roblox.Id)) continue;
            try
            {
                roblox.Kill();
                Log($"Killed installer-spawned Roblox (PID {roblox.Id})");
            }
            catch { }
        }
    }

    // -------------------------------------------------------------------------
    // Stock Roblox helpers
    // -------------------------------------------------------------------------
    private static string? FindStockRobloxVersionFolder(string? targetGuid = null)
    {
        if (!Directory.Exists(StockRobloxVersionsDir)) return null;
        if (targetGuid != null)
        {
            var specific = Path.Combine(StockRobloxVersionsDir, $"version-{targetGuid}");
            return Directory.Exists(specific) && IsVersionComplete(specific) ? specific : null;
        }
        return Directory.GetDirectories(StockRobloxVersionsDir)
            .OrderByDescending(d => new DirectoryInfo(d).LastWriteTime)
            .FirstOrDefault(IsVersionComplete);
    }

    private static string? FindFileInStockRoblox(string filename)
    {
        if (!Directory.Exists(StockRobloxVersionsDir)) return null;
        return Directory.GetDirectories(StockRobloxVersionsDir)
            .Select(d => Path.Combine(d, filename))
            .FirstOrDefault(File.Exists);
    }

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
            _cdnBaseUrl = await TestConnectivityAsync(ct) ?? "https://setup.rbxcdn.com";
            Log($"CDN winner: {_cdnBaseUrl}");

            ReportProgress("Fetching package list...", 3);
            Log($"Fetching manifest for: {versionGuid}");
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

            // 展開ファイル数を先に集計 (進捗精度のため)
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

    // -------------------------------------------------------------------------
    // CDN connectivity test
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

    // -------------------------------------------------------------------------
    // Manifest fetch
    // -------------------------------------------------------------------------
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
                    if (baseUrl != _cdnBaseUrl)
                    {
                        Log($"CDN switched: {_cdnBaseUrl} → {baseUrl}");
                        _cdnBaseUrl = baseUrl;
                    }
                    return pkgs;
                }
            }
            catch (Exception ex) { Log($"Manifest fetch failed ({baseUrl}): {ex.Message}"); }
        }
        return null;
    }

    private static List<RobloxPackage> ParseManifest(string text)
    {
        using var reader  = new StringReader(text);
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

            if (name == "RobloxPlayerLauncher.exe") break;

            long packed = long.TryParse(rawPacked, out var s) ? s : 0;
            result.Add(new RobloxPackage(name, packed, signature));
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // Version GUID
    // -------------------------------------------------------------------------
    private async Task<string?> GetLatestVersionGuidCachedAsync()
        => await _versionManifest.GetLatestVersionGuidCachedAsync();

    private void UpdateVersionCache(string guid)
        => _versionManifest.UpdateVersionCache(guid);

    // -------------------------------------------------------------------------
    // Protocol handler registration — roblox:// / roblox-player://
    // -------------------------------------------------------------------------
    /// <summary>
    /// 起動のたびに現在の EXE パスで roblox:// プロトコルを再登録する。
    /// Debug / Release / 移動後など、どのパスで起動しても Web 経由が機能するようにする。
    /// </summary>
    public static void RegisterProtocolHandler()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        var command = $"\"{exe}\" \"%1\"";
        var versionFolder = Path.GetFileName(
            Directory.Exists(VersionsDir)
                ? Directory.GetDirectories(VersionsDir).FirstOrDefault() ?? string.Empty
                : string.Empty);

        foreach (var scheme in new[] { "roblox", "roblox-player" })
        {
            try
            {
                using var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}");
                root.SetValue("", $"URL:{scheme} Protocol");
                root.SetValue("URL Protocol", "");

                using var icon = root.CreateSubKey("DefaultIcon");
                icon.SetValue("", exe);

                using var cmd = root.CreateSubKey(@"shell\open\command");
                cmd.SetValue("", command);
                if (!string.IsNullOrEmpty(versionFolder))
                    cmd.SetValue("version", versionFolder);
            }
            catch (Exception ex) { Log($"RegisterProtocolHandler({scheme}): {ex.Message}"); }
        }
    }

    // -------------------------------------------------------------------------
    // Setup
    // -------------------------------------------------------------------------
    public bool NeedsSetup() => !IsVcRedistInstalled();
    public void BroadcastProgress(BootstrapperProgress p) => BootstrapperProgress?.Invoke(this, p);
    public async Task RunSetupAsync() => await CheckAndInstallVcRedistAsync();

    // -------------------------------------------------------------------------
    // VC++ redistributable
    // -------------------------------------------------------------------------
    private static bool IsVcRedistInstalled()
    {
        // 複数の既知パスを確認（VS バージョン・WoW6432Node の違いに対応）
        var paths = new[]
        {
            @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64",
            @"SOFTWARE\WoW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X64",
            @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
        };
        foreach (var path in paths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key?.GetValue("Installed") is int v && v == 1) return true;
            }
            catch { }
        }
        // 追加チェック: アンインストールレジストリで VC++ 2015-2022 を探す
        try
        {
            foreach (var uninstallPath in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WoW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            })
            {
                using var key = Registry.LocalMachine.OpenSubKey(uninstallPath);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var entry = key.OpenSubKey(sub);
                    var name = entry?.GetValue("DisplayName") as string;
                    if (name != null && name.Contains("Microsoft Visual C++") &&
                        name.Contains("2015") || name != null && name.Contains("Redistributable") &&
                        name?.Contains("14.") == true)
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    private async Task CheckAndInstallVcRedistAsync()
    {
        if (IsVcRedistInstalled()) return;

        Log("VC++ 2015-2022 x64 not found, downloading...");
        ReportProgress("Downloading vc_redist.x64.exe", 0);

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
                ReportProgress($"Downloading vc_redist.x64.exe ({got / 1024:N0} KB)", pct,
                    detail: FormatSpeed(elapsed > 0.1 ? got / elapsed : 0));
            }
        }
        catch (Exception ex) { Log($"Failed to download VC++ redist: {ex.Message}"); return; }

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
        foreach (var proc in Process.GetProcessesByName("RobloxPlayerBeta"))
            try { proc.Kill(); await proc.WaitForExitAsync(); } catch { }

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

// Signature = MD5 hash (matches Bloxstrap Package.Signature)
internal record RobloxPackage(string Name, long CompressedSize, string Signature);
