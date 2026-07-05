namespace NexStrap.Services;

public sealed record ChromeImportResult(
    string? Cookie,
    long? UserId,
    bool RequiresSecureFallback = false);

public sealed class ChromeImportCoordinator(CookieAccountImportService cookieImport)
{
    public async Task<ChromeImportResult> TryImportAuthenticatedCookieAsync()
    {
        string? cookie = null;
        long?   userId = null;
        var requiresSecureFallback = false;

        for (int attempt = 0; attempt < 3 && userId == null; attempt++)
        {
            if (attempt > 0) await Task.Delay(1200);
            var result = await BrowserCookieImporter.TryImportAsync(BrowserType.Chrome);
            requiresSecureFallback |= result.HasUnsupportedEncryption || result.HasLockedProfile;
            foreach (var candidate in result.Cookies)
            {
                var candidateUserId = await cookieImport.GetAuthenticatedUserIdAsync(candidate);
                if (candidateUserId == null) continue;
                cookie = candidate;
                userId = candidateUserId;
                break;
            }
        }

        return new ChromeImportResult(cookie, userId, requiresSecureFallback);
    }

    public async Task<ChromeImportResult?> WaitForAuthenticatedCookieAsync(CancellationToken cancellationToken)
    {
        string? cookie = null;
        long?   userId = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            try { await Task.Delay(3000, cancellationToken); }
            catch (OperationCanceledException) { return null; }
            var result = await BrowserCookieImporter.TryImportAsync(BrowserType.Chrome);
            if (result.HasUnsupportedEncryption || result.HasLockedProfile)
                return new ChromeImportResult(null, null, true);
            foreach (var candidate in result.Cookies)
            {
                var candidateUserId = await cookieImport.GetAuthenticatedUserIdAsync(candidate);
                if (candidateUserId == null) continue;
                cookie = candidate;
                userId = candidateUserId;
                break;
            }
            if (userId != null) break;
        }

        return new ChromeImportResult(cookie, userId);
    }
}
