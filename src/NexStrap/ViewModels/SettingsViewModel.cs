using System.Diagnostics;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NexStrap.Core.Models;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly GameHistoryService _history;
    private readonly RobloxService _roblox;

    public IStorageProvider? StorageProvider { get; set; }

    [ObservableProperty] private bool _showPerformanceOverlay;
    [ObservableProperty] private bool _autoUpdateRoblox;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _hotReloadEnabled;
    [ObservableProperty] private bool _fpsUnlockEnabled;
    [ObservableProperty] private bool _multiThreadingEnabled;
    [ObservableProperty] private int _targetFps;
    [ObservableProperty] private string _browserHomepage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _startWithWindows;

    public SettingsViewModel(SettingsService settingsService, GameHistoryService history, RobloxService roblox)
    {
        _settingsService = settingsService;
        _history = history;
        _roblox = roblox;

        var s = settingsService.Settings;
        _showPerformanceOverlay = s.ShowPerformanceOverlay;
        _autoUpdateRoblox = s.AutoUpdateRoblox;
        _minimizeToTray = s.MinimizeToTray;
        _hotReloadEnabled = s.HotReloadEnabled;
        _fpsUnlockEnabled = s.FpsUnlockEnabled;
        _multiThreadingEnabled = s.MultiThreadingEnabled;
        _targetFps = s.TargetFps;
        _browserHomepage = s.BrowserHomepage;
        _startWithWindows = IsStartupRegistered();
    }

    partial void OnStartWithWindowsChanged(bool value) => SetStartupRegistry(value);

    private static bool IsStartupRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("NexStrap") != null;
        }
        catch { return false; }
    }

    private static void SetStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            if (enable)
            {
                var exe = Environment.ProcessPath ?? string.Empty;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue("NexStrap", $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue("NexStrap", throwOnMissingValue: false);
            }
        }
        catch { }
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
            s.StartWithWindows = StartWithWindows;
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

    [RelayCommand]
    private void OpenDataFolder()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = _settingsService.DataDirectory,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task ExportSettingsAsync()
    {
        if (StorageProvider == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title           = "Export Settings",
            SuggestedFileName = "settings.json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });
        if (file == null) return;
        try
        {
            _settingsService.ExportTo(file.Path.LocalPath);
            StatusMessage = "Settings exported";
        }
        catch { StatusMessage = "Export failed"; }
    }

    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        if (StorageProvider == null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title           = "Import Settings",
            AllowMultiple   = false,
            FileTypeFilter  = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });
        if (files.Count == 0) return;
        try
        {
            _settingsService.ImportFrom(files[0].Path.LocalPath);
            var s = _settingsService.Settings;
            ShowPerformanceOverlay = s.ShowPerformanceOverlay;
            AutoUpdateRoblox       = s.AutoUpdateRoblox;
            MinimizeToTray         = s.MinimizeToTray;
            HotReloadEnabled       = s.HotReloadEnabled;
            FpsUnlockEnabled       = s.FpsUnlockEnabled;
            MultiThreadingEnabled  = s.MultiThreadingEnabled;
            TargetFps              = s.TargetFps;
            BrowserHomepage        = s.BrowserHomepage;
            StatusMessage = "Settings imported";
        }
        catch { StatusMessage = "Import failed"; }
    }

    [RelayCommand]
    private async Task ExportHistoryAsync()
    {
        if (StorageProvider == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Export Game History",
            SuggestedFileName = "history.json",
            FileTypeChoices   = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });
        if (file == null) return;
        try
        {
            _history.ExportTo(file.Path.LocalPath);
            StatusMessage = "History exported";
        }
        catch { StatusMessage = "Export failed"; }
    }

    [RelayCommand]
    private async Task ImportHistoryAsync()
    {
        if (StorageProvider == null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = "Import Game History",
            AllowMultiple  = false,
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });
        if (files.Count == 0) return;
        try
        {
            _history.ImportFrom(files[0].Path.LocalPath);
            StatusMessage = "History imported — restart to see changes";
        }
        catch { StatusMessage = "Import failed"; }
    }

    [RelayCommand]
    private async Task UninstallNexStrapRobloxAsync()
    {
        try
        {
            await _roblox.UninstallNexStrapRobloxAsync();
            StatusMessage = "NexStrap Roblox uninstalled";
        }
        catch { StatusMessage = "Uninstall failed"; }
    }

    [RelayCommand]
    private async Task UninstallStockRobloxAsync()
    {
        try
        {
            await _roblox.UninstallStockRobloxAsync();
            StatusMessage = "Stock Roblox uninstalled";
        }
        catch { StatusMessage = "Uninstall failed"; }
    }
}
