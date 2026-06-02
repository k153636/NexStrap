using Avalonia;
using Avalonia.Fonts.Inter;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.Win32;
using Microsoft.Win32;

namespace NexStrap;

class Program
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "crash.log");

    [STAThread]
    public static void Main(string[] args)
    {
        // 起動のたびにプロトコルハンドラを現在の EXE パスで更新
        NexStrap.Core.Services.RobloxService.RegisterProtocolHandler();

        // ── プロトコルハンドラ / ジャンプリスト ──────────────────────────────
        // Avalonia 初期化より前に処理する。
        // Avalonia のレンダリング初期化が完了する前に URL 処理が必要なため、
        // また単一インスタンス mutex の影響を受けないようにするため。
        // ブラウザからは "roblox-player:1+key:value+..." 形式で届く（スラッシュなし）
        var robloxUrl = args.FirstOrDefault(a =>
            a.StartsWith("roblox://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase));

        if (robloxUrl != null)
        {
            HandleProtocolUrl(robloxUrl);
            return;
        }

        var jumpIdx = Array.IndexOf(args, "--launch-game");
        if (jumpIdx >= 0 && jumpIdx + 1 < args.Length && long.TryParse(args[jumpIdx + 1], out var jumpPlaceId))
        {
            HandleJumpGame(jumpPlaceId);
            return;
        }

        // ── 通常起動（メインウィンドウ） ─────────────────────────────────────
        RequestHighPerformanceGpu();

        using var mutex = new Mutex(true, "NexStrap_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // 既に起動中 → 既存ウィンドウを前面に表示してから終了
            BringExistingWindowToFront();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// roblox:// / roblox-player: URI を処理してゲームに参加する（Avalonia 不使用）。
    /// 通常起動と同じ処理: FastFlags/Mods 適用 → Cookie 注入 → Bloxstrap 方式で起動
    ///                      → PostLaunch（CPU/Memory/CrashHandler）を適用してから終了。
    /// </summary>
    private static void HandleProtocolUrl(string url)
    {
        NexStrap.Core.Services.RobloxService.Log($"HandleProtocolUrl: {url[..Math.Min(120, url.Length)]}");
        try
        {
            var settings  = new NexStrap.Core.Services.SettingsService();
            var roblox    = new NexStrap.Core.Services.RobloxService();
            var fastFlags = new NexStrap.Core.Services.FastFlagService(roblox);
            var mods      = new NexStrap.Core.Services.ModService(roblox);
            var accounts  = new NexStrap.Core.Services.AccountService();
            var s         = settings.Settings;

            // 1. FastFlags / Mods 適用（通常起動と同一）
            fastFlags.ApplyPerformanceSettings(s);
            fastFlags.SaveAsync().GetAwaiter().GetResult();
            mods.ApplyEnabledModsAsync().GetAwaiter().GetResult();

            // 2. アクティブアカウントのクッキーを注入（通常起動と同一）
            var cookie = accounts.GetActiveCookie();
            if (cookie != null)
            {
                NexStrap.Core.Services.RobloxService.InjectAccountCookie(cookie);
                NexStrap.Core.Services.RobloxService.Log("Cookie injected for web launch");
            }

            var playerPath = roblox.RobloxPlayerPath;
            if (playerPath == null) { NexStrap.Core.Services.RobloxService.Log("Player not found"); return; }

            // 3. Bloxstrap 方式: URI をそのまま RobloxPlayerBeta.exe に渡す
            NexStrap.Core.Services.RobloxService.Log($"Launching: {playerPath}");
            var psi = new System.Diagnostics.ProcessStartInfo(playerPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(playerPath)!,
                Arguments = url
            };
            var proc = System.Diagnostics.Process.Start(psi);

            // 4. 通常起動と同じ PostLaunch 処理（CPU/メモリ/CrashHandler）
            if (proc != null)
            {
                var opts = new NexStrap.Core.Services.LaunchOptions(
                    SuppressCrashHandler: s.SuppressCrashHandler,
                    CpuCoreLimit:         s.CpuAffinityEnabled ? s.CpuCoreLimit : 0,
                    MemoryOptimization:   s.MemoryOptimizationEnabled
                );
                Task.Run(() => roblox.PostLaunchAsync(proc, opts)).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex) { NexStrap.Core.Services.RobloxService.Log($"HandleProtocolUrl failed: {ex.Message}"); }
    }

    /// <summary>ジャンプリスト経由のゲーム起動（Avalonia 不使用）。</summary>
    private static void HandleJumpGame(long placeId)
    {
        NexStrap.Core.Services.RobloxService.Log($"HandleJumpGame: placeId={placeId}");
        try
        {
            var settings  = new NexStrap.Core.Services.SettingsService();
            var roblox    = new NexStrap.Core.Services.RobloxService();
            var fastFlags = new NexStrap.Core.Services.FastFlagService(roblox);
            var mods      = new NexStrap.Core.Services.ModService(roblox);
            var robloxApi = new NexStrap.Core.Services.RobloxApiService();
            var accounts  = new NexStrap.Core.Services.AccountService();
            var s         = settings.Settings;

            fastFlags.ApplyPerformanceSettings(s);
            fastFlags.SaveAsync().GetAwaiter().GetResult();
            mods.ApplyEnabledModsAsync().GetAwaiter().GetResult();

            var cookie = accounts.GetActiveCookie();
            if (cookie != null) NexStrap.Core.Services.RobloxService.InjectAccountCookie(cookie);

            var proc = BloxstrapLaunch(roblox, robloxApi, accounts, placeId);
            if (proc != null)
            {
                var opts = new NexStrap.Core.Services.LaunchOptions(
                    SuppressCrashHandler: s.SuppressCrashHandler,
                    CpuCoreLimit:         s.CpuAffinityEnabled ? s.CpuCoreLimit : 0,
                    MemoryOptimization:   s.MemoryOptimizationEnabled
                );
                Task.Run(() => roblox.PostLaunchAsync(proc, opts)).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex) { NexStrap.Core.Services.RobloxService.Log($"HandleJumpGame failed: {ex.Message}"); }
    }

    private static System.Diagnostics.Process? BloxstrapLaunch(
        NexStrap.Core.Services.RobloxService roblox,
        NexStrap.Core.Services.RobloxApiService robloxApi,
        NexStrap.Core.Services.AccountService accounts,
        long placeId, string? gameId = null, string? accessCode = null)
    {
        var playerPath = roblox.RobloxPlayerPath;
        if (playerPath == null) { NexStrap.Core.Services.RobloxService.Log("Player not found"); return null; }
        var workDir = Path.GetDirectoryName(playerPath)!;

        var cookie = accounts.GetActiveCookie();
        if (cookie != null)
        {
            var (joinScriptUrl, authTicket) =
                robloxApi.GetJoinInfoAsync(cookie, placeId, gameId, accessCode).GetAwaiter().GetResult();

            if (!string.IsNullOrEmpty(joinScriptUrl) && !string.IsNullOrEmpty(authTicket))
            {
                var jArgs = $"--joinscript \"{joinScriptUrl}\" " +
                            $"--authenticationTicket {authTicket} " +
                            $"--authenticationUrl \"https://auth.roblox.com\" " +
                            $"--joinAttemptId {Guid.NewGuid()} " +
                            $"--joinAttemptOrigin ExperiencesListAndGrid " +
                            $"--launchMode play";
                NexStrap.Core.Services.RobloxService.Log($"Bloxstrap launch OK: placeId={placeId}");
                return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(playerPath)
                    { UseShellExecute = false, WorkingDirectory = workDir, Arguments = jArgs });
            }

            var ticket = robloxApi.GetAuthTicketAsync(cookie).GetAwaiter().GetResult();
            if (ticket != null)
            {
                var fbArgs = $"--launchMode play --placeId {placeId} " +
                             $"--authenticationTicket {ticket} --authenticationUrl https://auth.roblox.com";
                NexStrap.Core.Services.RobloxService.Log($"Fallback launch: placeId={placeId}");
                return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(playerPath)
                    { UseShellExecute = false, WorkingDirectory = workDir, Arguments = fbArgs });
            }
        }

        NexStrap.Core.Services.RobloxService.Log($"No-auth launch: placeId={placeId}");
        return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(playerPath)
            { UseShellExecute = false, WorkingDirectory = workDir,
              Arguments = $"--launchMode play --placeId {placeId}" });
    }

    private static (long PlaceId, string? GameId, string? AccessCode) ParseRobloxUrl(string url)
    {
        long placeId = 0; string? gameId = null, accessCode = null;
        try
        {
            var q = url.Contains('?') ? url[(url.IndexOf('?') + 1)..] : string.Empty;
            foreach (var kv in q.Split('&'))
            {
                var p = kv.Split('=', 2);
                if (p.Length != 2) continue;
                var val = Uri.UnescapeDataString(p[1]);
                switch (p[0].ToLowerInvariant())
                {
                    case "placeid":    long.TryParse(val, out placeId); break;
                    case "gameid":     gameId     = val; break;
                    case "accesscode": accessCode = val; break;
                }
            }
        }
        catch { }
        return (placeId, gameId, accessCode);
    }

    private static void RequestHighPerformanceGpu()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            // Windows Settings > グラフィック > アプリの設定 > 高パフォーマンス と同等
            // GpuPreference=2 は DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\DirectX\UserGpuPreferences");
            key.SetValue(exePath, "GpuPreference=2;", RegistryValueKind.String);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    private static void BringExistingWindowToFront()
    {
        try
        {
            var existing = System.Diagnostics.Process.GetProcessesByName("NexStrap")
                .Where(p => p.Id != Environment.ProcessId && !p.HasExited)
                .FirstOrDefault();
            if (existing == null) return;
            var hwnd = existing.MainWindowHandle;
            if (hwnd == IntPtr.Zero) return;
            ShowWindow(hwnd, 9); // SW_RESTORE
            SetForegroundWindow(hwnd);
        }
        catch { }
    }

    private static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex}\n\n";
            File.AppendAllText(LogPath, msg);
        }
        catch { }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                // ANGLE EGL → Direct3D 11 (GPU), WGL → OpenGL (GPU), Software fallback
                RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Wgl, Win32RenderingMode.Software],
            })
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = 256 * 1024 * 1024, // 256 MB GPU キャッシュ
            })
            .WithInterFont()
            .LogToTrace();
}
