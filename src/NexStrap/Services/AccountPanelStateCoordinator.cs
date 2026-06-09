using NexStrap.ViewModels;

namespace NexStrap.Services;

public sealed class AccountPanelStateCoordinator
{
    public void ToggleAddMethodDropdown(AccountViewModel vm)
        => vm.IsAddMethodDropdownOpen = !vm.IsAddMethodDropdownOpen;

    public void ResetAddMethodPanels(AccountViewModel vm)
    {
        vm.IsAddMethodDropdownOpen = false;
        vm.IsPastePanelOpen        = false;
        vm.IsQuickLoginOpen        = false;
    }

    public void ShowPastePanel(AccountViewModel vm)
        => vm.IsPastePanelOpen = true;

    public void TogglePastePanel(AccountViewModel vm)
        => vm.IsPastePanelOpen = !vm.IsPastePanelOpen;

    public void CompleteManualImport(AccountViewModel vm)
    {
        vm.ManualCookie     = string.Empty;
        vm.IsPastePanelOpen = false;
    }
}
