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
    private readonly Dictionary<long, long>    _universeIdCache = new();

    public async Task<(string name, string? iconUrl, string? creator)> GetGameInfoAsync(long placeId, long universeId = 0)
    {
        if (_gameCache.TryGetValue(placeId, out var cached)) return cached;

        try
        {
            // universeId がログから直接取得できていれば API 呼び出しをスキップ
            long? resolvedUniverseId = universeId > 0 ? universeId : await GetUniverseIdAsync(placeId);
            if (resolvedUniverseId == null) return ("Roblox", null, null);

            var (name, creator) = await GetGameNameAndCreatorAsync(resolvedUniverseId.Value);
            var iconUrl         = await GetGameIconUrlAsync(resolvedUniverseId.Value);

            var result = (name ?? "Roblox", iconUrl, creator);
            // 失敗結果（name="Roblox" かつ iconUrl=null）はキャッシュしない
            if (result.Item1 != "Roblox" || iconUrl != null)
                _gameCache[placeId] = result;
            return result;
        }
        catch
        {
            return ("Roblox", null, null);
        }
    }

    public async Task<long?> GetUniverseIdAsync(long placeId)
    {
        if (_universeIdCache.TryGetValue(placeId, out var cached)) return cached;
        try
        {
            var url = $"https://apis.roblox.com/universes/v1/places/{placeId}/universe";
            var json = await Http.GetStringAsync(url);
            var universeId = JObject.Parse(json)["universeId"]?.Value<long>();
            if (universeId.HasValue) _universeIdCache[placeId] = universeId.Value;
            return universeId;
        }
        catch { return null; }
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

    public async Task<string?> GetAuthTicketAsync(string cookie)
    {
        try
        {
            using var req1 = new HttpRequestMessage(HttpMethod.Post,
                "https://auth.roblox.com/v1/authentication-ticket");
            req1.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            req1.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com");
            req1.Content = new StringContent("", Encoding.UTF8, "application/json");
            var resp1 = await Http.SendAsync(req1);
            if (!resp1.Headers.TryGetValues("x-csrf-token", out var tokens)) return null;
            var csrf = tokens.First();

            using var req2 = new HttpRequestMessage(HttpMethod.Post,
                "https://auth.roblox.com/v1/authentication-ticket");
            req2.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            req2.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com");
            req2.Headers.TryAddWithoutValidation("X-Csrf-Token", csrf);
            req2.Content = new StringContent("", Encoding.UTF8, "application/json");
            var resp2 = await Http.SendAsync(req2);
            if (!resp2.IsSuccessStatusCode) return null;
            if (!resp2.Headers.TryGetValues("RBX-Authentication-Ticket", out var tickets)) return null;
            return tickets.First();
        }
        catch { return null; }
    }

    /// <summary>
    /// Bloxstrap 互換: gamejoin.roblox.com/v1/join-* API を呼び joinScriptUrl と authTicket を返す。
    /// gameId 指定で特定サーバー参加、accessCode でプライベートサーバー参加。
    /// </summary>
    public async Task<(string? JoinScriptUrl, string? AuthTicket)> GetJoinInfoAsync(
        string cookie, long placeId, string? gameId = null, string? accessCode = null)
    {
        try
        {
            // CSRF トークン取得
            using var csrf1 = new HttpRequestMessage(HttpMethod.Post,
                "https://auth.roblox.com/v1/authentication-ticket");
            csrf1.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            csrf1.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com");
            csrf1.Content = new StringContent("", Encoding.UTF8, "application/json");
            var r1 = await Http.SendAsync(csrf1);
            if (!r1.Headers.TryGetValues("x-csrf-token", out var toks)) return (null, null);
            var csrf = toks.First();

            // join エンドポイントとボディを決定
            string endpoint;
            JObject body;
            if (!string.IsNullOrEmpty(accessCode))
            {
                endpoint = "https://gamejoin.roblox.com/v1/join-private-game";
                body = new JObject { ["placeId"] = placeId, ["accessCode"] = accessCode, ["linkCode"] = accessCode };
            }
            else if (!string.IsNullOrEmpty(gameId))
            {
                endpoint = "https://gamejoin.roblox.com/v1/join-game-instance";
                body = new JObject { ["placeId"] = placeId, ["gameId"] = gameId, ["isTeleport"] = false };
            }
            else
            {
                endpoint = "https://gamejoin.roblox.com/v1/join-game";
                body = new JObject { ["placeId"] = placeId, ["guestData"] = "" };
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            req.Headers.TryAddWithoutValidation("X-CSRF-Token", csrf);
            req.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com");
            req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

            var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return (null, null);

            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
            return (json["joinScriptUrl"]?.Value<string>(),
                    json["authenticationTicket"]?.Value<string>());
        }
        catch { return (null, null); }
    }

    public async Task<List<FriendPresenceDetail>> GetFriendPresenceDetailsAsync(IList<long> userIds)
    {
        try
        {
            var body    = new JObject { ["userIds"] = new JArray(userIds.Cast<object>().ToArray()) };
            var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            var resp    = await Http.PostAsync("https://presence.roblox.com/v1/presence/users", content);
            if (!resp.IsSuccessStatusCode) return [];
            var json = await resp.Content.ReadAsStringAsync();
            var data = JObject.Parse(json)["userPresences"] as JArray ?? [];
            return [..data.Select(p => new FriendPresenceDetail(
                p["userId"]!.Value<long>(),
                p["userPresenceType"]?.Value<int>() ?? 0,
                p["placeId"]?.Value<long?>(),
                p["lastLocation"]?.Value<string>()))];
        }
        catch { return []; }
    }
}
