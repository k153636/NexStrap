using NexStrap.Models;

namespace NexStrap.Services;

public sealed class QuickLoginCoordinator(QuickLoginService quickLogin, RobloxApiService robloxApi)
{
    public QuickLoginData? Redeem(string code) => quickLogin.Redeem(code);

    public RobloxAccount? FindExistingAccount(IEnumerable<RobloxAccount> accounts, long userId)
        => accounts.FirstOrDefault(a => a.UserId == userId);

    public async Task<RobloxAccount> CreateAccountAsync(QuickLoginData data)
    {
        var avatarUrl = data.AvatarUrl ?? await robloxApi.GetUserAvatarHeadshotAsync(data.UserId);
        return new RobloxAccount
        {
            UserId      = data.UserId,
            Username    = data.Username,
            DisplayName = data.DisplayName,
            AvatarUrl   = avatarUrl,
        };
    }
}
