using Newtonsoft.Json;
using NexStrap.Models;
using System.Xml.Linq;

namespace NexStrap.Services;

public class FastFlagService
{
    private const int UnlockedFramerateCap = 9999;
    private const int DefaultFramerateCap = 240;

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

    public Dictionary<string, string> GetAll() =>
        new Dictionary<string, string>(_currentFlags);

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

    // Keep Roblox's settings XML valid while allowing NexStrap to lift the FPS cap.
    private static void ApplyRobloxFramerateCap(int cap)
    {
        cap = Math.Clamp(cap, 1, UnlockedFramerateCap);
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox", "GlobalBasicSettings_13.xml");
        if (!File.Exists(path)) return;

        try
        {
            var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var properties = doc.Descendants("Properties").FirstOrDefault();
            if (properties == null) return;

            var capElement = properties.Elements("int")
                .FirstOrDefault(e => string.Equals((string?)e.Attribute("name"), "FramerateCap", StringComparison.Ordinal));

            if (capElement == null)
            {
                capElement = new XElement("int", new XAttribute("name", "FramerateCap"), cap);
                properties.AddFirst(capElement);
            }
            else if (int.TryParse(capElement.Value, out var current) && current == cap)
            {
                return;
            }
            else
            {
                capElement.Value = cap.ToString();
            }

            var backupPath = path + ".nexstrap.bak";
            if (!File.Exists(backupPath))
                File.Copy(path, backupPath, overwrite: false);

            doc.Save(path, SaveOptions.DisableFormatting);
        }
        catch (Exception ex)
        {
            RobloxService.Log($"ApplyRobloxFramerateCap failed: {ex.Message}");
        }
    }

    public void ApplyPerformanceSettings(AppSettings settings)
    {
        if (settings.FpsUnlockEnabled)
        {
            Set("DFIntTaskSchedulerTargetFps", UnlockedFramerateCap.ToString());
            Set("FFlagTaskSchedulerLimitTargetFpsTo2402", "False");
            ApplyRobloxFramerateCap(UnlockedFramerateCap);
        }
        else
        {
            Remove("DFIntTaskSchedulerTargetFps");
            Remove("FFlagTaskSchedulerLimitTargetFpsTo2402");
            ApplyRobloxFramerateCap(DefaultFramerateCap);
        }

        if (settings.MultiThreadingEnabled)
        {
            var maxThreads = Math.Min(Environment.ProcessorCount * 2, 64).ToString();
            Set("FIntRuntimeMaxNumOfThreads", maxThreads);
            Set("DFIntTaskSchedulerThreadCount", Environment.ProcessorCount.ToString());
        }
        else
        {
            Remove("FIntRuntimeMaxNumOfThreads");
            Remove("DFIntTaskSchedulerThreadCount");
        }
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
