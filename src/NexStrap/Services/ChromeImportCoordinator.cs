namespace NexStrap.Services;

public sealed record ChromeImportResult(string? Cookie, long? UserId);

public sealed class ChromeImportCoordinator(CookieAccountImportService cookieImport)
{
    public async Task<ChromeImportResult> TryImportAuthenticatedCookieAsync()
    {
        string? cookie = null;
        long?   userId = null;

        for (int attempt = 0; attempt < 3 && userId == null; attempt++)
        {
            if (attempt > 0) await Task.Delay(1200);
            cookie = await BrowserCookieImporter.TryImportAsync(BrowserType.Chrome);
            if (cookie != null) userId = await cookieImport.GetAuthenticatedUserIdAsync(cookie);
        }

        return new ChromeImportResult(cookie, userId);
    }

    public async Task<ChromeImportResult?> WaitForAuthenticatedCookieAsync(CancellationToken cancellationToken)
    {
        string? cookie = null;
        long?   userId = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            try { await Task.Delay(3000, cancellationToken); }
            catch (OperationCanceledException) { return null; }
            cookie = await BrowserCookieImporter.TryImportAsync(BrowserType.Chrome);
            if (cookie != null) userId = await cookieImport.GetAuthenticatedUserIdAsync(cookie);
            if (userId != null) break;
        }

        return new ChromeImportResult(cookie, userId);
    }
}
