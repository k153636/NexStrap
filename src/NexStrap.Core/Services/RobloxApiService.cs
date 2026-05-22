using System.Text;
using Newtonsoft.Json.Linq;
using NexStrap.Core.Models;

namespace NexStrap.Core.Services;

public class RobloxApiService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly Dictionary<long, (string name, string? iconUrl, string? creator)> _gameCache = new();
    private readonly Dictionary<long, string?> _avatarCache = new();

    public async Task<(string name, string? iconUrl, string? creator)> GetGameInfoAsync(long placeId)
    {
        if (_gameCache.TryGetValue(placeId, out var cached)) return cached;

        try
        {
            var universeId = await GetUniverseIdAsync(placeId);
            if (universeId == null) return ("Roblox", null, null);

            var (name, creator) = await GetGameNameAndCreatorAsync(universeId.Value);
            var iconUrl         = await GetGameIconUrlAsync(universeId.Value);

            var result = (name ?? "Roblox", iconUrl, creator);
            _gameCache[placeId] = result;
            return result;
        }
        catch
        {
            return ("Roblox", null, null);
        }
    }

    private static async Task<long?> GetUniverseIdAsync(long placeId)
    {
        var url = $"https://apis.roblox.com/universes/v1/places/{placeId}/universe";
        var json = await Http.GetStringAsync(url);
        var obj = JObject.Parse(json);
        return obj["universeId"]?.Value<long>();
    }

    private static async Task<(string? name, string? creator)> GetGameNameAndCreatorAsync(long universeId)
    {
        var url      = $"https://games.roblox.com/v1/games?universeIds={universeId}";
        var json     = await Http.GetStringAsync(url);
        var data     = JObject.Parse(json)["data"]?[0];
        var creatorName    = data?["creator"]?["name"]?.Value<string>();
        var hasVerified    = data?["creator"]?["hasVerifiedBadge"]?.Value<bool>() ?? false;
        var creatorDisplay = creatorName != null && hasVerified ? $"{creatorName} ☑️" : creatorName;
        return (data?["name"]?.Value<string>(), creatorDisplay);
    }

    private static async Task<string?> GetGameIconUrlAsync(long universeId)
    {
        var url = $"https://thumbnails.roblox.com/v1/games/icons" +
                  $"?universeIds={universeId}&size=512x512&format=Png&isCircular=false";
        var json = await Http.GetStringAsync(url);
        var obj = JObject.Parse(json);
        return obj["data"]?[0]?["imageUrl"]?.Value<string>();
    }

    // ゲームサーバーの国コードを返す（例: "SG", "US"）
    public async Task<string?> GetServerCountryCodeAsync(string ip)
    {
        try
        {
            var json = await Http.GetStringAsync($"https://ipinfo.io/{ip}/json");
            return JObject.Parse(json)["country"]?.Value<string>();
        }
        catch { return null; }
    }

    // プレイヤー自身の国名を返す（例: "Japan", "United States"）
    public async Task<string?> GetMyCountryAsync()
    {
        try
        {
            var json = await Http.GetStringAsync("https://ipinfo.io/json");
            var obj  = JObject.Parse(json);
            // ipinfo は国コードしか返さないので country フィールドをそのまま使う
            return obj["country"]?.Value<string>();
        }
        catch { return null; }
    }

    public async Task<List<FriendInfo>> GetFriendsAsync(long userId)
    {
        try
        {
            var url  = $"https://friends.roblox.com/v1/users/{userId}/friends?userSort=0&limit=100";
            var json = await Http.GetStringAsync(url);
            var data = JObject.Parse(json)["data"] as JArray ?? [];
            return [..data.Select(f => new FriendInfo(
                f["id"]!.Value<long>(),
                f["displayName"]?.Value<string>() ?? f["name"]?.Value<string>() ?? "Unknown"))];
        }
        catch { return []; }
    }

    public async Task<List<PresenceInfo>> GetUserPresencesAsync(IList<long> userIds)
    {
        try
        {
            var body    = new JObject { ["userIds"] = new JArray(userIds.Cast<object>().ToArray()) };
            var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            var resp    = await Http.PostAsync("https://presence.roblox.com/v1/presence/users", content);
            if (!resp.IsSuccessStatusCode) return [];
            var json = await resp.Content.ReadAsStringAsync();
            var data = JObject.Parse(json)["userPresences"] as JArray ?? [];
            return [..data.Select(p => new PresenceInfo(
                p["userId"]!.Value<long>(),
                p["userPresenceType"]?.Value<int>() ?? 0))];
        }
        catch { return []; }
    }

    public async Task<(string username, string displayName)?> GetUserInfoAsync(long userId)
    {
        try
        {
            var json        = await Http.GetStringAsync($"https://users.roblox.com/v1/users/{userId}");
            var obj         = JObject.Parse(json);
            var username    = obj["name"]?.Value<string>();
            var displayName = obj["displayName"]?.Value<string>();
            if (username == null) return null;
            return (username, displayName ?? username);
        }
        catch { return null; }
    }

    public async Task<string?> GetUserAvatarHeadshotAsync(long userId)
    {
        if (_avatarCache.TryGetValue(userId, out var cached)) return cached;

        try
        {
            var url = $"https://thumbnails.roblox.com/v1/users/avatar-headshot" +
                      $"?userIds={userId}&size=150x150&format=Png&isCircular=false";
            var json = await Http.GetStringAsync(url);
            var obj = JObject.Parse(json);
            var imageUrl = obj["data"]?[0]?["imageUrl"]?.Value<string>();
            _avatarCache[userId] = imageUrl;
            return imageUrl;
        }
        catch { return null; }
    }
}
