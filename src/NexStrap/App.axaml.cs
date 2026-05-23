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
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        JumpListService.Initialize();

        var args = Environment.GetCommandLineArgs();

        // roblox:// / roblox-player:// protocol launch from browser
        var robloxUrl = args.Skip(1).FirstOrDefault(a =>
            a.StartsWith("roblox://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("roblox-player://", StringComparison.OrdinalIgnoreCase));
        if (robloxUrl != null)
        {
            HandleRobloxUrlLaunchAsync(robloxUrl).GetAwaiter().GetResult();
            Environment.Exit(0);
            return;
        }

        // --launch-game {placeId}: フラグ/Mod適用 → ゲーム起動 → 即終了
        var idx  = Array.IndexOf(args, "--launch-game");
        if (idx >= 0 && idx + 1 < args.Length && long.TryParse(args[idx + 1], out var placeId))
        {
            HandleJumpLaunchAsync(placeId).GetAwaiter().GetResult();
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
            _ = RunStartupSequenceAsync(robloxService, settingsService, desktop);
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
    }

    private static void ApplyPerformanceFlags(FastFlagService fastFlags, SettingsService settings)
    {
        if (settings.Settings.FpsUnlockEnabled)
        {
            var fps = settings.Settings.TargetFps > 0 ? settings.Settings.TargetFps.ToString() : "9999";
            fastFlags.Set("DFIntTaskSchedulerTargetFps", fps);
            fastFlags.Set("FFlagTaskSchedulerLimitTargetFpsTo2402", "False");
        }
        else
        {
            fastFlags.Remove("DFIntTaskSchedulerTargetFps");
            fastFlags.Remove("FFlagTaskSchedulerLimitTargetFpsTo2402");
        }

        if (settings.Settings.MultiThreadingEnabled)
        {
            fastFlags.Set("FIntRuntimeMaxNumOfThreads", "2400");
            fastFlags.Set("DFIntTaskSchedulerThreadCount", Environment.ProcessorCount.ToString());
        }
        else
        {
            fastFlags.Remove("FIntRuntimeMaxNumOfThreads");
            fastFlags.Remove("DFIntTaskSchedulerThreadCount");
        }
    }

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

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = $"roblox://experiences/start?placeId={placeId}",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private async Task HandleRobloxUrlLaunchAsync(string url)
    {
        try
        {
            var fastFlags = Services.GetRequiredService<FastFlagService>();
            var settings  = Services.GetRequiredService<SettingsService>();
            var mods      = Services.GetRequiredService<ModService>();
            var roblox    = Services.GetRequiredService<RobloxService>();

            ApplyPerformanceFlags(fastFlags, settings);
            await fastFlags.SaveAsync();
            await mods.ApplyEnabledModsAsync();

            var playerPath = roblox.RobloxPlayerPath;
            if (playerPath != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(playerPath)
                {
                    UseShellExecute  = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(playerPath)!,
                    Arguments        = url
                });
            }
            else
            {
                // Roblox not installed via NexStrap, pass URL to shell
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            }
        }
        catch { }
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
    }
}
