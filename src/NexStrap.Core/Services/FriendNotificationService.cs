using NexStrap.Core.Models;

namespace NexStrap.Core.Services;

public class FriendOnlineEventArgs(string displayName) : EventArgs
{
    public string DisplayName { get; } = displayName;
}

public class FriendNotificationService(RobloxApiService robloxApi) : IDisposable
{
    private Timer?        _timer;
    private HashSet<long> _onlineIds    = [];
    private bool          _isFirstPoll  = true;
    private long          _userId;

    public event EventHandler<FriendOnlineEventArgs>? FriendCameOnline;

    public void Start(long userId)
    {
        if (_userId == userId) return;
        _userId      = userId;
        _isFirstPoll = true;
        _timer?.Dispose();
        _timer = new Timer(_ => _ = PollAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    public void SetBackgroundMode(bool background, bool playing)
    {
        if (_timer == null) return;

        if (background && playing)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        _timer.Change(
            background ? TimeSpan.FromMinutes(5) : TimeSpan.Zero,
            background ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(60));
    }

    private async Task PollAsync()
    {
        if (_userId == 0) return;
        try
        {
            var friends = await robloxApi.GetFriendsAsync(_userId);
            if (friends.Count == 0) return;

            var ids       = friends.Select(f => f.UserId).ToList();
            var presences = await robloxApi.GetUserPresencesAsync(ids);

            var nowOnline = presences
                .Where(p => p.UserPresenceType > 0)
                .Select(p => p.UserId)
                .ToHashSet();

            if (!_isFirstPoll)
            {
                foreach (var uid in nowOnline.Except(_onlineIds))
                {
                    var friend = friends.FirstOrDefault(f => f.UserId == uid);
                    if (friend != null)
                        FriendCameOnline?.Invoke(this, new FriendOnlineEventArgs(friend.DisplayName));
                }
            }

            _onlineIds   = nowOnline;
            _isFirstPoll = false;
        }
        catch { }
    }

    public void Dispose() => _timer?.Dispose();
}
