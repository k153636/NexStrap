namespace NexStrap.Services;

public sealed class StudioInstallDirectoryService
{
    internal void PrepareInstallDirectories(string versionDir, string downloadsDir)
    {
        if (Directory.Exists(versionDir))
            try { Directory.Delete(versionDir, recursive: true); } catch { }

        Directory.CreateDirectory(versionDir);
        Directory.CreateDirectory(downloadsDir);
    }
}
