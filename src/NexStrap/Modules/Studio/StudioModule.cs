using Microsoft.Extensions.DependencyInjection;
using NexStrap.Services;

namespace NexStrap.Modules.Studio;

public static class StudioModule
{
    public static IServiceCollection AddStudioModule(this IServiceCollection services)
    {
        services.AddSingleton<StudioAppSettingsService>();
        services.AddSingleton<StudioVersionCleanupService>();
        services.AddSingleton<StudioVersionManifestService>();
        services.AddSingleton<StudioPackageManifestService>();
        services.AddSingleton<StudioPackageInstallerService>();
        services.AddSingleton<StudioCdnConnectivityService>();
        services.AddSingleton<StudioService>();
        services.AddSingleton<StudioFastFlagService>();
        services.AddSingleton<StudioRpcServer>();

        return services;
    }
}
