using System.Text;
using Newtonsoft.Json.Linq;
using NexStrap.Models;

namespace NexStrap.Services;

public class RobloxApiService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly Dictionary<(long placeId, bool english), (string name, string? iconUrl, string? creator)> _gameCache = new();
    private readonly Dictionary<long, string?> _avatarCache = new();
    private readonly Dictionary<long, long>    _universeIdCache = new();

    public async Task<(string name, string? iconUrl, string? creator)> GetGameInfoAsync(long placeId, long universeId = 0, bool english = false)
    {
        var cacheKey = (placeId, english);
        if (_gameCache.TryGetValue(cacheKey, out var cached)) return cached;

        try
        {
            // universeId 縺後Ο繧ｰ縺九ｉ逶ｴ謗･蜿門ｾ励〒縺阪※縺・ｌ縺ｰ API 蜻ｼ縺ｳ蜃ｺ縺励ｒ繧ｹ繧ｭ繝・・
            long? resolvedUniverseId = universeId > 0 ? universeId : await GetUniverseIdAsync(placeId);
            if (resolvedUniverseId == null) return ("Roblox", null, null);

            var (name, creator) = await GetGameNameAndCreatorAsync(resolvedUniverseId.Value, english);
            var iconUrl         = await GetGameIconUrlAsync(resolvedUniverseId.Value);

            var result = (name ?? "Roblox", iconUrl, creator);
            // 螟ｱ謨礼ｵ先棡・・ame="Roblox" 縺九▽ iconUrl=null・峨・繧ｭ繝｣繝・す繝･縺励↑縺・
            if (result.Item1 != "Roblox" || iconUrl != null)
                _gameCache[cacheKey] = result;
            return result;
        }
        catch
        {
            return ("Roblox", null, null);
        }
    }

    public void ClearGameCache() => _gameCache.Clear();

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

    private static async Task<(string? name, string? creator)> GetGameNameAndCreatorAsync(long universeId, bool english = false)
    {
        async Task<(string? name, string? creator)> ReadGamesEndpointAsync()
        {
            var url = $"https://games.roblox.com/v1/games?universeIds={universeId}";
            string json;
            if (english)
            {
                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                req.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                using var resp = await Http.SendAsync(req);
                json = await resp.Content.ReadAsStringAsync();
            }
            else
            {
                json = await Http.GetStringAsync(url);
            }

            var data = JObject.Parse(json)["data"] as JArray;
            var item = data?.FirstOrDefault();
            if (item == null) return (null, null);

            var creatorName = item["creator"]?["name"]?.Value<string>();
            var hasVerified = item["creator"]?["hasVerifiedBadge"]?.Value<bool>() ?? false;
            var creatorDisplay = creatorName != null && hasVerified ? $"{creatorName} ☑️" : creatorName;
            return (item["name"]?.Value<string>(), creatorDisplay);
        }

        async Task<(string? name, string? creator)> ReadUniverseEndpointAsync()
        {
            var json = await Http.GetStringAsync($"https://develop.roblox.com/v1/universes/{universeId}");
            var obj = JObject.Parse(json);
            var name = obj["name"]?.Value<string>();
            var creatorName = obj["creatorName"]?.Value<string>();
            return (name, creatorName);
        }

        var (name, creator) = await ReadGamesEndpointAsync();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(creator))
        {
            var fallback = await ReadUniverseEndpointAsync();
            name ??= fallback.name;
            creator ??= fallback.creator;
        }

        return (name, creator);
    }

    private static async Task<string?> GetGameIconUrlAsync(long universeId)
    {
        var url = $"https://thumbnails.roblox.com/v1/games/icons" +
                  $"?universeIds={universeId}&size=512x512&format=Png&isCircular=false";

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var json = await Http.GetStringAsync(url);
            var obj = JObject.Parse(json);
            var data = obj["data"]?[0];
            var imageUrl = data?["imageUrl"]?.Value<string>();
            var state = data?["state"]?.Value<string>();

            if (!string.IsNullOrWhiteSpace(imageUrl))
                return imageUrl;

            if (!string.Equals(state, "Pending", StringComparison.OrdinalIgnoreCase))
                return null;

            await Task.Delay(250 * (attempt + 1));
        }

        return null;
    }
    // 繧ｲ繝ｼ繝繧ｵ繝ｼ繝舌・縺ｮ蝗ｽ繧ｳ繝ｼ繝峨ｒ霑斐☆・井ｾ・ "SG", "US"・・
    public async Task<string?> GetServerCountryCodeAsync(string ip)
    {
        try
        {
            var json = await Http.GetStringAsync($"https://ipinfo.io/{ip}/json");
            return JObject.Parse(json)["country"]?.Value<string>();
        }
        catch { return null; }
    }

    // 繝励Ξ繧､繝､繝ｼ閾ｪ霄ｫ縺ｮ蝗ｽ蜷阪ｒ霑斐☆・井ｾ・ "Japan", "United States"・・
    public async Task<string?> GetMyCountryAsync()
    {
        try
        {
            var json = await Http.GetStringAsync("https://ipinfo.io/json");
            var obj  = JObject.Parse(json);
            // ipinfo 縺ｯ蝗ｽ繧ｳ繝ｼ繝峨＠縺玖ｿ斐＆縺ｪ縺・・縺ｧ country 繝輔ぅ繝ｼ繝ｫ繝峨ｒ縺昴・縺ｾ縺ｾ菴ｿ縺・
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
            var ids  = data.Select(f => f["id"]!.Value<long>()).ToList();
            if (ids.Count == 0) return [];

            // friends endpoint returns empty name/displayName without auth 窶・batch-fetch via users API
            var bodyJson = Newtonsoft.Json.JsonConvert.SerializeObject(
                new { userIds = ids, excludeBannedUsers = false });
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://users.roblox.com/v1/users")
            {
                Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json")
            };
            var resp      = await Http.SendAsync(req);
            var usersData = JObject.Parse(await resp.Content.ReadAsStringAsync())["data"] as JArray ?? [];

            var nameMap = usersData.ToDictionary(
                u => u["id"]!.Value<long>(),
                u =>
                {
                    var dn = u["displayName"]?.Value<string>();
                    if (string.IsNullOrEmpty(dn)) dn = u["name"]?.Value<string>();
                    return string.IsNullOrEmpty(dn) ? "Unknown" : dn;
                });

            return ids.Select(id => new FriendInfo(id, nameMap.GetValueOrDefault(id, "Unknown"))).ToList();
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

    public async Task<string?> GetUserAvatarHeadshotAsync(long userId, bool forceRefresh = false)
    {
        if (!forceRefresh && _avatarCache.TryGetValue(userId, out var cached)) return cached;

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
    /// Bloxstrap 莠呈鋤: gamejoin.roblox.com/v1/join-* API 繧貞他縺ｳ joinScriptUrl 縺ｨ authTicket 繧定ｿ斐☆縲・
    /// gameId 謖・ｮ壹〒迚ｹ螳壹し繝ｼ繝舌・蜿ょ刈縲∥ccessCode 縺ｧ繝励Λ繧､繝吶・繝医し繝ｼ繝舌・蜿ょ刈縲・
    /// </summary>
    public async Task<(string? JoinScriptUrl, string? AuthTicket)> GetJoinInfoAsync(
        string cookie, long placeId, string? gameId = null, string? accessCode = null)
    {
        try
        {
            // CSRF 繝医・繧ｯ繝ｳ蜿門ｾ・
            using var csrf1 = new HttpRequestMessage(HttpMethod.Post,
                "https://auth.roblox.com/v1/authentication-ticket");
            csrf1.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            csrf1.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com");
            csrf1.Content = new StringContent("", Encoding.UTF8, "application/json");
            var r1 = await Http.SendAsync(csrf1);
            if (!r1.Headers.TryGetValues("x-csrf-token", out var toks)) return (null, null);
            var csrf = toks.First();

            // join 繧ｨ繝ｳ繝峨・繧､繝ｳ繝医→繝懊ョ繧｣繧呈ｱｺ螳・
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

    public async Task<List<FriendPresenceDetail>> GetFriendPresenceDetailsAsync(IList<long> userIds, string? cookie = null)
    {
        try
        {
            var body    = new JObject { ["userIds"] = new JArray(userIds.Cast<object>().ToArray()) };
            var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            HttpResponseMessage resp;

            if (string.IsNullOrWhiteSpace(cookie))
            {
                resp = await Http.PostAsync("https://presence.roblox.com/v1/presence/users", content);
            }
            else
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://presence.roblox.com/v1/presence/users")
                {
                    Content = content
                };
                req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
                req.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com");
                resp = await Http.SendAsync(req);
            }

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

    // 笏笏 邨ｱ險医き繝ｼ繝臥畑・亥・髢・API・・笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏

    public async Task<int> GetFriendsCountAsync(long userId)
    {
        try
        {
            var json = await Http.GetStringAsync(
                $"https://friends.roblox.com/v1/users/{userId}/friends/count");
            return JObject.Parse(json)["count"]?.Value<int>() ?? 0;
        }
        catch { return 0; }
    }

    public async Task<int> GetFollowersCountAsync(long userId)
    {
        try
        {
            var json = await Http.GetStringAsync(
                $"https://friends.roblox.com/v1/users/{userId}/followers/count");
            return JObject.Parse(json)["count"]?.Value<int>() ?? 0;
        }
        catch { return 0; }
    }

    public async Task<int> GetFollowingsCountAsync(long userId)
    {
        try
        {
            var json = await Http.GetStringAsync(
                $"https://friends.roblox.com/v1/users/{userId}/followings/count");
            return JObject.Parse(json)["count"]?.Value<int>() ?? 0;
        }
        catch { return 0; }
    }

    // 笏笏 Quick Sign-In・・oblox 蜈ｬ蠑・API・・笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏

    public async Task<(string Code, string PrivateKey)?> CreateQuickSignInAsync()
    {
        try
        {
            var resp = await Http.PostAsync(
                "https://apis.roblox.com/auth-token-service/v1/login/create",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode) return null;
            var obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
            var code = obj["code"]?.Value<string>();
            var key  = obj["privateKey"]?.Value<string>();
            return code != null && key != null ? (code, key) : null;
        }
        catch { return null; }
    }

    public async Task<string> PollQuickSignInStatusAsync(string code, string privateKey, string xsrf)
    {
        try
        {
            var body = Newtonsoft.Json.JsonConvert.SerializeObject(new { code, privateKey });
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://apis.roblox.com/auth-token-service/v1/login/status")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("x-csrf-token", xsrf);
            var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return "Error";
            return JObject.Parse(await resp.Content.ReadAsStringAsync())["status"]?.Value<string>() ?? "Error";
        }
        catch { return "Error"; }
    }

    // 1蝗樒岼縺ｯ XSRF 蜿門ｾ礼畑縺ｫ螟ｱ謨励＆縺帙ｋ縲りｿ泌唆蛟､縺ｯ (xsrfToken, status)
    public async Task<(string Xsrf, string Status)> PollQuickSignInFirstAsync(string code, string privateKey)
    {
        try
        {
            var body = Newtonsoft.Json.JsonConvert.SerializeObject(new { code, privateKey });
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://apis.roblox.com/auth-token-service/v1/login/status")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            var resp = await Http.SendAsync(req);
            var xsrf = resp.Headers.TryGetValues("x-csrf-token", out var vals) ? vals.First() : string.Empty;
            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden) return (xsrf, "Created");
            var status = JObject.Parse(await resp.Content.ReadAsStringAsync())["status"]?.Value<string>() ?? "Error";
            return (xsrf, status);
        }
        catch { return (string.Empty, "Error"); }
    }

    public async Task<string?> AuthenticateWithQuickSignInAsync(string code, string privateKey)
    {
        try
        {
            var body = Newtonsoft.Json.JsonConvert.SerializeObject(new
                { ctype = "AuthToken", cvalue = code, password = privateKey });

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            async Task<HttpResponseMessage> Post(string? csrf = null)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v2/login")
                    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                if (csrf != null) req.Headers.TryAddWithoutValidation("x-csrf-token", csrf);
                return await client.SendAsync(req);
            }

            var resp = await Post();
            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden &&
                resp.Headers.TryGetValues("x-csrf-token", out var tokens))
                resp = await Post(tokens.First());

            if (!resp.IsSuccessStatusCode) return null;

            if (resp.Headers.TryGetValues("Set-Cookie", out var cookies))
                foreach (var c in cookies)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(c, @"\.ROBLOSECURITY=([^;]+)");
                    if (m.Success) return m.Groups[1].Value;
                }
            return null;
        }
        catch { return null; }
    }
}

