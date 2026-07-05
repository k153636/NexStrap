using NexStrap.ViewModels;

namespace NexStrap.Services;

public sealed class AccountImportStatusCoordinator
{
    public bool TryBeginImport(AccountViewModel vm)
    {
        if (vm.IsImporting) return false;
        vm.IsImporting = true;
        return true;
    }

    public void EndImport(AccountViewModel vm)
    {
        vm.IsImporting       = false;
        vm.IsWaitingForLogin = false;
    }

    public void ShowChromeNotFound(AccountViewModel vm)
        => vm.StatusMessage = "Chrome not found";

    public void ShowImporting(AccountViewModel vm)
        => vm.StatusMessage = "Importing...";

    public void ShowWaitingForChromeLogin(AccountViewModel vm)
    {
        vm.IsWaitingForLogin = true;
        vm.StatusMessage     = "Please log in to roblox.com in Chrome";
    }

    public void ShowChromeSecureLoginFallback(AccountViewModel vm)
        => vm.StatusMessage = "Chrome cookie is protected — continue in the secure login window";

    public void ShowAccountAlreadyAdded(AccountViewModel vm)
        => vm.StatusMessage = "Account already added";

    public void ShowFetchingAccountInfo(AccountViewModel vm)
        => vm.StatusMessage = "Fetching account info...";

    public void ShowAdded(AccountViewModel vm, string displayName)
        => vm.StatusMessage = $"Added {displayName}";

    public void ClearStatus(AccountViewModel vm)
        => vm.StatusMessage = "";

    public void ShowBrowserUnavailable(AccountViewModel vm)
        => vm.StatusMessage = "WebView2 not available";

    public void ShowValidating(AccountViewModel vm)
        => vm.StatusMessage = "Validating...";

    public void ShowInvalidCookie(AccountViewModel vm)
        => vm.StatusMessage = "Invalid or expired cookie";

    public void ShowMissingManualCookie(AccountViewModel vm)
        => vm.StatusMessage = "Paste your .ROBLOSECURITY cookie first";
}
