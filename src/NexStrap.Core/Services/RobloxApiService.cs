using Newtonsoft.Json.Linq;

namespace NexStrap.Core.Services;

public class RobloxApiService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public async Task<(string name, string? iconUrl)> GetGameInfoAsync(long placeId)
    {
        try
        {
            var universeId = await GetUniverseIdAsync(placeId);
            if (universeId == null) return ("Roblox", null);

            var name = await GetGameNameAsync(universeId.Value);
            var iconUrl = await GetGameIconUrlAsync(universeId.Value);

            return (name ?? "Roblox", iconUrl);
        }
        catch
        {
            return ("Roblox", null);
        }
    }

    private static async Task<long?> GetUniverseIdAsync(long placeId)
    {
        var url = $"https://apis.roblox.com/universes/v1/places/{placeId}/universe";
        var json = await Http.GetStringAsync(url);
        var obj = JObject.Parse(json);
        return obj["universeId"]?.Value<long>();
    }

    private static async Task<string?> GetGameNameAsync(long universeId)
    {
        var url = $"https://games.roblox.com/v1/games?universeIds={universeId}";
        var json = await Http.GetStringAsync(url);
        var obj = JObject.Parse(json);
        return obj["data"]?[0]?["name"]?.Value<string>();
    }

    private static async Task<string?> GetGameIconUrlAsync(long universeId)
    {
        var url = $"https://thumbnails.roblox.com/v1/games/icons" +
                  $"?universeIds={universeId}&size=512x512&format=Png&isCircular=false";
        var json = await Http.GetStringAsync(url);
        var obj = JObject.Parse(json);
        return obj["data"]?[0]?["imageUrl"]?.Value<string>();
    }

    public async Task<string?> GetUserAvatarHeadshotAsync(long userId)
    {
        try
        {
            var url = $"https://thumbnails.roblox.com/v1/users/avatar-headshot" +
                      $"?userIds={userId}&size=150x150&format=Png&isCircular=false";
            var json = await Http.GetStringAsync(url);
            var obj = JObject.Parse(json);
            return obj["data"]?[0]?["imageUrl"]?.Value<string>();
        }
        catch { return null; }
    }
}
