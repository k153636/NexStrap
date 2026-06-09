using Microsoft.Extensions.DependencyInjection;
using NexStrap.Services;
using NexStrap.ViewModels;

namespace NexStrap.Modules.AccountsFriends;

public static class AccountsFriendsModule
{
    public static IServiceCollection AddAccountsFriendsModule(this IServiceCollection services)
    {
        services.AddSingleton<AccountService>();
        services.AddSingleton<AccountActivityRefreshService>();
        services.AddSingleton<AccountDialogCoordinator>();
        services.AddSingleton<AccountImportStatusCoordinator>();
        services.AddSingleton<AccountOperationCoordinator>();
        services.AddSingleton<AccountPanelStateCoordinator>();
        services.AddSingleton<ChromeImportCoordinator>();
        services.AddSingleton<CookieAccountImportService>();
        services.AddSingleton<CookieInputNormalizer>();
        services.AddSingleton<QuickLoginService>();
        services.AddSingleton<QuickLoginCoordinator>();
        services.AddSingleton<AccountQuickSignInStatusCoordinator>();
        services.AddSingleton<FriendNotificationService>();

        services.AddTransient<AccountViewModel>();
        services.AddTransient<FriendsViewModel>();
        services.AddSingleton<AccountEntryViewModelFactory>();
        services.AddSingleton<QuickSignInViewModelFactory>();

        return services;
    }
}
