using Microsoft.Extensions.DependencyInjection;
using NexStrap.Modules.AccountsFriends;
using NexStrap.Modules.Discord;
using NexStrap.Modules.FastFlags;
using NexStrap.Modules.MinorFeatures;
using NexStrap.Modules.Roblox;
using NexStrap.Modules.Shell;
using NexStrap.Modules.Studio;

namespace NexStrap.Modules.Infrastructure.DependencyInjection;

public static class NexStrapServiceRegistration
{
    public static IServiceCollection AddNexStrapServices(this IServiceCollection services)
    {
        services
            .AddShellModule()
            .AddRobloxModule()
            .AddStudioModule()
            .AddDiscordModule()
            .AddFastFlagsModule()
            .AddAccountsFriendsModule()
            .AddMinorFeaturesModule();

        return services;
    }
}
