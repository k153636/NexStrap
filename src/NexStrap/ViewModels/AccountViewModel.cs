using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Models;
using NexStrap.Services;
using System.Collections.ObjectModel;

namespace NexStrap.ViewModels;

public partial class AccountViewModel : ViewModelBase
{
    private readonly AccountService    _accounts;
    private readonly AccountActivityRefreshService _activityRefresh;
    private readonly AccountDialogCoordinator _dialogCoordinator;
    private readonly ChromeImportCoordinator _chromeImport;
    private readonly RobloxApiService  _robloxApi;
    private readonly CookieAccountImportService _cookieImport;
    private readonly QuickLoginCoordinator _quickLoginCoordinator;
    private readonly AccountEntryViewModelFactory _accountEntryFactory;
    private CancellationTokenSource?   _pollCts;
    private CancellationTokenSource    _presenceCts = new();

    public FriendsViewModel FriendsVm { get; }

    public event Action? LaunchAsRequested;

    public ObservableCollection<AccountEntryViewModel> Accounts { get; } = [];

    // タブ
    [ObservableProperty] private bool _isAccountsTab = true;
    public bool IsFriendsTab => !IsAccountsTab;
    partial void OnIsAccountsTabChanged(bool _) => OnPropertyChanged(nameof(IsFriendsTab));

    [RelayCommand]
    private void SwitchTab(string tab) => IsAccountsTab = tab == "Accounts";

    // ドロップダウン
    [ObservableProperty] private bool _isAddMethodDropdownOpen;

    [RelayCommand]
    private void ToggleAddMethodDropdown() => IsAddMethodDropdownOpen = !IsAddMethodDropdownOpen;

    [RelayCommand]
    private void SelectAddMethod(string method)
    {
        IsAddMethodDropdownOpen = false;
        IsPastePanelOpen        = false;
        IsQuickLoginOpen        = false;
        switch (method)
        {
            case "Browser":     _ = LoginWithBrowserAsync();  break;
            case "Chrome":      _ = ImportFromChromeAsync();  break;
            case "Cookie":      IsPastePanelOpen = true;      break;
            case "QuickSignIn": _ = StartQuickSignInAsync();  break;
        }
    }

    // 統計カード
    [ObservableProperty] private int    _activeFriendsCount;
    [ObservableProperty] private int    _activeFollowersCount;
    [ObservableProperty] private int    _activeFollowingsCount;
    [ObservableProperty] private bool   _isStatsLoading;
    [ObservableProperty] private bool   _isStatsVisible;

    public string  ActiveDisplayName => _accounts.Accounts.FirstOrDefault(a => a.IsActive)?.DisplayName ?? "";
    public string  ActiveUsername    => _accounts.Accounts.FirstOrDefault(a => a.IsActive)?.Username ?? "";
    public Bitmap? ActiveIcon        => Accounts.FirstOrDefault(a => a.IsActive)?.Icon;
    public string  ActiveStatusColor => Accounts.FirstOrDefault(a => a.IsActive)?.StatusColor ?? "#888888";

    // ロード中は "—"、完了後は数値（レイアウトを固定するため IsVisible を使わない）
    public string FriendsCountDisplay   => IsStatsLoading ? "—" : ActiveFriendsCount.ToString();
    public string FollowersCountDisplay  => IsStatsLoading ? "—" : ActiveFollowersCount.ToString();
    public string FollowingsCountDisplay => IsStatsLoading ? "—" : ActiveFollowingsCount.ToString();

    partial void OnIsStatsLoadingChanged(bool _)
    {
        OnPropertyChanged(nameof(FriendsCountDisplay));
        OnPropertyChanged(nameof(FollowersCountDisplay));
        OnPropertyChanged(nameof(FollowingsCountDisplay));
    }

    partial void OnActiveFriendsCountChanged(int _)   => OnPropertyChanged(nameof(FriendsCountDisplay));
    partial void OnActiveFollowersCountChanged(int _)  => OnPropertyChanged(nameof(FollowersCountDisplay));
    partial void OnActiveFollowingsCountChanged(int _) => OnPropertyChanged(nameof(FollowingsCountDisplay));

    // 既存
    [ObservableProperty] private string _statusMessage      = string.Empty;
    [ObservableProperty] private bool   _isImporting;
    [ObservableProperty] private bool   _isWaitingForLogin;
    [ObservableProperty] private string _manualCookie       = string.Empty;
    [ObservableProperty] private bool   _isPastePanelOpen;
    [ObservableProperty] private bool   _isQuickLoginOpen;
    [ObservableProperty] private string _quickLoginInput    = string.Empty;

    public AccountViewModel(AccountService accounts, RobloxApiService robloxApi,
        FriendsViewModel friendsVm,
        QuickLoginCoordinator quickLoginCoordinator,
        CookieAccountImportService cookieImport,
        AccountActivityRefreshService activityRefresh,
        ChromeImportCoordinator chromeImport,
        AccountEntryViewModelFactory accountEntryFactory,
        AccountDialogCoordinator dialogCoordinator)
    {
        _accounts              = accounts;
        _activityRefresh       = activityRefresh;
        _dialogCoordinator     = dialogCoordinator;
        _chromeImport          = chromeImport;
        _robloxApi             = robloxApi;
        _cookieImport          = cookieImport;
        _quickLoginCoordinator = quickLoginCoordinator;
        _accountEntryFactory   = accountEntryFactory;
        FriendsVm              = friendsVm;
        Reload();
    }

    private void Reload()
    {
        // 既存VMのPropertyChanged購読を解除してからクリア（メモリリーク防止）
        foreach (var vm in Accounts)
            vm.PropertyChanged -= OnEntryPropertyChanged;

        Accounts.Clear();
        var list = _accounts.Accounts;
        for (int i = 0; i < list.Count; i++)
        {
            var entry = _accountEntryFactory.Create(list[i], i,
                e => { _accounts.SetActive(e.Id); Reload(); },
                e => { _accounts.Remove(e.Id);   Reload(); StatusMessage = "Removed"; },
                LaunchAs);
            entry.PropertyChanged += OnEntryPropertyChanged;
            Accounts.Add(entry);
        }

        OnPropertyChanged(nameof(ActiveDisplayName));
        OnPropertyChanged(nameof(ActiveUsername));
        OnPropertyChanged(nameof(ActiveIcon));
        OnPropertyChanged(nameof(ActiveStatusColor));
        IsStatsVisible = _accounts.Accounts.Any(a => a.IsActive);

        // 前の非同期取得をキャンセルしてから再起動
        _presenceCts.Cancel();
        _presenceCts = new CancellationTokenSource();
        var ct = _presenceCts.Token;
        _ = RefreshPresenceAsync(ct);
        _ = RefreshActiveStatsAsync(ct);
    }

    private void OnEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not AccountEntryViewModel vm) return;
        if (!vm.IsActive) return;
        if (e.PropertyName == nameof(AccountEntryViewModel.Icon))
            OnPropertyChanged(nameof(ActiveIcon));
        if (e.PropertyName == nameof(AccountEntryViewModel.StatusColor))
            OnPropertyChanged(nameof(ActiveStatusColor));
    }

    private void LaunchAs(AccountEntryViewModel entry)
    {
        _accounts.SetActive(entry.Id);
        Reload();
        LaunchAsRequested?.Invoke();
    }

    private async Task RefreshPresenceAsync(CancellationToken ct)
    {
        if (Accounts.Count == 0) return;
        try
        {
            var userIds  = Accounts.Select(a => a.UserId).ToList();
            var map = await _activityRefresh.GetPresenceByUserIdAsync(userIds, ct);
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var e in Accounts)
                    if (map.TryGetValue(e.UserId, out var p))
                    { e.PresenceType = p.PresenceType; e.LastLocation = p.LastLocation; }
            });
        }
        catch { }
    }

    private async Task RefreshActiveStatsAsync(CancellationToken ct)
    {
        var active = _accounts.Accounts.FirstOrDefault(a => a.IsActive);
        if (active == null) { IsStatsVisible = false; return; }
        IsStatsLoading = true;
        try
        {
            var stats = await _activityRefresh.GetActiveStatsAsync(active, ct);
            if (ct.IsCancellationRequested) return;
            if (stats == null) return;
            ActiveFriendsCount    = stats.Friends;
            ActiveFollowersCount  = stats.Followers;
            ActiveFollowingsCount = stats.Followings;
        }
        catch { }
        finally { IsStatsLoading = false; }
    }

    private async Task StartQuickSignInAsync()
    {
        var result = await _robloxApi.CreateQuickSignInAsync();
        if (result == null) { StatusMessage = "Failed to create Quick Sign-In session"; return; }

        await _dialogCoordinator.ShowQuickSignInAsync(
            result.Value.Code,
            result.Value.PrivateKey,
            ImportCookieAsync);
    }

    [RelayCommand]
    private void ToggleQuickLoginPanel()
    {
        IsQuickLoginOpen = !IsQuickLoginOpen;
        if (!IsQuickLoginOpen) QuickLoginInput = string.Empty;
    }

    [RelayCommand]
    private async Task RedeemCodeAsync()
    {
        var input = QuickLoginInput.Trim();
        if (input.Length != 6 || !input.All(char.IsDigit))
        { StatusMessage = "Enter a valid 6-digit code"; return; }

        var data = _quickLoginCoordinator.Redeem(input);
        if (data == null) { StatusMessage = "Code is invalid or expired"; return; }

        var existing = _quickLoginCoordinator.FindExistingAccount(_accounts.Accounts, data.UserId);
        if (existing != null)
        {
            _accounts.SetActive(existing.Id);
            Reload();
            QuickLoginInput  = string.Empty;
            IsQuickLoginOpen = false;
            StatusMessage    = $"Switched to {data.DisplayName}";
            return;
        }

        StatusMessage = "Fetching account info...";
        var account = await _quickLoginCoordinator.CreateAccountAsync(data);
        _accounts.Add(account, data.PlaintextCookie);
        _accounts.SetActive(account.Id);
        Reload();
        QuickLoginInput  = string.Empty;
        IsQuickLoginOpen = false;
        StatusMessage    = $"Added & switched to {data.DisplayName}";
    }

    [RelayCommand]
    private async Task ImportFromChromeAsync()
    {
        if (IsImporting) return;
        IsImporting = true;
        _pollCts?.Cancel();
        _pollCts = new CancellationTokenSource();
        try
        {
            var localApp   = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localState = Path.Combine(localApp, "Google", "Chrome", "User Data", "Local State");
            if (!File.Exists(localState)) { StatusMessage = "Chrome not found"; return; }

            StatusMessage = "Importing...";
            var importResult = await _chromeImport.TryImportAuthenticatedCookieAsync();
            var cookie       = importResult.Cookie;
            var userId       = importResult.UserId;

            if (userId == null)
            {
                IsWaitingForLogin = true;
                StatusMessage     = "Please log in to roblox.com in Chrome";
                importResult = await _chromeImport.WaitForAuthenticatedCookieAsync(_pollCts.Token);
                if (importResult == null) return;
                cookie = importResult.Cookie;
                userId = importResult.UserId;
                if (userId == null) return;
            }

            if (_accounts.Accounts.Any(a => a.UserId == userId))
            { StatusMessage = "Account already added"; return; }

            StatusMessage = "Fetching account info...";
            var account = await _cookieImport.CreateAccountAsync(userId.Value);
            _accounts.Add(account, cookie!);
            Reload();
            StatusMessage = $"Added {account.DisplayName}";
        }
        finally
        {
            IsImporting       = false;
            IsWaitingForLogin = false;
            _pollCts?.Dispose();
            _pollCts = null;
        }
    }

    [RelayCommand]
    private void CancelImport()
    {
        _pollCts?.Cancel();
        StatusMessage = "";
    }

    [RelayCommand]
    private async Task LoginWithBrowserAsync()
    {
        try
        {
            var cookie = await _dialogCoordinator.ShowBrowserLoginAsync();
            if (!string.IsNullOrEmpty(cookie)) await ImportCookieAsync(cookie);
        }
        catch { StatusMessage = "WebView2 not available"; }
    }

    internal async Task ImportCookieAsync(string cookie)
    {
        StatusMessage = "Validating...";
        var userId = await _cookieImport.GetAuthenticatedUserIdAsync(cookie);
        if (userId == null) { StatusMessage = "Invalid or expired cookie"; return; }
        if (_accounts.Accounts.Any(a => a.UserId == userId))
        { StatusMessage = "Account already added"; return; }

        StatusMessage = "Fetching account info...";
        var account = await _cookieImport.CreateAccountAsync(userId.Value);
        _accounts.Add(account, cookie);
        Reload();
        StatusMessage = $"Added {account.DisplayName}";
    }

    [RelayCommand]
    private void TogglePastePanel() => IsPastePanelOpen = !IsPastePanelOpen;

    [RelayCommand]
    private async Task ImportManuallyAsync()
    {
        var raw = ManualCookie.Trim();
        if (string.IsNullOrEmpty(raw)) { StatusMessage = "Paste your .ROBLOSECURITY cookie first"; return; }
        const string prefix = ".ROBLOSECURITY=";
        if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            raw = raw[prefix.Length..].Trim();
        await ImportCookieAsync(raw);
        ManualCookie     = string.Empty;
        IsPastePanelOpen = false;
    }

}
