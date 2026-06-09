using System.Text.Json;

namespace NexStrap.Services;

public sealed class StudioVersionManifestService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly TimeSpan VersionCheckInterval = TimeSpan.FromHours(4);

    private string? _cachedLatestGuid;
    private DateTime _lastVersionCheck = DateTime.MinValue;

    static StudioVersionManifestService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
    }

    public async Task<string?> GetLatestVersionGuidCachedAsync()
    {
        if (_cachedLatestGuid != null && DateTime.UtcNow - _lastVersionCheck < VersionCheckInterval)
            return _cachedLatestGuid;

        var guid = await GetLatestStudioVersionGuidAsync();
        if (guid != null)
        {
            _cachedLatestGuid = guid;
            _lastVersionCheck = DateTime.UtcNow;
        }
        return guid;
    }

    private static async Task<string?> GetLatestStudioVersionGuidAsync()
    {
        foreach (var url in new[]
        {
            "https://clientsettingscdn.roblox.com/v2/client-version/WindowsStudio64",
            "https://clientsettings.roblox.com/v2/client-version/WindowsStudio64",
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
                    RobloxService.Log($"Studio version GUID: {version}");
                    return version;
                }
            }
            catch (Exception ex) { RobloxService.Log($"Studio version fetch failed from {url}: {ex.Message}"); }
        }
        return null;
    }
}
