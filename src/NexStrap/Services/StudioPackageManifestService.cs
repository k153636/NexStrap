namespace NexStrap.Services;

public sealed class StudioPackageManifestService
{
    private static readonly HttpClient ManifestHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    static StudioPackageManifestService()
    {
        ManifestHttp.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
    }

    internal async Task<StudioPackageManifestResult?> FetchManifestAsync(
        string versionGuid,
        string cdnBaseUrl,
        IEnumerable<string> cdnMirrors,
        CancellationToken ct)
    {
        var urls = new[] { cdnBaseUrl }
            .Concat(cdnMirrors.Where(u => u != cdnBaseUrl));

        foreach (var baseUrl in urls)
        {
            try
            {
                var text = await ManifestHttp.GetStringAsync(
                    $"{baseUrl}/version-{versionGuid}-rbxPkgManifest.txt", ct);
                var pkgs = ParseManifest(text);
                if (pkgs.Count > 0)
                {
                    return new StudioPackageManifestResult(pkgs, baseUrl);
                }
            }
            catch (Exception ex) { RobloxService.Log($"Studio manifest fetch failed ({baseUrl}): {ex.Message}"); }
        }
        return null;
    }

    private static List<RobloxPackage> ParseManifest(string text)
    {
        using var reader = new StringReader(text);
        if (reader.ReadLine() != "v0") return [];

        var result = new List<RobloxPackage>();
        while (true)
        {
            var name      = reader.ReadLine();
            var signature = reader.ReadLine();
            var rawPacked = reader.ReadLine();
            var rawSize   = reader.ReadLine();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(signature) ||
                string.IsNullOrEmpty(rawPacked) || string.IsNullOrEmpty(rawSize))
                break;

            long packed = long.TryParse(rawPacked, out var s) ? s : 0;
            result.Add(new RobloxPackage(name, packed, signature));
        }
        return result;
    }
}

internal sealed record StudioPackageManifestResult(List<RobloxPackage> Packages, string CdnBaseUrl);
