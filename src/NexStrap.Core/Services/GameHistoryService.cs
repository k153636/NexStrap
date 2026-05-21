using Newtonsoft.Json;
using NexStrap.Core.Models;

namespace NexStrap.Core.Services;

public class GameHistoryService
{
    private const int MaxEntries = 20;

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
        _entries.RemoveAll(e => e.PlaceId == entry.PlaceId);
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

    private List<GameHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var json = File.ReadAllText(_path);
            return JsonConvert.DeserializeObject<List<GameHistoryEntry>>(json) ?? [];
        }
        catch { return []; }
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonConvert.SerializeObject(_entries, Formatting.Indented)); }
        catch { }
    }
}
