using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NexStrap.Core.Services;
using NexStrap.ViewModels;
using NexStrap.Views;

namespace NexStrap;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
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
        Services.GetRequiredService<RobloxLogWatcher>().SetBackgroundMode(background);
        Services.GetRequiredService<SmtcService>().SetBackgroundMode(background);
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

        services.AddTransient<ThemeViewModel>();
        services.AddTransient<StatsViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<FastFlagsViewModel>();
        services.AddTransient<ModsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<BrowserViewModel>();
    }
}
