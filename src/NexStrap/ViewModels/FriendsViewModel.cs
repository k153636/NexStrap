using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Models;
using NexStrap.Services;
using System.Collections.ObjectModel;

namespace NexStrap.ViewModels;

public partial class FriendEntryViewModel : ViewModelBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public long    UserId       { get; }
    public string  DisplayName  { get; }
    public int     PresenceType { get; }
    public long?   PlaceId      { get; }
    public string? LastLocation { get; }

    [ObservableProperty] private Bitmap? _icon;

    public bool IsOnline => PresenceType > 0;
    public bool IsInGame => PresenceType == 2;

    public string StatusText => PresenceType switch
    {
        2 => string.IsNullOrWhiteSpace(LastLocation) ? "In Game" : LastLocation,
        1 => "Online",
        _ => "Offline"
    };

    // オフラインは#888888で視認性を確保
    public string StatusColor => PresenceType > 0 ? "#4ADE80" : "#888888";

    public FriendEntryViewModel(long userId, string displayName, int presenceType, long? placeId, string? lastLocation, string? avatarUrl)
    {
        UserId       = userId;
        DisplayName  = displayName;
        PresenceType = presenceType;
        PlaceId      = placeId;
        LastLocation = lastLocation;

        if (!string.IsNullOrEmpty(avatarUrl))
            _ = LoadIconAsync(avatarUrl);
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

public partial class FriendsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly AccountService _accounts;
    private readonly RobloxApiService _robloxApi;
    private readonly FriendNotificationService _friendNotifs;
    private bool _refreshing;
    private bool _refreshPending;

    [ObservableProperty] private ObservableCollection<FriendEntryViewModel> _friends = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;

    public FriendsViewModel(
        SettingsService settings,
        AccountService accounts,
        RobloxApiService robloxApi,
        FriendNotificationService friendNotifs)
    {
        _settings      = settings;
        _accounts      = accounts;
        _robloxApi     = robloxApi;
        _friendNotifs  = friendNotifs;

        // フレンドがオンラインになったら自動でリスト更新
        _friendNotifs.FriendCameOnline += (_, _) =>
        {
            if (!_refreshing)
                Dispatcher.UIThread.InvokeAsync(() => _ = RefreshAsync());
        };

        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            _refreshPending = true;
            return;
        }

        _refreshing = true;
        IsLoading   = true;

        var userId = ResolveCurrentUserId();
        if (userId <= 0)
        {
            StatusText  = "Please log in to Roblox";
            IsLoading   = false;
            _refreshing = false;
            return;
        }

        _friendNotifs.Start(userId);

        try
        {
            var friendList = await _robloxApi.GetFriendsAsync(userId);
            if (userId != ResolveCurrentUserId())
            {
                _refreshPending = true;
                return;
            }

            if (friendList.Count == 0)
            {
                Friends    = [];
                StatusText = "No friends found";
                return;
            }

            var userIds     = friendList.Select(f => f.UserId).ToList();
            var presence    = await _robloxApi.GetFriendPresenceDetailsAsync(userIds, ResolveCurrentAccountCookie());
            var presenceMap = presence.ToDictionary(p => p.UserId);

            var placeNames = new Dictionary<long, string>();
            var placeIds = presence
                .Where(p => p.PresenceType == 2 && p.PlaceId.HasValue && IsGenericInGameLocation(p.LastLocation))
                .Select(p => p.PlaceId!.Value)
                .Distinct()
                .ToList();

            await Task.WhenAll(placeIds.Select(async placeId =>
            {
                var (name, _, _) = await _robloxApi.GetGameInfoAsync(placeId);
                lock (placeNames) placeNames[placeId] = name;
            }));

            var avatarUrls  = new Dictionary<long, string?>();
            await Task.WhenAll(userIds.Select(async id =>
            {
                var url = await _robloxApi.GetUserAvatarHeadshotAsync(id);
                lock (avatarUrls) avatarUrls[id] = url;
            }));

            if (userId != ResolveCurrentUserId())
            {
                _refreshPending = true;
                return;
            }

            var sorted = friendList
                .Select(f =>
                {
                    presenceMap.TryGetValue(f.UserId, out var p);
                    avatarUrls.TryGetValue(f.UserId, out var avatarUrl);
                    var location = ResolveLocationName(p, placeNames);
                    return new FriendEntryViewModel(
                        f.UserId, f.DisplayName,
                        p?.PresenceType ?? 0,
                        p?.PlaceId,
                        location,
                        avatarUrl);
                })
                .OrderByDescending(e => e.PresenceType)
                .ThenBy(e => e.DisplayName)
                .ToList();

            // 新データが揃ってから一括置換 → ローディング中にリストが消えない
            Friends    = new ObservableCollection<FriendEntryViewModel>(sorted);
            StatusText = sorted.Count == 0 ? "No friends found" : string.Empty;
        }
        catch { StatusText = "Failed to load"; }
        finally
        {
            IsLoading   = false;
            _refreshing = false;
            if (_refreshPending)
            {
                _refreshPending = false;
                _ = RefreshAsync();
            }
        }
    }

    private long ResolveCurrentUserId()
    {
        var active = _accounts.Accounts.FirstOrDefault(a => a.IsActive);
        if (active?.UserId > 0) return active.UserId;
        return _accounts.Accounts.Count > 0 ? 0 : _settings.Settings.CachedRobloxUserId;
    }

    private string? ResolveCurrentAccountCookie()
    {
        return _accounts.Accounts.Any(a => a.IsActive)
            ? _accounts.GetActiveCookie()
            : null;
    }

    private static bool IsGenericInGameLocation(string? location)
        => string.IsNullOrWhiteSpace(location)
           || string.Equals(location, "In Game", StringComparison.OrdinalIgnoreCase)
           || string.Equals(location, "Playing", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveLocationName(
        FriendPresenceDetail? presence,
        IReadOnlyDictionary<long, string> placeNames)
    {
        if (presence == null) return null;
        if (!IsGenericInGameLocation(presence.LastLocation)) return presence.LastLocation;
        return presence.PlaceId.HasValue && placeNames.TryGetValue(presence.PlaceId.Value, out var name)
            ? name
            : presence.LastLocation;
    }
}
