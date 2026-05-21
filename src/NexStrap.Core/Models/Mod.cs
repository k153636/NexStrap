namespace NexStrap.Core.Models;

public enum ModType
{
    Texture,
    Sound,
    Font,
    Other
}

public class Mod
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public ModType Type { get; set; } = ModType.Other;
    public bool IsEnabled { get; set; } = true;
    public string FolderPath { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;

    public string TypeLabel => Type switch
    {
        ModType.Texture => "テクスチャ",
        ModType.Sound => "サウンド",
        ModType.Font => "フォント",
        _ => "その他"
    };
}
