using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Models;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;

    [ObservableProperty] private bool _showPerformanceOverlay;
    [ObservableProperty] private bool _autoUpdateRoblox;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _hotReloadEnabled;
    [ObservableProperty] private bool _fpsUnlockEnabled;
    [ObservableProperty] private bool _multiThreadingEnabled;
    [ObservableProperty] private int _targetFps;
    [ObservableProperty] private string _browserHomepage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;

        var s = settingsService.Settings;
        _showPerformanceOverlay = s.ShowPerformanceOverlay;
        _autoUpdateRoblox = s.AutoUpdateRoblox;
        _minimizeToTray = s.MinimizeToTray;
        _hotReloadEnabled = s.HotReloadEnabled;
        _fpsUnlockEnabled = s.FpsUnlockEnabled;
        _multiThreadingEnabled = s.MultiThreadingEnabled;
        _targetFps = s.TargetFps;
        _browserHomepage = s.BrowserHomepage;
    }

    [RelayCommand]
    private void Save()
    {
        _settingsService.Update(s =>
        {
            s.ShowPerformanceOverlay = ShowPerformanceOverlay;
            s.AutoUpdateRoblox = AutoUpdateRoblox;
            s.MinimizeToTray = MinimizeToTray;
            s.HotReloadEnabled = HotReloadEnabled;
            s.FpsUnlockEnabled = FpsUnlockEnabled;
            s.MultiThreadingEnabled = MultiThreadingEnabled;
            s.TargetFps = TargetFps;
            s.BrowserHomepage = BrowserHomepage;
        });
        StatusMessage = "Settings saved";
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = new AppSettings();
        ShowPerformanceOverlay = defaults.ShowPerformanceOverlay;
        AutoUpdateRoblox = defaults.AutoUpdateRoblox;
        MinimizeToTray = defaults.MinimizeToTray;
        HotReloadEnabled = defaults.HotReloadEnabled;
        FpsUnlockEnabled = defaults.FpsUnlockEnabled;
        MultiThreadingEnabled = defaults.MultiThreadingEnabled;
        TargetFps = defaults.TargetFps;
        BrowserHomepage = defaults.BrowserHomepage;
        Save();
    }
}
