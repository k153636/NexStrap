using Microsoft.Extensions.DependencyInjection;
using NexStrap.Modules.Infrastructure.Startup;
using NexStrap.Services;
using NexStrap.ViewModels;

namespace NexStrap.Modules.Shell;

public static class ShellModule
{
    public static IServiceCollection AddShellModule(this IServiceCollection services)
    {
        services.AddSingleton<EnvService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<PerformanceMonitorService>();
        services.AddSingleton<GameHistoryService>();
        services.AddSingleton<GlobalHotKeyService>();
        services.AddSingleton<StartupCoordinator>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<LaunchWindowViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<DevViewModel>();

        return services;
    }
}
