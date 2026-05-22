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

        // --launch-game {placeId}: フラグ/Mod適用 → ゲーム起動 → 即終了
        var args = Environment.GetCommandLineArgs();
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

        // Show bootstrapper window when Roblox install/update starts
        var robloxService = Services.GetRequiredService<RobloxService>();
        BootstrapperWindow? bootstrapperWindow = null;
        robloxService.StatusChanged += (_, status) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (status == RobloxStatus.Updating && bootstrapperWindow == null)
                {
                    var vm = new BootstrapperViewModel(robloxService);
                    bootstrapperWindow = new BootstrapperWindow(vm);
                    bootstrapperWindow.Closed += (_, _) => bootstrapperWindow = null;
                    bootstrapperWindow.Show();
                }
                else if (status is RobloxStatus.Running or RobloxStatus.Idle or RobloxStatus.NotInstalled)
                {
                    bootstrapperWindow?.Close();
                    bootstrapperWindow = null;
                }
            });
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task HandleJumpLaunchAsync(long placeId)
    {
        try
        {
            var fastFlags = Services.GetRequiredService<FastFlagService>();
            var settings  = Services.GetRequiredService<SettingsService>();
            var mods      = Services.GetRequiredService<ModService>();

            if (settings.Settings.FpsUnlockEnabled)
            {
                fastFlags.Set("DFIntTaskSchedulerTargetFps", "9999");
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
        services.AddSingleton<RobloxLogWatcher>();
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
        services.AddSingleton<AccountService>();
        services.AddTransient<AccountViewModel>();
        services.AddTransient<FriendsViewModel>();
    }
}
