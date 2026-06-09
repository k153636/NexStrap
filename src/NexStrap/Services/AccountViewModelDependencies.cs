using NexStrap.ViewModels;

namespace NexStrap.Services;

public sealed class AccountViewModelDependencies(
    AccountService accounts,
    RobloxApiService robloxApi,
    QuickLoginCoordinator quickLoginCoordinator,
    CookieAccountImportService cookieImport,
    AccountActivityRefreshService activityRefresh,
    ChromeImportCoordinator chromeImport,
    AccountEntryViewModelFactory accountEntryFactory,
    AccountQuickSignInStatusCoordinator quickSignInStatus,
    AccountDialogCoordinator dialogCoordinator,
    AccountImportStatusCoordinator importStatus,
    AccountOperationCoordinator accountOperations,
    AccountPanelStateCoordinator panelState,
    CookieInputNormalizer cookieInputNormalizer)
{
    public AccountService Accounts { get; } = accounts;
    public RobloxApiService RobloxApi { get; } = robloxApi;
    public QuickLoginCoordinator QuickLoginCoordinator { get; } = quickLoginCoordinator;
    public CookieAccountImportService CookieImport { get; } = cookieImport;
    public AccountActivityRefreshService ActivityRefresh { get; } = activityRefresh;
    public ChromeImportCoordinator ChromeImport { get; } = chromeImport;
    public AccountEntryViewModelFactory AccountEntryFactory { get; } = accountEntryFactory;
    public AccountQuickSignInStatusCoordinator QuickSignInStatus { get; } = quickSignInStatus;
    public AccountDialogCoordinator DialogCoordinator { get; } = dialogCoordinator;
    public AccountImportStatusCoordinator ImportStatus { get; } = importStatus;
    public AccountOperationCoordinator AccountOperations { get; } = accountOperations;
    public AccountPanelStateCoordinator PanelState { get; } = panelState;
    public CookieInputNormalizer CookieInputNormalizer { get; } = cookieInputNormalizer;
}
