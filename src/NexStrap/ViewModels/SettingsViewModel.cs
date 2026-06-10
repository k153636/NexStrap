using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NexStrap.Models;
using NexStrap.Services;
using NexStrap.Views;

namespace NexStrap.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly GameHistoryService _history;
    private readonly RobloxService _roblox;
    private readonly DiagnosticReportService _diagnosticReport;

    public IStorageProvider? StorageProvider { get; set; }

    [ObservableProperty] private bool _showPerformanceOverlay;
    [ObservableProperty] private bool _autoUpdateRoblox;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _hotReloadEnabled;
    [ObservableProperty] private bool _fpsUnlockEnabled;
    [ObservableProperty] private bool _multiThreadingEnabled;
    [ObservableProperty] private string _browserHomepage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _stretchResolutionEnabled;
    [ObservableProperty] private int  _stretchResolutionWidth;
    [ObservableProperty] private int  _stretchResolutionHeight;
    [ObservableProperty] private bool _multiInstanceEnabled;
    [ObservableProperty] private bool _suppressCrashHandler;
    [ObservableProperty] private bool _cpuAffinityEnabled;
    [ObservableProperty] private int  _cpuCoreLimit;
    [ObservableProperty] private bool _memoryOptimizationEnabled;
    [ObservableProperty] private bool _cleanupOldVersions;
    [ObservableProperty] private string _selectedTab = "General";
    [ObservableProperty] private bool   _isDataLoading;
    [ObservableProperty] private object _currentTabContent = null!;

    public string[] TabNames { get; } = ["General", "Performance", "Roblox", "Data"];

    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly FilePickerFileType JsonFileType = new("JSON") { Patterns = ["*.json"] };

    [RelayCommand]
    private void SelectTab(string tab) => SelectedTab = tab;

    partial void OnSelectedTabChanged(string value)
    {
        CurrentTabContent = value switch
        {
            "Performance" => (object)new SettingsPerformanceTab(this),
            "Roblox"      => new SettingsRobloxTab(this),
            "Data"        => new SettingsDataTab(this),
            _             => new SettingsGeneralTab(this),
        };
    }

    public SettingsViewModel(SettingsService settingsService, GameHistoryService history, RobloxService roblox,
        DiagnosticReportService diagnosticReport)
    {
        _settingsService = settingsService;
        _history = history;
        _roblox = roblox;
        _diagnosticReport = diagnosticReport;
        _currentTabContent = new SettingsGeneralTab(this);

        var s = settingsService.Settings;
        _showPerformanceOverlay = s.ShowPerformanceOverlay;
        _autoUpdateRoblox = s.AutoUpdateRoblox;
        _minimizeToTray = s.MinimizeToTray;
        _hotReloadEnabled = s.HotReloadEnabled;
        _fpsUnlockEnabled = s.FpsUnlockEnabled;
        _multiThreadingEnabled = s.MultiThreadingEnabled;
        _browserHomepage = s.BrowserHomepage;
        _startWithWindows = IsStartupRegistered();
        _stretchResolutionEnabled = s.StretchResolutionEnabled;
        _stretchResolutionWidth   = s.StretchResolutionWidth;
        _stretchResolutionHeight  = s.StretchResolutionHeight;
        _multiInstanceEnabled    = s.MultiInstanceEnabled;
        _suppressCrashHandler    = s.SuppressCrashHandler;
        _cpuAffinityEnabled      = s.CpuAffinityEnabled;
        _cpuCoreLimit            = s.CpuCoreLimit;
        _memoryOptimizationEnabled = s.MemoryOptimizationEnabled;
        _cleanupOldVersions      = s.CleanupOldVersions;
    }

    // 各プロパティ変更時に即座に保存（自動保存）
    private void AutoSave(Action<AppSettings> update) => _settingsService.Update(update);

    partial void OnStartWithWindowsChanged(bool value)     { SetStartupRegistry(value); AutoSave(s => s.StartWithWindows = value); }
    partial void OnShowPerformanceOverlayChanged(bool v)   => AutoSave(s => s.ShowPerformanceOverlay = v);
    partial void OnAutoUpdateRobloxChanged(bool v)         => AutoSave(s => s.AutoUpdateRoblox = v);
    partial void OnMinimizeToTrayChanged(bool v)           => AutoSave(s => s.MinimizeToTray = v);
    partial void OnHotReloadEnabledChanged(bool v)         => AutoSave(s => s.HotReloadEnabled = v);
    partial void OnFpsUnlockEnabledChanged(bool v)         => AutoSave(s => s.FpsUnlockEnabled = v);
    partial void OnMultiThreadingEnabledChanged(bool v)    => AutoSave(s => s.MultiThreadingEnabled = v);
    partial void OnBrowserHomepageChanged(string v)        => AutoSave(s => s.BrowserHomepage = v);
    partial void OnStretchResolutionEnabledChanged(bool v) => AutoSave(s => s.StretchResolutionEnabled = v);
    partial void OnStretchResolutionWidthChanged(int v)    => AutoSave(s => s.StretchResolutionWidth = v);
    partial void OnStretchResolutionHeightChanged(int v)   => AutoSave(s => s.StretchResolutionHeight = v);
    partial void OnMultiInstanceEnabledChanged(bool v)     => AutoSave(s => s.MultiInstanceEnabled = v);
    partial void OnSuppressCrashHandlerChanged(bool v)     => AutoSave(s => s.SuppressCrashHandler = v);
    partial void OnCpuAffinityEnabledChanged(bool v)       => AutoSave(s => s.CpuAffinityEnabled = v);
    partial void OnCpuCoreLimitChanged(int v)              => AutoSave(s => s.CpuCoreLimit = v);
    partial void OnMemoryOptimizationEnabledChanged(bool v)=> AutoSave(s => s.MemoryOptimizationEnabled = v);
    partial void OnCleanupOldVersionsChanged(bool v)       => AutoSave(s => s.CleanupOldVersions = v);

    private static bool IsStartupRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey);
            return key?.GetValue("NexStrap") != null;
        }
        catch { return false; }
    }

    private static void SetStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true);
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

    private void ApplySettings(AppSettings s)
    {
        ShowPerformanceOverlay    = s.ShowPerformanceOverlay;
        AutoUpdateRoblox          = s.AutoUpdateRoblox;
        MinimizeToTray            = s.MinimizeToTray;
        HotReloadEnabled          = s.HotReloadEnabled;
        FpsUnlockEnabled          = s.FpsUnlockEnabled;
        MultiThreadingEnabled     = s.MultiThreadingEnabled;
        BrowserHomepage           = s.BrowserHomepage;
        StretchResolutionEnabled  = s.StretchResolutionEnabled;
        StretchResolutionWidth    = s.StretchResolutionWidth;
        StretchResolutionHeight   = s.StretchResolutionHeight;
        MultiInstanceEnabled      = s.MultiInstanceEnabled;
        SuppressCrashHandler      = s.SuppressCrashHandler;
        CpuAffinityEnabled        = s.CpuAffinityEnabled;
        CpuCoreLimit              = s.CpuCoreLimit;
        MemoryOptimizationEnabled = s.MemoryOptimizationEnabled;
        CleanupOldVersions        = s.CleanupOldVersions;
    }

    [RelayCommand]
    private void ResetToDefaults() => ApplySettings(new AppSettings());

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
            Title             = "Export Settings",
            SuggestedFileName = "settings.json",
            FileTypeChoices   = [JsonFileType]
        });
        if (file == null) return;
        IsDataLoading = true;
        try
        {
            _settingsService.ExportTo(file.Path.LocalPath);
            StatusMessage = "Settings exported";
        }
        catch { StatusMessage = "Export failed"; }
        finally { IsDataLoading = false; }
    }

    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        if (StorageProvider == null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = "Import Settings",
            AllowMultiple  = false,
            FileTypeFilter = [JsonFileType]
        });
        if (files.Count == 0) return;
        IsDataLoading = true;
        try
        {
            _settingsService.ImportFrom(files[0].Path.LocalPath);
            ApplySettings(_settingsService.Settings);
            StatusMessage = "Settings imported";
        }
        catch { StatusMessage = "Import failed"; }
        finally { IsDataLoading = false; }
    }

    [RelayCommand]
    private async Task ExportHistoryAsync()
    {
        if (StorageProvider == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Export Game History",
            SuggestedFileName = "history.json",
            FileTypeChoices   = [JsonFileType]
        });
        if (file == null) return;
        IsDataLoading = true;
        try
        {
            _history.ExportTo(file.Path.LocalPath);
            StatusMessage = "History exported";
        }
        catch { StatusMessage = "Export failed"; }
        finally { IsDataLoading = false; }
    }

    [RelayCommand]
    private async Task ImportHistoryAsync()
    {
        if (StorageProvider == null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = "Import Game History",
            AllowMultiple  = false,
            FileTypeFilter = [JsonFileType]
        });
        if (files.Count == 0) return;
        IsDataLoading = true;
        try
        {
            _history.ImportFrom(files[0].Path.LocalPath);
            StatusMessage = "History imported — restart to see changes";
        }
        catch { StatusMessage = "Import failed"; }
        finally { IsDataLoading = false; }
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

    [RelayCommand]
    private async Task ShowDiagnosticInfoAsync()
    {
        var report = _diagnosticReport.GenerateReport();
        var dialog = new DiagnosticReportDialog(report);

        var mainWin = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWin != null) await dialog.ShowDialog(mainWin);
        else dialog.Show();
    }
}

public sealed record SettingsGeneralTab(SettingsViewModel VM);
public sealed record SettingsPerformanceTab(SettingsViewModel VM);
public sealed record SettingsRobloxTab(SettingsViewModel VM);
public sealed record SettingsDataTab(SettingsViewModel VM);
