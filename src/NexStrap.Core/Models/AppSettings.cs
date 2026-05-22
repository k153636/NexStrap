namespace NexStrap.Core.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string AccentColor { get; set; } = "#0078D4";
    public bool DiscordRpcEnabled { get; set; } = true;
    public bool ShowPerformanceOverlay { get; set; } = false;
    public bool AutoUpdateRoblox { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public string ActiveProfileId { get; set; } = string.Empty;
    public string RobloxInstallPath { get; set; } = string.Empty;
    public int TargetFps { get; set; } = 144;
    public bool FpsUnlockEnabled { get; set; } = false;
    public bool MultiThreadingEnabled { get; set; } = false;
    public string BrowserHomepage { get; set; } = "https://www.youtube.com";
    public bool HotReloadEnabled { get; set; } = true;
    public string Language { get; set; } = "ja";
    public long CachedRobloxUserId { get; set; } = 0;
    public bool GlassThemeEnabled { get; set; } = false;
    public string BackgroundImagePath { get; set; } = string.Empty;
    public double BackgroundBlurRadius { get; set; } = 18.0;
    public double BackgroundImageOpacity { get; set; } = 0.85;
    public List<long> FavoriteGameIds { get; set; } = [];
    public string GlassAccentColor { get; set; } = "#FFFFFF";
    public double GlassOpacity { get; set; } = 0.75;
    public bool DiscordShowRobloxUsername { get; set; } = false;
    public bool DiscordUseDisplayNameFormat { get; set; } = false;
    public bool DiscordShowCreator { get; set; } = true;
    public bool DiscordShowJoinButton { get; set; } = true;
    public bool DiscordShowLauncherPresence { get; set; } = true;
    public bool DiscordShowLauncherDetails { get; set; } = true;
}
