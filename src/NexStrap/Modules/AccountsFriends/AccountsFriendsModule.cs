using Microsoft.Extensions.DependencyInjection;
using NexStrap.Services;
using NexStrap.ViewModels;

namespace NexStrap.Modules.AccountsFriends;

public static class AccountsFriendsModule
{
    public static IServiceCollection AddAccountsFriendsModule(this IServiceCollection services)
    {
        services.AddSingleton<AccountService>();
        services.AddSingleton<QuickLoginService>();
        services.AddSingleton<FriendNotificationService>();

        services.AddTransient<AccountViewModel>();
        services.AddTransient<FriendsViewModel>();

        return services;
    }
}
