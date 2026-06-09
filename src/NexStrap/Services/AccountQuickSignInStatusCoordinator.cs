using NexStrap.ViewModels;

namespace NexStrap.Services;

public sealed class AccountQuickSignInStatusCoordinator
{
    public void ShowSessionCreationFailed(AccountViewModel vm)
        => vm.StatusMessage = "Failed to create Quick Sign-In session";

    public void TogglePanel(AccountViewModel vm)
    {
        vm.IsQuickLoginOpen = !vm.IsQuickLoginOpen;
        if (!vm.IsQuickLoginOpen) vm.QuickLoginInput = string.Empty;
    }

    public void ShowInvalidCodeInput(AccountViewModel vm)
        => vm.StatusMessage = "Enter a valid 6-digit code";

    public void ShowInvalidOrExpiredCode(AccountViewModel vm)
        => vm.StatusMessage = "Code is invalid or expired";

    public void ShowFetchingAccountInfo(AccountViewModel vm)
        => vm.StatusMessage = "Fetching account info...";

    public void CompleteSwitched(AccountViewModel vm, string displayName)
    {
        vm.QuickLoginInput  = string.Empty;
        vm.IsQuickLoginOpen = false;
        vm.StatusMessage    = $"Switched to {displayName}";
    }

    public void CompleteAddedAndSwitched(AccountViewModel vm, string displayName)
    {
        vm.QuickLoginInput  = string.Empty;
        vm.IsQuickLoginOpen = false;
        vm.StatusMessage    = $"Added & switched to {displayName}";
    }
}
