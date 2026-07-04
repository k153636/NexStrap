namespace NexStrap.Models;

public class DiscordPartyPreset
{
    public bool Enabled { get; set; } = true;
    public string Label { get; set; } = "New Preset";
    public long PlaceId { get; set; } = 0;
    public int CurrentSize { get; set; } = 1;
    public int MaxSize { get; set; } = 2;
}
