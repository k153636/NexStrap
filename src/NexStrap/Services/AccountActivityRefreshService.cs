using NexStrap.Models;

namespace NexStrap.Services;

public sealed record AccountStatsSnapshot(int Friends, int Followers, int Followings);

public sealed class AccountActivityRefreshService(RobloxApiService robloxApi)
{
    public async Task<Dictionary<long, FriendPresenceDetail>> GetPresenceByUserIdAsync(
        IList<long> userIds,
        CancellationToken ct)
    {
        var presence = await robloxApi.GetFriendPresenceDetailsAsync(userIds);
        if (ct.IsCancellationRequested) return [];
        return presence.ToDictionary(p => p.UserId);
    }

    public async Task<AccountStatsSnapshot?> GetActiveStatsAsync(RobloxAccount? active, CancellationToken ct)
    {
        if (active == null) return null;

        var t1 = robloxApi.GetFriendsCountAsync(active.UserId);
        var t2 = robloxApi.GetFollowersCountAsync(active.UserId);
        var t3 = robloxApi.GetFollowingsCountAsync(active.UserId);
        await Task.WhenAll(t1, t2, t3);
        if (ct.IsCancellationRequested) return null;

        return new AccountStatsSnapshot(t1.Result, t2.Result, t3.Result);
    }
}
