namespace NexStrap.Services;

public sealed class RobloxAppSettingsService
{
    public Task WriteAppSettingsAsync(string versionDir, CancellationToken ct)
        => File.WriteAllTextAsync(
            Path.Combine(versionDir, "AppSettings.xml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Settings>
            	<ContentFolder>content</ContentFolder>
            	<BaseUrl>http://www.roblox.com</BaseUrl>
            </Settings>
            """, ct);
}
