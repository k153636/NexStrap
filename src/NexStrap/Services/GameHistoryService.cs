using Newtonsoft.Json;
using NexStrap.Models;

namespace NexStrap.Services;

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
        BackfillUniverseIds();
    }

    // PlaceId -> UniverseId の補完マップを作成する。
    // 同じPlaceIdに複数のUniverseIdが記録されている場合は最新PlayedAtのものを採用する。
    public static Dictionary<long, long> BuildPlaceIdToUniverseMap(IEnumerable<GameHistoryEntry> entries)
    {
        return entries
            .Where(e => e.PlaceId > 0 && e.UniverseId > 0)
            .GroupBy(e => e.PlaceId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.PlayedAt).First().UniverseId);
    }

    // 履歴エントリのグルーピングキーを解決する。
    // PlaceIdToUniverseMapに該当があればそのUniverseIdを優先し、なければ従来のフォールバックを使う。
    public static long ResolveGroupKey(GameHistoryEntry entry, IReadOnlyDictionary<long, long> placeIdToUniverseMap)
    {
        if (entry.PlaceId > 0 && placeIdToUniverseMap.TryGetValue(entry.PlaceId, out var universeId))
            return universeId;
        return entry.UniverseId != 0 ? entry.UniverseId : entry.PlaceId;
    }

    // UniverseId=0の古いエントリに、同じPlaceIdを持つ他エントリのUniverseIdを補完する。
    private void BackfillUniverseIds()
    {
        var map = BuildPlaceIdToUniverseMap(_entries);
        var changed = false;
        foreach (var e in _entries)
        {
            if (e.UniverseId == 0 && e.PlaceId > 0 && map.TryGetValue(e.PlaceId, out var universeId))
            {
                e.UniverseId = universeId;
                changed = true;
            }
        }
        if (changed) Save();
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

    // 特定セッションエントリを直接更新（テレポートをまたいだ正確な累積時間に使用）
    public void UpdateDuration(GameHistoryEntry entry, int durationSeconds)
    {
        if (!_entries.Contains(entry)) return;
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
