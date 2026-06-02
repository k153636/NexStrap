using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using NexStrap.Core.Models;
using NexStrap.Core.Services;
using NexStrap.Views;
using System.Collections.ObjectModel;

namespace NexStrap.ViewModels;

public partial class AccountEntryViewModel : ViewModelBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public RobloxAccount Account { get; }

    public Guid    Id            => Account.Id;
    public long    UserId        => Account.UserId;
    public string  Username      => Account.Username;
    public string  DisplayName   => Account.DisplayName;
    public bool    IsActive      => Account.IsActive;
    public int     InstanceIndex { get; }
    public string  InstanceLabel => $"Instance {InstanceIndex + 1}";

    // 親 ViewModel への参照不要にするためコマンドをここに持つ
    public CommunityToolkit.Mvvm.Input.IRelayCommand SetActiveCommand { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand RemoveCommand    { get; }

    [ObservableProperty] private Bitmap? _icon;

    public AccountEntryViewModel(RobloxAccount account, int index,
        Action<AccountEntryViewModel> setActive, Action<AccountEntryViewModel> remove)
    {
        Account          = account;
        InstanceIndex    = index;
        SetActiveCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => setActive(this));
        RemoveCommand    = new CommunityToolkit.Mvvm.Input.RelayCommand(() => remove(this));
        if (!string.IsNullOrEmpty(account.AvatarUrl))
            _ = LoadIconAsync(account.AvatarUrl);
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
}

public partial class AccountViewModel : ViewModelBase
{
    private readonly AccountService _accounts;
    private readonly RobloxApiService _robloxApi;
    private CancellationTokenSource? _pollCts;

    public ObservableCollection<AccountEntryViewModel> Accounts { get; } = [];

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool   _isImporting;
    [ObservableProperty] private bool   _isWaitingForLogin;
    [ObservableProperty] private string _manualCookie = string.Empty;
    [ObservableProperty] private bool   _isPastePanelOpen;

    public AccountViewModel(AccountService accounts, RobloxApiService robloxApi)
    {
        _accounts = accounts;
        _robloxApi = robloxApi;
        Reload();
    }

    private void Reload()
    {
        Accounts.Clear();
        var list = _accounts.Accounts;
        for (int i = 0; i < list.Count; i++)
            Accounts.Add(new AccountEntryViewModel(list[i], i,
                e => { _accounts.SetActive(e.Id); Reload(); },
                e => { _accounts.Remove(e.Id); Reload(); StatusMessage = "Removed"; }));
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
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localState = Path.Combine(localApp, "Google", "Chrome", "User Data", "Local State");
            if (!File.Exists(localState))
            {
                StatusMessage = "Chrome not found";
                return;
            }

            StatusMessage = "Importing...";
            string? cookie = null;
            long?   userId = null;

            for (int attempt = 0; attempt < 3 && userId == null; attempt++)
            {
                if (attempt > 0) await Task.Delay(1200);
                cookie = await BrowserCookieImporter.TryImportAsync(BrowserType.Chrome);
                if (cookie != null)
                    userId = await GetAuthenticatedUserIdAsync(cookie);
            }

            if (userId == null)
            {
                IsWaitingForLogin = true;
                StatusMessage = "Please log in to roblox.com in Chrome";

                while (!_pollCts.Token.IsCancellationRequested)
                {
                    try { await Task.Delay(3000, _pollCts.Token); }
                    catch (OperationCanceledException) { return; }

                    cookie = await BrowserCookieImporter.TryImportAsync(BrowserType.Chrome);
                    if (cookie != null)
                        userId = await GetAuthenticatedUserIdAsync(cookie);
                    if (userId != null) break;
                }

                if (userId == null) return;
            }

            if (_accounts.Accounts.Any(a => a.UserId == userId))
            { StatusMessage = "Account already added"; return; }

            StatusMessage = "Fetching account info...";
            var info      = await _robloxApi.GetUserInfoAsync(userId.Value);
            var avatarUrl = await _robloxApi.GetUserAvatarHeadshotAsync(userId.Value);

            var account = new RobloxAccount
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
            IsImporting      = false;
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
            if (mainWindow != null)
                await loginWindow.ShowDialog(mainWindow);
            else
                loginWindow.Show();

            var cookie = loginWindow.CapturedCookie;
            if (!string.IsNullOrEmpty(cookie))
                await ImportCookieAsync(cookie);
        }
        catch { StatusMessage = "WebView2 not available"; }
    }

    private async Task ImportCookieAsync(string cookie)
    {
        StatusMessage = "Validating...";
        var userId = await GetAuthenticatedUserIdAsync(cookie);
        if (userId == null) { StatusMessage = "Invalid or expired cookie"; return; }

        if (_accounts.Accounts.Any(a => a.UserId == userId))
        { StatusMessage = "Account already added"; return; }

        StatusMessage = "Fetching account info...";
        var info      = await _robloxApi.GetUserInfoAsync(userId.Value);
        var avatarUrl = await _robloxApi.GetUserAvatarHeadshotAsync(userId.Value);

        var account = new RobloxAccount
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

    [RelayCommand]
    private void SetActive(AccountEntryViewModel entry)
    {
        _accounts.SetActive(entry.Id);
        Reload();
    }

    [RelayCommand]
    private void Remove(AccountEntryViewModel entry)
    {
        _accounts.Remove(entry.Id);
        Reload();
        StatusMessage = "Removed";
    }

    private static async Task<long?> GetAuthenticatedUserIdAsync(string cookie)
    {
        try
        {
            using var client = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated");
            req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            var obj = JObject.Parse(json);
            return obj["id"]?.Value<long>();
        }
        catch { return null; }
    }
}
