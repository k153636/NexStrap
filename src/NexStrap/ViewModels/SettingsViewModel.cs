using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Models;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly DiscordRpcService _discord;
    private readonly EnvService _env;

    [ObservableProperty] private bool _discordRpcEnabled;
    [ObservableProperty] private bool _discordAppIdConfigured;
    [ObservableProperty] private bool _multiInstanceEnabled;
    [ObservableProperty] private bool _showPerformanceOverlay;
    [ObservableProperty] private bool _autoUpdateRoblox;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _hotReloadEnabled;
    [ObservableProperty] private bool _fpsUnlockEnabled;
    [ObservableProperty] private int _targetFps;
    [ObservableProperty] private string _browserHomepage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public SettingsViewModel(SettingsService settingsService, DiscordRpcService discord, EnvService env)
    {
        _settingsService = settingsService;
        _discord = discord;
        _env = env;

        var s = settingsService.Settings;
        _discordRpcEnabled = s.DiscordRpcEnabled;
        _discordAppIdConfigured = env.Get("DISCORD_APP_ID") != null;
        _multiInstanceEnabled = s.MultiInstanceEnabled;
        _showPerformanceOverlay = s.ShowPerformanceOverlay;
        _autoUpdateRoblox = s.AutoUpdateRoblox;
        _minimizeToTray = s.MinimizeToTray;
        _hotReloadEnabled = s.HotReloadEnabled;
        _fpsUnlockEnabled = s.FpsUnlockEnabled;
        _targetFps = s.TargetFps;
        _browserHomepage = s.BrowserHomepage;
    }

    // ToggleSwitch を変更したら即時保存・反映
    partial void OnDiscordRpcEnabledChanged(bool value)
    {
        _settingsService.Update(s => s.DiscordRpcEnabled = value);

        var appId = _env.Get("DISCORD_APP_ID");
        if (value && appId != null)
            _discord.Initialize(appId);
        else
            _discord.ClearPresence();
    }

    [RelayCommand]
    private void Save()
    {
        _settingsService.Update(s =>
        {
            s.DiscordRpcEnabled = DiscordRpcEnabled;
            s.MultiInstanceEnabled = MultiInstanceEnabled;
            s.ShowPerformanceOverlay = ShowPerformanceOverlay;
            s.AutoUpdateRoblox = AutoUpdateRoblox;
            s.MinimizeToTray = MinimizeToTray;
            s.HotReloadEnabled = HotReloadEnabled;
            s.FpsUnlockEnabled = FpsUnlockEnabled;
            s.TargetFps = TargetFps;
            s.BrowserHomepage = BrowserHomepage;
        });
        StatusMessage = "設定を保存しました";
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = new AppSettings();
        DiscordRpcEnabled = defaults.DiscordRpcEnabled;
        MultiInstanceEnabled = defaults.MultiInstanceEnabled;
        ShowPerformanceOverlay = defaults.ShowPerformanceOverlay;
        AutoUpdateRoblox = defaults.AutoUpdateRoblox;
        MinimizeToTray = defaults.MinimizeToTray;
        HotReloadEnabled = defaults.HotReloadEnabled;
        FpsUnlockEnabled = defaults.FpsUnlockEnabled;
        TargetFps = defaults.TargetFps;
        BrowserHomepage = defaults.BrowserHomepage;
        Save();
    }
}
