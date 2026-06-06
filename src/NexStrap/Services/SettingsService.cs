using Newtonsoft.Json;
using NexStrap.Models;

namespace NexStrap.Services;

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

    public string DataDirectory => Path.GetDirectoryName(_settingsPath)!;

    public void Load()
    {
        if (!File.Exists(_settingsPath)) { WriteFile(); return; }
        try
        {
            var json = File.ReadAllText(_settingsPath);
            _settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            File.Copy(_settingsPath, _settingsPath + ".bak", overwrite: true);
        }
        catch { _settings = new AppSettings(); }
    }

    public void ExportTo(string destPath) => File.Copy(_settingsPath, destPath, overwrite: true);

    public void ImportFrom(string srcPath)
    {
        var json = File.ReadAllText(srcPath);
        _settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
        WriteFile();
        SettingsChanged?.Invoke(this, _settings);
    }

    public void Save()
    {
        WriteFile();
        SettingsChanged?.Invoke(this, _settings);
    }

    private void WriteFile()
    {
        var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
        File.WriteAllText(_settingsPath, json);
    }

    public void Update(Action<AppSettings> update)
    {
        update(_settings);
        Save();
    }
}
