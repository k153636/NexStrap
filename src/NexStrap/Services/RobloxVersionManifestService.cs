using System.Text.Json;

namespace NexStrap.Services;

public sealed class RobloxVersionManifestService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly TimeSpan VersionCheckInterval = TimeSpan.FromHours(4);

    private string?  _cachedLatestGuid;
    private DateTime _lastVersionCheck = DateTime.MinValue;

    public string? CachedGuid => _cachedLatestGuid;

    static RobloxVersionManifestService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
    }

    public async Task<string?> GetLatestVersionGuidCachedAsync()
    {
        if (_cachedLatestGuid != null && DateTime.UtcNow - _lastVersionCheck < VersionCheckInterval)
        {
            RobloxService.Log($"Using cached version GUID: {_cachedLatestGuid}");
            return _cachedLatestGuid;
        }

        var guid = await GetLatestVersionGuidAsync();
        if (guid != null)
        {
            _cachedLatestGuid = guid;
            _lastVersionCheck = DateTime.UtcNow;
        }
        return guid;
    }

    public void UpdateVersionCache(string guid)
    {
        _cachedLatestGuid = guid;
        _lastVersionCheck = DateTime.UtcNow;
    }

    private static async Task<string?> GetLatestVersionGuidAsync()
    {
        foreach (var url in new[]
        {
            "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer",
            "https://clientsettings.roblox.com/v2/client-version/WindowsPlayer"
        })
        {
            try
            {
                var json = await Http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("clientVersionUpload", out var v))
                {
                    var version = v.GetString();
                    if (version == null) continue;
                    if (version.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
                        version = version[8..];
                    RobloxService.Log($"Fetched version GUID: {version} from {url}");
                    return version;
                }
            }
            catch (Exception ex) { RobloxService.Log($"Failed to fetch version GUID from {url}: {ex.Message}"); }
        }
        RobloxService.Log("Failed to fetch version GUID from all sources");
        return null;
    }
}
