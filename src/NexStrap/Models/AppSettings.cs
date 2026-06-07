namespace NexStrap.Models;

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
    public bool FpsUnlockEnabled { get; set; } = false;  // 9999 FPS（公式 240 上限を超える）
    public bool MultiThreadingEnabled { get; set; } = false;
    public string BrowserHomepage { get; set; } = "https://www.youtube.com";
    public bool HotReloadEnabled { get; set; } = true;
    public string Language { get; set; } = "ja";
    public long CachedRobloxUserId { get; set; } = 0;
    public bool GlassThemeEnabled { get; set; } = false;
    public string BackgroundImagePath { get; set; } = string.Empty;
    public string BootstrapperImagePath { get; set; } = string.Empty;
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
    public bool DiscordShowServerRegion { get; set; } = true;
    public bool DiscordShowFlagCount { get; set; } = true;
    public bool DiscordPlaceNameLocalized { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public double BackgroundVignetteIntensity { get; set; } = 0.0;
    public double BackgroundVignetteRange { get; set; } = 0.15;
    public string BackgroundVignetteColor { get; set; } = "#000000";

    // Shortcuts
    public string StretchHotKey { get; set; } = "";  // "" = not set, e.g. "Ctrl+F9"

    // Stretched Resolution
    public bool StretchResolutionEnabled { get; set; } = false;
    public bool StretchWarningDismissed  { get; set; } = false;
    public int  StretchResolutionWidth   { get; set; } = 1280;
    public int  StretchResolutionHeight  { get; set; } = 960;

    // Roblox behavior
    public bool MultiInstanceEnabled    { get; set; } = false;
    public bool SuppressCrashHandler    { get; set; } = true;
    public bool CpuAffinityEnabled      { get; set; } = false;
    public int  CpuCoreLimit            { get; set; } = 0;   // 0 = all cores
    public bool MemoryOptimizationEnabled { get; set; } = false;
    public bool CleanupOldVersions      { get; set; } = true;
}
