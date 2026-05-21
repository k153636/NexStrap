namespace NexStrap.Core.Models;

public class GameHistoryEntry
{
    public long     PlaceId         { get; set; }
    public string   Name            { get; set; } = string.Empty;
    public string?  IconUrl         { get; set; }
    public DateTime PlayedAt        { get; set; }
    public int      DurationSeconds { get; set; }
}
