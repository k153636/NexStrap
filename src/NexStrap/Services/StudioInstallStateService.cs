using System.Text.Json;

namespace NexStrap.Services;

public sealed class StudioInstallStateService
{
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "studio-state.json");

    internal StudioStateFile? LoadState()
    {
        try
        {
            if (!File.Exists(StateFilePath)) return null;
            return JsonSerializer.Deserialize<StudioStateFile>(File.ReadAllText(StateFilePath));
        }
        catch { return null; }
    }

    internal void SaveState(string guid, string path)
    {
        try { File.WriteAllText(StateFilePath, JsonSerializer.Serialize(new StudioStateFile(guid, path))); }
        catch { }
    }
}

internal sealed record StudioStateFile(string VersionGuid, string VersionPath);
