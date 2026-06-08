using NexStrap.Services;

namespace NexStrap.ViewModels;

public sealed class QuickSignInViewModelFactory(RobloxApiService robloxApi)
{
    public QuickSignInViewModel Create(string code, string privateKey)
        => new(code, privateKey, robloxApi);
}
