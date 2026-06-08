using Microsoft.Extensions.DependencyInjection;
using NexStrap.Services;
using NexStrap.ViewModels;

namespace NexStrap.Modules.FastFlags;

public static class FastFlagsModule
{
    public static IServiceCollection AddFastFlagsModule(this IServiceCollection services)
    {
        services.AddSingleton<FastFlagService>();
        services.AddSingleton<ProfileService>();
        services.AddTransient<FastFlagsViewModel>();

        return services;
    }
}
