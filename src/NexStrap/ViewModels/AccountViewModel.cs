using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using NexStrap.Core.Models;
using NexStrap.Core.Services;
using System.Collections.ObjectModel;

namespace NexStrap.ViewModels;

public partial class AccountEntryViewModel : ViewModelBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public RobloxAccount Account { get; }

    public Guid    Id          => Account.Id;
    public long    UserId      => Account.UserId;
    public string  Username    => Account.Username;
    public string  DisplayName => Account.DisplayName;
    public bool    IsActive    => Account.IsActive;

    [ObservableProperty] private Bitmap? _icon;

    public AccountEntryViewModel(RobloxAccount account)
    {
        Account = account;
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

    public AccountViewModel(AccountService accounts, RobloxApiService robloxApi)
    {
        _accounts = accounts;
        _robloxApi = robloxApi;
        Reload();
    }

    private void Reload()
    {
        Accounts.Clear();
        foreach (var a in _accounts.Accounts)
            Accounts.Add(new AccountEntryViewModel(a));
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
