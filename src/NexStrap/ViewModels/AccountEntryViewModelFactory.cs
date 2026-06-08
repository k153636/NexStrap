using NexStrap.Models;
using NexStrap.Services;

namespace NexStrap.ViewModels;

public sealed class AccountEntryViewModelFactory(QuickLoginService quickLogin)
{
    public AccountEntryViewModel Create(
        RobloxAccount account,
        int index,
        Action<AccountEntryViewModel> setActive,
        Action<AccountEntryViewModel> remove,
        Action<AccountEntryViewModel> launchAs)
        => new(account, index, setActive, remove, quickLogin, launchAs);
}
