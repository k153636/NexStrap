using Newtonsoft.Json;
using NexStrap.Core.Models;

namespace NexStrap.Core.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings = new();

    public AppSettings Settings => _settings;

    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "NexStrap");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
        Load();
    }

    public void Load()
    {
        if (!File.Exists(_settingsPath)) { Save(); return; }
        try
        {
            var json = File.ReadAllText(_settingsPath);
            _settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
        }
        catch { _settings = new AppSettings(); }
    }

    public void Save()
    {
        var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
        File.WriteAllText(_settingsPath, json);
        SettingsChanged?.Invoke(this, _settings);
    }

    public void Update(Action<AppSettings> update)
    {
        update(_settings);
        Save();
    }
}
