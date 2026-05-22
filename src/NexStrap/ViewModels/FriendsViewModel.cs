using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Services;
using System.Collections.ObjectModel;

namespace NexStrap.ViewModels;

public partial class FriendEntryViewModel : ViewModelBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public long   UserId      { get; }
    public string DisplayName { get; }
    public int    PresenceType { get; }
    public long?  PlaceId     { get; }
    public string? LastLocation { get; }

    [ObservableProperty] private Bitmap? _icon;

    public bool   IsOnline    => PresenceType > 0;
    public bool   IsInGame    => PresenceType == 2;

    public string StatusText => PresenceType switch
    {
        2 => LastLocation ?? "In Game",
        1 => "Online",
        _ => "Offline"
    };

    public string StatusColor => PresenceType > 0 ? "#4ADE80" : "#444444";

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
    private readonly RobloxApiService _robloxApi;

    public ObservableCollection<FriendEntryViewModel> Friends { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;

    public FriendsViewModel(SettingsService settings, RobloxApiService robloxApi)
    {
        _settings  = settings;
        _robloxApi = robloxApi;
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var userId = _settings.Settings.CachedRobloxUserId;
        if (userId <= 0)
        {
            StatusText = "Please log in to Roblox";
            return;
        }

        IsLoading  = true;
        StatusText = string.Empty;
        try
        {
            var friendList = await _robloxApi.GetFriendsAsync(userId);
            if (friendList.Count == 0)
            {
                Friends.Clear();
                StatusText = "No friends found";
                return;
            }

            var userIds  = friendList.Select(f => f.UserId).ToList();
            var presence = await _robloxApi.GetFriendPresenceDetailsAsync(userIds);
            var presenceMap = presence.ToDictionary(p => p.UserId);

            var avatarUrls = new Dictionary<long, string?>();
            var avatarTasks = userIds.Select(async id =>
            {
                var url = await _robloxApi.GetUserAvatarHeadshotAsync(id);
                lock (avatarUrls) avatarUrls[id] = url;
            });
            await Task.WhenAll(avatarTasks);

            var entries = friendList.Select(f =>
            {
                presenceMap.TryGetValue(f.UserId, out var p);
                avatarUrls.TryGetValue(f.UserId, out var avatarUrl);
                return new FriendEntryViewModel(
                    f.UserId,
                    f.DisplayName,
                    p?.PresenceType ?? 0,
                    p?.PlaceId,
                    p?.LastLocation,
                    avatarUrl);
            });

            var sorted = entries
                .OrderByDescending(e => e.PresenceType)
                .ThenBy(e => e.DisplayName)
                .ToList();

            Friends.Clear();
            foreach (var e in sorted)
                Friends.Add(e);

            if (Friends.Count == 0)
                StatusText = "No friends found";
        }
        catch { StatusText = "Failed to load"; }
        finally { IsLoading = false; }
    }
}
