using Microsoft.Extensions.DependencyInjection;
using NexStrap.Services;

namespace NexStrap.Modules.Studio;

public static class StudioModule
{
    public static IServiceCollection AddStudioModule(this IServiceCollection services)
    {
        services.AddSingleton<StudioService>();
        services.AddSingleton<StudioFastFlagService>();
        services.AddSingleton<StudioRpcServer>();

        return services;
    }
}
