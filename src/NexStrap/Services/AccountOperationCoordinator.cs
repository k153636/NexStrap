using NexStrap.ViewModels;

namespace NexStrap.Services;

public sealed class AccountOperationCoordinator(AccountService accounts)
{
    public void SetActive(AccountEntryViewModel entry)
        => accounts.SetActive(entry.Id);

    public void Remove(AccountEntryViewModel entry)
        => accounts.Remove(entry.Id);

    public void LaunchAs(AccountEntryViewModel entry)
        => accounts.SetActive(entry.Id);
}
