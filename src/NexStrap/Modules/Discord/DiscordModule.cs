using Microsoft.Extensions.DependencyInjection;
using NexStrap.Services;
using NexStrap.ViewModels;

namespace NexStrap.Modules.Discord;

public static class DiscordModule
{
    public static IServiceCollection AddDiscordModule(this IServiceCollection services)
    {
        services.AddSingleton<DiscordRichPresence>();
        services.AddTransient<DiscordViewModel>();

        return services;
    }
}
