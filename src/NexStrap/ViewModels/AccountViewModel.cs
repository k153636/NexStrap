using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using NexStrap.Models;
using NexStrap.Services;
using NexStrap.Views;
using System.Collections.ObjectModel;

namespace NexStrap.ViewModels;

public partial class AccountEntryViewModel : ViewModelBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly QuickLoginService _quickLogin;
    private DispatcherTimer? _timer;

    public RobloxAccount Account { get; }

    public Guid    Id            => Account.Id;
    public long    UserId        => Account.UserId;
    public string  Username      => Account.Username;
    public string  DisplayName   => Account.DisplayName;
    public bool    IsActive      => Account.IsActive;
    public int     InstanceIndex { get; }
    public string  InstanceLabel => $"Instance {InstanceIndex + 1}";
    public string  LastUsedText  => FormatRelativeTime(Account.LastUsedAt);

    public CommunityToolkit.Mvvm.Input.IRelayCommand SetActiveCommand { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand RemoveCommand    { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand LaunchAsCommand  { get; }

    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private string? _activeCode;
    [ObservableProperty] private int     _codeSecondsLeft;
    [ObservableProperty] private int     _presenceType;
    [ObservableProperty] private string? _lastLocation;

    public bool   HasActiveCode => ActiveCode != null;
    public string StatusColor   => PresenceType > 0 ? "#4ADE80" : "#888888";
    public bool   IsInGame      => PresenceType == 2;

    partial void OnPresenceTypeChanged(int value)
    {
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(IsInGame));
    }

    public AccountEntryViewModel(RobloxAccount account, int index,
        Action<AccountEntryViewModel> setActive,
        Action<AccountEntryViewModel> remove,
        QuickLoginService quickLogin,
        Action<AccountEntryViewModel> launchAs)
    {
        Account          = account;
        InstanceIndex    = index;
        _quickLogin      = quickLogin;
        SetActiveCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => setActive(this));
        RemoveCommand    = new CommunityToolkit.Mvvm.Input.RelayCommand(() => remove(this));
        LaunchAsCommand  = new CommunityToolkit.Mvvm.Input.RelayCommand(() => launchAs(this));
        if (!string.IsNullOrEmpty(account.AvatarUrl))
            _ = LoadIconAsync(account.AvatarUrl);
    }

    [RelayCommand]
    private void GenerateCode()
    {
        var code = _quickLogin.GenerateCode(Account.Id);
        if (code == null) return;
        ActiveCode       = code;
        CodeSecondsLeft  = (int)_quickLogin.GetRemaining(ActiveCode)!.Value.TotalSeconds;
        OnPropertyChanged(nameof(HasActiveCode));

        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            if (ActiveCode == null) { _timer.Stop(); return; }
            var rem = _quickLogin.GetRemaining(ActiveCode);
            if (rem == null)
            {
                ActiveCode = null;
                OnPropertyChanged(nameof(HasActiveCode));
                _timer.Stop();
                return;
            }
            CodeSecondsLeft = (int)rem.Value.TotalSeconds;
        };
        _timer.Start();
    }

    private async Task LoadIconAsync(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            Icon = new Bitmap(ms);
        }
        catch { }
    }

    private static string FormatRelativeTime(DateTime? dt)
    {
        if (dt == null) return "";
        var diff = DateTime.UtcNow - dt.Value;
        if (diff.TotalMinutes < 1)  return "Just now";
        if (diff.TotalHours   < 1)  return $"{(int)diff.TotalMinutes} min ago";
        if (diff.TotalDays    < 1)  return $"{(int)diff.TotalHours} hr ago";
        if (diff.TotalDays    < 30) return $"{(int)diff.TotalDays} days ago";
        return dt.Value.ToLocalTime().ToString("MMM d");
    }
}

public partial class AccountViewModel : ViewModelBase
{
    private readonly AccountService    _accounts;
    private readonly RobloxApiService  _robloxApi;
    private readonly QuickLoginService _quickLogin;
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
        QuickLoginService quickLogin, FriendsViewModel friendsVm)
    {
        _accounts   = accounts;
        _robloxApi  = robloxApi;
        _quickLogin = quickLogin;
        FriendsVm   = friendsVm;
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
            var entry = new AccountEntryViewModel(list[i], i,
                e => { _accounts.SetActive(e.Id); Reload(); },
                e => { _accounts.Remove(e.Id);   Reload(); StatusMessage = "Removed"; },
                _quickLogin,
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
            var presence = await _robloxApi.GetFriendPresenceDetailsAsync(userIds);
            if (ct.IsCancellationRequested) return;
            var map = presence.ToDictionary(p => p.UserId);
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
            var t1 = _robloxApi.GetFriendsCountAsync(active.UserId);
            var t2 = _robloxApi.GetFollowersCountAsync(active.UserId);
            var t3 = _robloxApi.GetFollowingsCountAsync(active.UserId);
            await Task.WhenAll(t1, t2, t3);
            if (ct.IsCancellationRequested) return;
            ActiveFriendsCount    = t1.Result;
            ActiveFollowersCount  = t2.Result;
            ActiveFollowingsCount = t3.Result;
        }
        catch { }
        finally { IsStatsLoading = false; }
    }

    private async Task StartQuickSignInAsync()
    {
        var result = await _robloxApi.CreateQuickSignInAsync();
        if (result == null) { StatusMessage = "Failed to create Quick Sign-In session"; return; }

        var vm     = new QuickSignInViewModel(result.Value.Code, result.Value.PrivateKey, _robloxApi);
        var dialog = new QuickSignInDialog { DataContext = vm };
        vm.Completed += async (_, _) =>
        {
            if (vm.ResultCookie != null)
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ImportCookieAsync(vm.ResultCookie);
                    dialog.Close();
                });
        };

        var mainWin = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWin != null) await dialog.ShowDialog(mainWin);
        else dialog.Show();
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

        var data = _quickLogin.Redeem(input);
        if (data == null) { StatusMessage = "Code is invalid or expired"; return; }

        var existing = _accounts.Accounts.FirstOrDefault(a => a.UserId == data.UserId);
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
        var avatarUrl = data.AvatarUrl ?? await _robloxApi.GetUserAvatarHeadshotAsync(data.UserId);
        var account   = new RobloxAccount
        {
            UserId      = data.UserId,
            Username    = data.Username,
            DisplayName = data.DisplayName,
            AvatarUrl   = avatarUrl,
        };
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
            string? cookie = null;
            long?   userId = null;

            for (int attempt = 0; attempt < 3 && userId == null; attempt++)
            {
                if (attempt > 0) await Task.Delay(1200);
                cookie = await BrowserCookieImporter.TryImportAsync(BrowserType.Chrome);
                if (cookie != null) userId = await GetAuthenticatedUserIdAsync(cookie);
            }

            if (userId == null)
            {
                IsWaitingForLogin = true;
                StatusMessage     = "Please log in to roblox.com in Chrome";
                while (!_pollCts.Token.IsCancellationRequested)
                {
                    try { await Task.Delay(3000, _pollCts.Token); }
                    catch (OperationCanceledException) { return; }
                    cookie = await BrowserCookieImporter.TryImportAsync(BrowserType.Chrome);
                    if (cookie != null) userId = await GetAuthenticatedUserIdAsync(cookie);
                    if (userId != null) break;
                }
                if (userId == null) return;
            }

            if (_accounts.Accounts.Any(a => a.UserId == userId))
            { StatusMessage = "Account already added"; return; }

            StatusMessage = "Fetching account info...";
            var info      = await _robloxApi.GetUserInfoAsync(userId.Value);
            var avatarUrl = await _robloxApi.GetUserAvatarHeadshotAsync(userId.Value);
            var account   = new RobloxAccount
            {
                UserId      = userId.Value,
                Username    = info?.username    ?? userId.Value.ToString(),
                DisplayName = info?.displayName ?? userId.Value.ToString(),
                AvatarUrl   = avatarUrl,
            };
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
            var mainWindow = (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var loginWindow = new RobloxLoginWindow();
            if (mainWindow != null) await loginWindow.ShowDialog(mainWindow);
            else loginWindow.Show();
            var cookie = loginWindow.CapturedCookie;
            if (!string.IsNullOrEmpty(cookie)) await ImportCookieAsync(cookie);
        }
        catch { StatusMessage = "WebView2 not available"; }
    }

    internal async Task ImportCookieAsync(string cookie)
    {
        StatusMessage = "Validating...";
        var userId = await GetAuthenticatedUserIdAsync(cookie);
        if (userId == null) { StatusMessage = "Invalid or expired cookie"; return; }
        if (_accounts.Accounts.Any(a => a.UserId == userId))
        { StatusMessage = "Account already added"; return; }

        StatusMessage = "Fetching account info...";
        var info      = await _robloxApi.GetUserInfoAsync(userId.Value);
        var avatarUrl = await _robloxApi.GetUserAvatarHeadshotAsync(userId.Value);
        var account   = new RobloxAccount
        {
            UserId      = userId.Value,
            Username    = info?.username    ?? userId.Value.ToString(),
            DisplayName = info?.displayName ?? userId.Value.ToString(),
            AvatarUrl   = avatarUrl,
        };
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

    private static async Task<long?> GetAuthenticatedUserIdAsync(string cookie)
    {
        try
        {
            using var client = new HttpClient();
            using var req    = new HttpRequestMessage(HttpMethod.Get,
                "https://users.roblox.com/v1/users/authenticated");
            req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
            return obj["id"]?.Value<long>();
        }
        catch { return null; }
    }
}
