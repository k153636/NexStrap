using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NexStrap.Core.Services;
using NexStrap.Services;
using NexStrap.ViewModels;
using NexStrap.Views;

namespace NexStrap;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private bool _isBackground;
    private bool _isPlaying;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();
        }
        catch (Exception ex)
        {
            RobloxService.Log($"DI initialization failed: {ex}");
            // サービス初期化に失敗した場合は即座にクラッシュログを残して終了
            var msg = $"NexStrap failed to initialize:\n\n{ex.Message}\n\nCheck %LOCALAPPDATA%\\NexStrap\\crash.log";
            NativeMessageBox(msg, "NexStrap Error");
            Environment.Exit(1);
            return;
        }

        try { JumpListService.Initialize(); } catch { }

        // 起動のたびに現在の EXE パスでプロトコルを更新（Debug/Release/移動後も Web 経由が機能する）
        RobloxService.RegisterProtocolHandler();

        var args = Environment.GetCommandLineArgs();
        RobloxService.Log($"Args: {string.Join(" | ", args)}");

        // roblox:// / roblox-player:// protocol launch from browser
        var robloxUrl = args.Skip(1).FirstOrDefault(a =>
            a.StartsWith("roblox://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("roblox-player://", StringComparison.OrdinalIgnoreCase));
        if (robloxUrl != null)
        {
            RobloxService.Log($"URL detected: {robloxUrl}");
            // Task.Run でスレッドプール上で実行し Avalonia UI スレッドのデッドロックを回避
            Task.Run(() => HandleRobloxUrlLaunchAsync(robloxUrl)).GetAwaiter().GetResult();
            Environment.Exit(0);
            return;
        }

        // --launch-game {placeId}: フラグ/Mod適用 → ゲーム起動 → 即終了
        var idx  = Array.IndexOf(args, "--launch-game");
        if (idx >= 0 && idx + 1 < args.Length && long.TryParse(args[idx + 1], out var placeId))
        {
            Task.Run(() => HandleJumpLaunchAsync(placeId)).GetAwaiter().GetResult();
            Environment.Exit(0);
            return;
        }

        // Wire friend online → toast notification
        var friendNotif = Services.GetRequiredService<FriendNotificationService>();
        friendNotif.FriendCameOnline += (_, e) => NotificationService.ShowFriendOnline(e.DisplayName);

        // Start media detection
        _ = Services.GetRequiredService<SmtcService>().StartAsync();

        // Show bootstrapper window when Roblox install/update starts
        var robloxService   = Services.GetRequiredService<RobloxService>();
        var settingsService = Services.GetRequiredService<SettingsService>();
        BootstrapperWindow? bootstrapperWindow = null;
        BootstrapperViewModel? bootstrapperViewModel = null;

        robloxService.StatusChanged += (_, status) =>
        {
            void HandleStatus()
            {
                if (status is RobloxStatus.Updating or RobloxStatus.Launching && bootstrapperWindow == null)
                {
                    bootstrapperViewModel = new BootstrapperViewModel(robloxService, settingsService);
                    bootstrapperWindow = new BootstrapperWindow(bootstrapperViewModel);
                    bootstrapperWindow.Closed += (_, _) =>
                    {
                        bootstrapperWindow = null;
                        bootstrapperViewModel = null;
                    };
                    bootstrapperWindow.Show();
                }
                else if (status is RobloxStatus.Running or RobloxStatus.Idle or RobloxStatus.NotInstalled)
                {
                    bootstrapperWindow?.Close();
                }
            }

            // If already on the UI thread (typical: called from RelayCommand), run synchronously
            // so the window opens BEFORE Process.Start. Otherwise post to the dispatcher.
            if (Dispatcher.UIThread.CheckAccess())
                HandleStatus();
            else
                Dispatcher.UIThread.InvokeAsync(HandleStatus);
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Keep the app alive during the startup sequence; main window is shown at the end
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunStartupSequenceAsync(robloxService, settingsService, desktop)
                .ContinueWith(t =>
                {
                    if (!t.IsFaulted) return;
                    var ex = t.Exception?.InnerException ?? t.Exception;
                    Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var msg = $"NexStrap failed to start:\n\n{ex?.Message}\n\n" +
                                  $"Check %LOCALAPPDATA%\\NexStrap\\crash.log for details.";
                        var box = new Avalonia.Controls.Window
                        {
                            Title  = "NexStrap - Startup Error",
                            Width  = 480, Height = 200,
                            CanResize = false,
                            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen,
                            Content = new Avalonia.Controls.TextBlock
                            {
                                Text = msg, Margin = new Avalonia.Thickness(20),
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            }
                        };
                        box.Show();
                        RobloxService.Log($"Startup failed: {ex}");
                        await Task.Delay(8000);
                        box.Close();
                        desktop.Shutdown(1);
                    });
                });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunStartupSequenceAsync(
        RobloxService roblox, SettingsService settings,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Step 1: first-time environment setup (VC++ etc.)
        if (roblox.NeedsSetup())
        {
            BootstrapperWindow? win = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var vm = new BootstrapperViewModel(roblox, settings);
                win = new BootstrapperWindow(vm);
                win.Show();
            });
            await roblox.RunSetupAsync();
            await Dispatcher.UIThread.InvokeAsync(() => win?.Close());
        }

        // Step 2: check for NexStrap self-update
        var updateService = new UpdateService();
        var update        = await updateService.CheckForUpdateAsync();
        if (update != null)
        {
            BootstrapperWindow? win = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var vm = new BootstrapperViewModel(roblox, settings);
                win = new BootstrapperWindow(vm);
                win.Show();
            });
            await updateService.DownloadAndApplyAsync(
                update.Value.DownloadUrl,
                p => roblox.BroadcastProgress(p));
            return; // Environment.Exit(0) called inside DownloadAndApplyAsync
        }

        // Step 3: all done — create and show main window, restore normal shutdown mode
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
            desktop.MainWindow   = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
            mainWindow.Activate();
        });
        RobloxService.Log("Main window shown");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
    private static void NativeMessageBox(string text, string caption) => MessageBox(IntPtr.Zero, text, caption, 0x10);

    private static void ApplyPerformanceFlags(FastFlagService fastFlags, SettingsService settings)
        => fastFlags.ApplyPerformanceSettings(settings.Settings);

    private async Task HandleJumpLaunchAsync(long placeId)
    {
        try
        {
            var fastFlags = Services.GetRequiredService<FastFlagService>();
            var settings  = Services.GetRequiredService<SettingsService>();
            var mods      = Services.GetRequiredService<ModService>();

            ApplyPerformanceFlags(fastFlags, settings);
            await fastFlags.SaveAsync();
            await mods.ApplyEnabledModsAsync();

            await BloxstrapLaunchAsync(placeId);
        }
        catch (Exception ex) { RobloxService.Log($"HandleJumpLaunchAsync: {ex.Message}"); }
    }

    private async Task HandleRobloxUrlLaunchAsync(string url)
    {
        try
        {
            var fastFlags = Services.GetRequiredService<FastFlagService>();
            var settings  = Services.GetRequiredService<SettingsService>();
            var mods      = Services.GetRequiredService<ModService>();

            RobloxService.Log($"Web launch: {url}");

            ApplyPerformanceFlags(fastFlags, settings);
            await fastFlags.SaveAsync();
            await mods.ApplyEnabledModsAsync();

            var (placeId, gameId, accessCode) = ParseRobloxUrl(url);
            if (placeId > 0)
                await BloxstrapLaunchAsync(placeId, gameId, accessCode);
            else
                RobloxService.Log($"Could not extract placeId from: {url}");
        }
        catch (Exception ex) { RobloxService.Log($"HandleRobloxUrlLaunchAsync failed: {ex.Message}"); }
    }

    /// <summary>
    /// Bloxstrap 互換の起動フロー:
    /// 1. gamejoin.roblox.com/v1/join-* で joinScriptUrl + authTicket を取得
    /// 2. RobloxPlayerBeta に --joinscript で渡す
    /// join API 失敗時は --launchMode play --placeId にフォールバック
    /// </summary>
    private async Task BloxstrapLaunchAsync(long placeId, string? gameId = null, string? accessCode = null)
    {
        var roblox    = Services.GetRequiredService<RobloxService>();
        var robloxApi = Services.GetRequiredService<RobloxApiService>();
        var accounts  = Services.GetRequiredService<AccountService>();

        var playerPath = roblox.RobloxPlayerPath;
        if (playerPath == null) { RobloxService.Log("Player not found"); return; }
        var workDir = System.IO.Path.GetDirectoryName(playerPath)!;

        var cookie = accounts.GetActiveCookie();

        // ── Bloxstrap 方式: join API → --joinscript ──────────────────────────
        if (cookie != null)
        {
            var (joinScriptUrl, authTicket) =
                await robloxApi.GetJoinInfoAsync(cookie, placeId, gameId, accessCode);

            if (!string.IsNullOrEmpty(joinScriptUrl) && !string.IsNullOrEmpty(authTicket))
            {
                var jArgs = $"--joinscript \"{joinScriptUrl}\" " +
                            $"--authenticationTicket {authTicket} " +
                            $"--authenticationUrl \"https://auth.roblox.com\" " +
                            $"--joinAttemptId {Guid.NewGuid()} " +
                            $"--joinAttemptOrigin ExperiencesListAndGrid " +
                            $"--launchMode play";
                RobloxService.Log($"Bloxstrap launch: placeId={placeId} gameId={gameId}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(playerPath)
                    { UseShellExecute = false, WorkingDirectory = workDir, Arguments = jArgs });
                return;
            }
        }

        // ── フォールバック: --launchMode play --placeId ───────────────────────
        // アカウント未設定 or join API 失敗 → Roblox 自身のセッションで参加を試みる
        string fbArgs;
        if (cookie != null)
        {
            RobloxService.Log($"join API failed, fallback with auth: placeId={placeId}");
            var ticket = await robloxApi.GetAuthTicketAsync(cookie);
            fbArgs = ticket != null
                ? $"--launchMode play --placeId {placeId} --authenticationTicket {ticket} --authenticationUrl https://auth.roblox.com"
                : $"--launchMode play --placeId {placeId}";
        }
        else
        {
            RobloxService.Log($"No account, launching without auth: placeId={placeId}");
            fbArgs = $"--launchMode play --placeId {placeId}";
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(playerPath)
            { UseShellExecute = false, WorkingDirectory = workDir, Arguments = fbArgs });
    }

    /// <summary>roblox:// URL から (placeId, gameId, accessCode) を抽出する。</summary>
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

    private void OnTrayIconClicked(object? sender, EventArgs e) => ShowMainWindow();
    private void OnTrayShowClicked(object? sender, EventArgs e) => ShowMainWindow();
    private void OnTrayExitClicked(object? sender, EventArgs e)
        => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    private void ShowMainWindow()
    {
        var window = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window == null) return;
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
        SetBackgroundMode(false);
    }

    internal void SetBackgroundMode(bool background)
    {
        _isBackground = background;
        ApplyServiceModes();
    }

    internal void SetPlayingMode(bool playing)
    {
        _isPlaying = playing;
        ApplyServiceModes();
    }

    private void ApplyServiceModes()
    {
        Services.GetRequiredService<RobloxLogWatcher>().SetBackgroundMode(_isBackground, _isPlaying);
        Services.GetRequiredService<SmtcService>().SetBackgroundMode(_isBackground, _isPlaying);
        Services.GetRequiredService<FriendNotificationService>().SetBackgroundMode(_isBackground, _isPlaying);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<EnvService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<RobloxService>();
        services.AddSingleton<FastFlagService>();
        services.AddSingleton<ModService>();
        services.AddSingleton<DiscordRpcService>();
        services.AddSingleton<ProfileService>();
        services.AddSingleton<RobloxLogWatcher>(sp => new RobloxLogWatcher(sp.GetRequiredService<RobloxService>().IsNexStrapRobloxRunning));
        services.AddSingleton<RobloxApiService>();
        services.AddSingleton<PerformanceMonitorService>();
        services.AddSingleton<SmtcService>();
        services.AddSingleton<GameHistoryService>();
        services.AddSingleton<FriendNotificationService>();

        services.AddTransient<ThemeViewModel>();
        services.AddTransient<StatsViewModel>();
        services.AddTransient<DevViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<FastFlagsViewModel>();
        services.AddTransient<ModsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<DiscordViewModel>();
        services.AddTransient<BrowserViewModel>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<AccountService>();
        services.AddTransient<AccountViewModel>();
        services.AddTransient<FriendsViewModel>();
        services.AddTransient<StretchResolutionViewModel>();
    }
}
