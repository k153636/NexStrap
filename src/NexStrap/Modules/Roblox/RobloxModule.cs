using Microsoft.Extensions.DependencyInjection;
using NexStrap.Modules.Roblox.Protocol;
using NexStrap.Services;

namespace NexStrap.Modules.Roblox;

public static class RobloxModule
{
    public static IServiceCollection AddRobloxModule(this IServiceCollection services)
    {
        services.AddSingleton<RobloxVersionManifestService>();
        services.AddSingleton<RobloxPackageInstallerService>();
        services.AddSingleton<RobloxService>();
        services.AddSingleton<RobloxProtocolLaunchHandler>();
        services.AddSingleton<RobloxApiService>();
        services.AddSingleton<RobloxLogWatcher>(sp =>
            new RobloxLogWatcher(sp.GetRequiredService<RobloxService>().IsNexStrapRobloxRunning));

        return services;
    }
}
