using System.Diagnostics;
using Newtonsoft.Json;
using NexStrap.Core.Models;

namespace NexStrap.Core.Services;

public class FastFlagService
{
    private readonly RobloxService _robloxService;
    private FileSystemWatcher? _watcher;
    private Dictionary<string, string> _currentFlags = new();

    public event EventHandler? FlagsChanged;
    public event EventHandler<Dictionary<string, string>>? FlagsHotReloaded;

    public FastFlagService(RobloxService robloxService)
    {
        _robloxService = robloxService;
        LoadFlags();
    }

    private string ClientSettingsFile
    {
        get
        {
            var dir = _robloxService.ClientSettingsPath;
            if (string.IsNullOrEmpty(dir)) return string.Empty;
            return Path.Combine(dir, "ClientAppSettings.json");
        }
    }

    public string GetSavePath() => ClientSettingsFile;

    public Dictionary<string, string> GetAll()
    {
        LoadFlags();
        return new Dictionary<string, string>(_currentFlags);
    }

    public void Set(string name, string value)
    {
        _currentFlags[name] = value;
    }

    public void Remove(string name)
    {
        _currentFlags.Remove(name);
    }

    public void SetMany(IEnumerable<FastFlag> flags)
    {
        foreach (var f in flags.Where(f => f.IsEnabled))
            _currentFlags[f.Name] = f.Value;
    }

    public async Task SaveAsync()
    {
        var path = ClientSettingsFile;
        if (string.IsNullOrEmpty(path)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonConvert.SerializeObject(_currentFlags, Formatting.Indented);
        await File.WriteAllTextAsync(path, json);
        FlagsChanged?.Invoke(this, EventArgs.Empty);
    }

    // Hot reload: Roblox が起動中でも flags を書き換えてゲームジョイン時に反映
    public async Task HotReloadAsync(Dictionary<string, string> newFlags)
    {
        _currentFlags = new Dictionary<string, string>(newFlags);
        await SaveAsync();
        FlagsHotReloaded?.Invoke(this, _currentFlags);
    }

    public void StartWatching()
    {
        var dir = _robloxService.ClientSettingsPath;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, "ClientAppSettings.json")
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) =>
        {
            LoadFlags();
            FlagsChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public void ApplyPreset(IEnumerable<FastFlag> presets)
    {
        foreach (var preset in presets.Where(p => p.IsEnabled))
            _currentFlags[preset.Name] = preset.Value;
    }

    private void LoadFlags()
    {
        var path = ClientSettingsFile;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _currentFlags = new();
            return;
        }
        try
        {
            var json = File.ReadAllText(path);
            _currentFlags = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            _currentFlags = new();
        }
    }
}
