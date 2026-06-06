namespace NexStrap.Models;

public class GameHistoryEntry
{
    public long     PlaceId         { get; set; }
    public long     UniverseId      { get; set; }  // 0 = 不明。同一ゲームの集約キー
    public string   Name            { get; set; } = string.Empty;
    public string?  IconUrl         { get; set; }
    public DateTime PlayedAt        { get; set; }
    public int      DurationSeconds { get; set; }
}
