using Newtonsoft.Json;
using NexStrap.Core.Models;

namespace NexStrap.Core.Services;

public class ModService
{
    private readonly RobloxService _robloxService;
    private readonly string _modsDirectory;
    private List<Mod> _mods = new();

    public IReadOnlyList<Mod> Mods => _mods.AsReadOnly();
    public event EventHandler? ModsChanged;

    public ModService(RobloxService robloxService)
    {
        _robloxService = robloxService;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _modsDirectory = Path.Combine(appData, "NexStrap", "Mods");
        Directory.CreateDirectory(_modsDirectory);
        LoadMods();
    }

    private string ModsIndexFile => Path.Combine(_modsDirectory, "mods.json");

    public void LoadMods()
    {
        if (!File.Exists(ModsIndexFile)) { _mods = new(); return; }
        try
        {
            var json = File.ReadAllText(ModsIndexFile);
            _mods = JsonConvert.DeserializeObject<List<Mod>>(json) ?? new();
        }
        catch { _mods = new(); }
    }

    public void SaveMods()
    {
        var json = JsonConvert.SerializeObject(_mods, Formatting.Indented);
        File.WriteAllText(ModsIndexFile, json);
        ModsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<Mod?> ImportModAsync(string sourcePath)
    {
        if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath)) return null;

        var isDir = Directory.Exists(sourcePath);
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var modFolder = Path.Combine(_modsDirectory, name);
        Directory.CreateDirectory(modFolder);

        if (isDir)
            await CopyDirectoryAsync(sourcePath, modFolder);
        else
        {
            var dest = Path.Combine(modFolder, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, dest, true);
        }

        var mod = new Mod
        {
            Name = name,
            FolderPath = modFolder,
            Type = DetectModType(sourcePath)
        };

        _mods.Add(mod);
        SaveMods();
        return mod;
    }

    public async Task ApplyEnabledModsAsync()
    {
        var contentPath = _robloxService.ContentPath;
        if (string.IsNullOrEmpty(contentPath)) return;

        foreach (var mod in _mods.Where(m => m.IsEnabled))
            await ApplyModAsync(mod, contentPath);
    }

    private async Task ApplyModAsync(Mod mod, string contentPath)
    {
        if (!Directory.Exists(mod.FolderPath)) return;
        await CopyDirectoryAsync(mod.FolderPath, contentPath);
    }

    public void ToggleMod(string modId, bool enabled)
    {
        var mod = _mods.FirstOrDefault(m => m.Id == modId);
        if (mod == null) return;
        mod.IsEnabled = enabled;
        SaveMods();
    }

    public void RemoveMod(string modId)
    {
        var mod = _mods.FirstOrDefault(m => m.Id == modId);
        if (mod == null) return;

        if (Directory.Exists(mod.FolderPath))
            Directory.Delete(mod.FolderPath, true);

        _mods.Remove(mod);
        SaveMods();
    }

    private static async Task CopyDirectoryAsync(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destFile = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            await Task.Run(() => File.Copy(file, destFile, true));
        }
    }

    private static ModType DetectModType(string path)
    {
        var ext = Path.GetExtension(path).ToLower();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".dds" => ModType.Texture,
            ".ogg" or ".mp3" or ".wav" => ModType.Sound,
            ".ttf" or ".otf" => ModType.Font,
            ".lua" or ".luau" => ModType.Script,
            _ => ModType.Other
        };
    }
}
