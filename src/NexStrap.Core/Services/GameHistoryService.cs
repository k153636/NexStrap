using Newtonsoft.Json;
using NexStrap.Core.Models;

namespace NexStrap.Core.Services;

public class GameHistoryService
{
    private const int MaxEntries = 500;

    private readonly string _path;
    private readonly List<GameHistoryEntry> _entries;

    public IReadOnlyList<GameHistoryEntry> Entries => _entries;

    public GameHistoryService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexStrap");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "history.json");
        _entries = Load();
    }

    public void Add(GameHistoryEntry entry)
    {
        _entries.Insert(0, entry);
        if (_entries.Count > MaxEntries)
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        Save();
    }

    public void UpdateDuration(long placeId, int durationSeconds)
    {
        var entry = _entries.FirstOrDefault(e => e.PlaceId == placeId);
        if (entry == null) return;
        entry.DurationSeconds = durationSeconds;
        Save();
    }

    public void ExportTo(string destPath) => File.Copy(_path, destPath, overwrite: true);

    public void ImportFrom(string srcPath)
    {
        var json = File.ReadAllText(srcPath);
        var loaded = JsonConvert.DeserializeObject<List<GameHistoryEntry>>(json) ?? [];
        _entries.Clear();
        _entries.AddRange(loaded);
        Save();
    }

    private List<GameHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var json = File.ReadAllText(_path);
            var entries = JsonConvert.DeserializeObject<List<GameHistoryEntry>>(json) ?? [];
            File.Copy(_path, _path + ".bak", overwrite: true);
            return entries;
        }
        catch { return []; }
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonConvert.SerializeObject(_entries, Formatting.Indented)); }
        catch { }
    }
}
