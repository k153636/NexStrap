namespace NexStrap.Services;

public sealed class RobloxVersionCleanupService
{
    public void CleanupOldVersionDirectories(string versionsDir, string keepGuid)
    {
        if (!Directory.Exists(versionsDir)) return;
        foreach (var dir in Directory.GetDirectories(versionsDir))
        {
            if (string.Equals(Path.GetFileName(dir), keepGuid, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                RobloxService.Log($"Cleaned up old version: {Path.GetFileName(dir)}");
            }
            catch { }
        }
    }
}
