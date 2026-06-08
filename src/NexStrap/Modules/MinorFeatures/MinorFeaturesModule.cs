using Microsoft.Extensions.DependencyInjection;
using NexStrap.Services;
using NexStrap.ViewModels;

namespace NexStrap.Modules.MinorFeatures;

public static class MinorFeaturesModule
{
    public static IServiceCollection AddMinorFeaturesModule(this IServiceCollection services)
    {
        services.AddSingleton<ModService>();

        services.AddTransient<ThemeViewModel>();
        services.AddTransient<StatsViewModel>();
        services.AddTransient<ModsViewModel>();
        services.AddTransient<StretchResolutionViewModel>();
        services.AddTransient<ShortcutsViewModel>();

        return services;
    }
}
