namespace NexStrap.Services;

public sealed class StudioVersionCleanupService
{
    public void CleanupOldVersionDirectories(string studioVersionsDir, string keepGuid)
    {
        if (!Directory.Exists(studioVersionsDir)) return;
        foreach (var dir in Directory.GetDirectories(studioVersionsDir))
        {
            if (string.Equals(Path.GetFileName(dir), keepGuid, StringComparison.OrdinalIgnoreCase))
                continue;
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
