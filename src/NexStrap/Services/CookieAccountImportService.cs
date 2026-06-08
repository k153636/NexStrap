using Newtonsoft.Json.Linq;
using NexStrap.Models;

namespace NexStrap.Services;

public sealed class CookieAccountImportService(RobloxApiService robloxApi)
{
    public async Task<long?> GetAuthenticatedUserIdAsync(string cookie)
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

    public async Task<RobloxAccount> CreateAccountAsync(long userId)
    {
        var info      = await robloxApi.GetUserInfoAsync(userId);
        var avatarUrl = await robloxApi.GetUserAvatarHeadshotAsync(userId);
        return new RobloxAccount
        {
            UserId      = userId,
            Username    = info?.username    ?? userId.ToString(),
            DisplayName = info?.displayName ?? userId.ToString(),
            AvatarUrl   = avatarUrl,
        };
    }
}
