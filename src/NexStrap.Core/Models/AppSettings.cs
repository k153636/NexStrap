namespace NexStrap.Core.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string AccentColor { get; set; } = "#0078D4";
    public bool DiscordRpcEnabled { get; set; } = true;
    public bool MultiInstanceEnabled { get; set; } = false;
    public bool ShowPerformanceOverlay { get; set; } = false;
    public bool AutoUpdateRoblox { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public string ActiveProfileId { get; set; } = string.Empty;
    public string RobloxInstallPath { get; set; } = string.Empty;
    public int TargetFps { get; set; } = 144;
    public bool FpsUnlockEnabled { get; set; } = false;
    public string BrowserHomepage { get; set; } = "https://www.youtube.com";
    public bool HotReloadEnabled { get; set; } = true;
    public string Language { get; set; } = "ja";
    public long CachedRobloxUserId { get; set; } = 0;
}
