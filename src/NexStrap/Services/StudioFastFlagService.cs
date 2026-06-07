using Newtonsoft.Json;

namespace NexStrap.Services;

public class StudioFastFlagService
{
    private readonly StudioService _studio;
    private Dictionary<string, string> _currentFlags = new();

    public event EventHandler? FlagsChanged;
    public event EventHandler<Dictionary<string, string>>? FlagsHotReloaded;

    public StudioFastFlagService(StudioService studio)
    {
        _studio = studio;
        LoadFlags();
    }

    private string ClientSettingsFile
    {
        get
        {
            var dir = _studio.ClientSettingsPath;
            return string.IsNullOrEmpty(dir) ? string.Empty : Path.Combine(dir, "ClientAppSettings.json");
        }
    }

    public string GetSavePath() => ClientSettingsFile;

    public Dictionary<string, string> GetAll() => new(_currentFlags);

    public void Set(string name, string value) => _currentFlags[name] = value;

    public void Remove(string name) => _currentFlags.Remove(name);

    public async Task SaveAsync()
    {
        var path = ClientSettingsFile;
        if (string.IsNullOrEmpty(path)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonConvert.SerializeObject(_currentFlags, Formatting.Indented);
        await File.WriteAllTextAsync(path, json);
        FlagsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task HotReloadAsync(Dictionary<string, string> newFlags)
    {
        _currentFlags = new(newFlags);
        await SaveAsync();
        FlagsHotReloaded?.Invoke(this, _currentFlags);
    }

    public void ApplyPreset(IEnumerable<Models.FastFlag> presets)
    {
        foreach (var p in presets.Where(p => p.IsEnabled))
            _currentFlags[p.Name] = p.Value;
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
