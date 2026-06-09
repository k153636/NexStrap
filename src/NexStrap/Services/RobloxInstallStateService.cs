using System.Text.Json;

namespace NexStrap.Services;

internal sealed record RobloxInstallStateFile(string VersionGuid, string VersionPath);

public sealed class RobloxInstallStateService
{
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "roblox-state.json");

    private static readonly string StockRobloxVersionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "Versions");

    private static readonly string VersionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "Versions");

    private string? _currentVersionFolder;

    public static bool IsVersionComplete(string dir) =>
        File.Exists(Path.Combine(dir, "RobloxPlayerBeta.exe"));

    internal RobloxInstallStateFile? LoadState()
    {
        try
        {
            if (!File.Exists(StateFilePath)) return null;
            return JsonSerializer.Deserialize<RobloxInstallStateFile>(File.ReadAllText(StateFilePath));
        }
        catch { return null; }
    }

    internal void SaveState(string guid, string path)
    {
        try { File.WriteAllText(StateFilePath, JsonSerializer.Serialize(new RobloxInstallStateFile(guid, path))); }
        catch { }
    }

    internal string? FindVersionFolder()
    {
        if (_currentVersionFolder != null &&
            Directory.Exists(_currentVersionFolder) &&
            IsVersionComplete(_currentVersionFolder))
            return _currentVersionFolder;

        var state = LoadState();
        if (state != null && IsVersionComplete(state.VersionPath))
        {
            _currentVersionFolder = state.VersionPath;
            return _currentVersionFolder;
        }

        if (Directory.Exists(VersionsDir))
        {
            var found = Directory.GetDirectories(VersionsDir)
                .Where(IsVersionComplete)
                .OrderByDescending(d => new DirectoryInfo(d).LastWriteTime)
                .FirstOrDefault();
            if (found != null)
            {
                _currentVersionFolder = found;
                return _currentVersionFolder;
            }
        }

        _currentVersionFolder = FindStockRobloxVersionFolder();
        return _currentVersionFolder;
    }

    internal string? FindPlayerPath()
    {
        var versionFolder = FindVersionFolder();
        if (versionFolder == null) return null;
        var playerExe = Path.Combine(versionFolder, "RobloxPlayerBeta.exe");
        return File.Exists(playerExe) ? playerExe : null;
    }

    internal void SetCurrentVersionFolder(string versionFolder)
    {
        _currentVersionFolder = versionFolder;
    }

    internal string? FindStockRobloxVersionFolder(string? targetGuid = null)
    {
        if (!Directory.Exists(StockRobloxVersionsDir)) return null;
        if (targetGuid != null)
        {
            var specific = Path.Combine(StockRobloxVersionsDir, $"version-{targetGuid}");
            return Directory.Exists(specific) && IsVersionComplete(specific) ? specific : null;
        }
        return Directory.GetDirectories(StockRobloxVersionsDir)
            .OrderByDescending(d => new DirectoryInfo(d).LastWriteTime)
            .FirstOrDefault(IsVersionComplete);
    }
}
