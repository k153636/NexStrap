namespace NexStrap.Services;

public sealed class RobloxPackageManifestService
{
    internal const string DefaultCdnBaseUrl = "https://setup.rbxcdn.com";

    private static readonly HttpClient Http         = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly HttpClient ManifestHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly (string BaseUrl, int DelayMs)[] CdnMirrors =
    [
        ("https://setup.rbxcdn.com",                     0),
        ("https://setup-aws.rbxcdn.com",              2000),
        ("https://setup-ak.rbxcdn.com",               2000),
        ("https://roblox-setup.cachefly.net",         2000),
        ("https://s3.amazonaws.com/setup.roblox.com", 4000),
    ];

    static RobloxPackageManifestService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
        ManifestHttp.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
    }

    internal async Task<string?> TestConnectivityAsync(CancellationToken ct)
    {
        using var cts   = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var       tasks = new List<Task<string>>();

        foreach (var (baseUrl, delayMs) in CdnMirrors)
        {
            var url   = baseUrl;
            var delay = delayMs;
            tasks.Add(Task.Run(async () =>
            {
                if (delay > 0) await Task.Delay(delay, cts.Token);
                await Http.GetAsync($"{url}/version",
                    HttpCompletionOption.ResponseHeadersRead, cts.Token);
                return url;
            }, cts.Token));
        }

        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks);
            tasks.Remove(completed);
            try
            {
                var winner = await completed;
                await cts.CancelAsync();
                return winner;
            }
            catch { }
        }
        return null;
    }

    internal async Task<RobloxPackageManifestResult?> FetchManifestAsync(
        string versionGuid,
        string cdnBaseUrl,
        CancellationToken ct)
    {
        var urls = new[] { cdnBaseUrl }
            .Concat(CdnMirrors.Select(m => m.BaseUrl).Where(u => u != cdnBaseUrl));

        foreach (var baseUrl in urls)
        {
            try
            {
                var text = await ManifestHttp.GetStringAsync(
                    $"{baseUrl}/version-{versionGuid}-rbxPkgManifest.txt", ct);
                var pkgs = ParseManifest(text);
                if (pkgs.Count > 0)
                {
                    if (baseUrl != cdnBaseUrl)
                    {
                        RobloxService.Log($"CDN switched: {cdnBaseUrl} → {baseUrl}");
                    }
                    return new RobloxPackageManifestResult(pkgs, baseUrl);
                }
            }
            catch (Exception ex) { RobloxService.Log($"Manifest fetch failed ({baseUrl}): {ex.Message}"); }
        }
        return null;
    }

    private static List<RobloxPackage> ParseManifest(string text)
    {
        using var reader  = new StringReader(text);
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

            if (name == "RobloxPlayerLauncher.exe") continue;

            long packed = long.TryParse(rawPacked, out var s) ? s : 0;
            result.Add(new RobloxPackage(name, packed, signature));
        }

        if (!result.Any(p => string.Equals(p.Name, "RobloxApp.zip", StringComparison.OrdinalIgnoreCase)))
            RobloxService.Log("Manifest does not contain RobloxApp.zip");

        return result;
    }
}

internal sealed record RobloxPackageManifestResult(List<RobloxPackage> Packages, string CdnBaseUrl);

// Signature = MD5 hash (matches Bloxstrap Package.Signature)
internal record RobloxPackage(string Name, long CompressedSize, string Signature);