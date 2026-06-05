using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Services;
using NexStrap.Services;
using NexStrap.Views;

namespace NexStrap.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DiscordRichPresence _discord;
    private readonly SettingsService _settings;
    private readonly PerformanceMonitorService _perfMonitor;
    private PerformanceOverlayWindow? _overlayWindow;

    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private bool _isDiscordConnected;
    [ObservableProperty] private bool _isDiscordAppIdMissing;
    [ObservableProperty] private bool _isOnSettingsPage;

    public HomeViewModel HomeVM { get; }
    public FastFlagsViewModel FastFlagsVM { get; }
    public ModsViewModel ModsVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public DiscordViewModel DiscordVM { get; }
    public ThemeViewModel ThemeVM { get; }
    public StatsViewModel StatsVM { get; }
    public DevViewModel DevVM { get; }
    public AccountViewModel AccountVM { get; }
    public StretchResolutionViewModel StretchVM { get; }

    public MainWindowViewModel(
        DiscordRichPresence discord,
        SettingsService settings,
        PerformanceMonitorService perfMonitor,
        HomeViewModel homeVM,
        FastFlagsViewModel fastFlagsVM,
        ModsViewModel modsVM,
        SettingsViewModel settingsVM,
        DiscordViewModel discordVM,
        ThemeViewModel themeVM,
        StatsViewModel statsVM,
        DevViewModel devVM,
        AccountViewModel accountVM,
        StretchResolutionViewModel stretchVM)
    {
        _discord = discord;
        _settings = settings;
        _perfMonitor = perfMonitor;

        HomeVM = homeVM;
        FastFlagsVM = fastFlagsVM;
        ModsVM = modsVM;
        SettingsVM = settingsVM;
        DiscordVM = discordVM;
        ThemeVM = themeVM;
        StatsVM = statsVM;
        DevVM = devVM;
        AccountVM = accountVM;
        StretchVM = stretchVM;

        accountVM.LaunchAsRequested += () =>
        {
            CurrentPage = HomeVM;
            HomeVM.LaunchRobloxCommand.Execute(null);
        };
        _currentPage = homeVM;

        IsDiscordAppIdMissing = false;

        if (settings.Settings.DiscordRpcEnabled)
            discord.SetDiscordEnabled(true);

        discord.ConnectionChanged += (_, connected) =>
            Dispatcher.UIThread.InvokeAsync(() => IsDiscordConnected = connected);

        settings.SettingsChanged += (_, s) =>
        {
            discord.SetDiscordEnabled(s.DiscordRpcEnabled);
            UpdateOverlayVisibility(s.ShowPerformanceOverlay);
        };

        homeVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.IsRobloxRunning))
                UpdateOverlayVisibility(settings.Settings.ShowPerformanceOverlay && homeVM.IsRobloxRunning);
        };
    }

    private void UpdateOverlayVisibility(bool show)
    {
        if (show)
        {
            if (_overlayWindow == null || !_overlayWindow.IsVisible)
            {
                _perfMonitor.Start();
                _overlayWindow = new PerformanceOverlayWindow(_perfMonitor);
                _overlayWindow.Show();
            }
        }
        else
        {
            _overlayWindow?.Close();
            _overlayWindow = null;
            _perfMonitor.Stop();
        }
    }

    [RelayCommand]
    private void NavigateTo(string page)
    {
        if (page == "Stats")
            StatsVM.Refresh();

        if (page == "Dev")
        {
            DevVM.Refresh();
            CurrentPage = DevVM;
            HomeVM.CurrentPageName = "Dev";
            _discord.SetDevPresence();
            return;
        }

        CurrentPage = page switch
        {
            "Home" => HomeVM,
            "FastFlags" => FastFlagsVM,
            "Mods" => ModsVM,
            "Theme" => ThemeVM,
            "Stats" => StatsVM,
            "Discord" => DiscordVM,
            "Settings" => SettingsVM,
            "Account" => AccountVM,
            "Stretch" => StretchVM,
            _ => HomeVM
        };
        IsOnSettingsPage = page == "Settings";

        HomeVM.CurrentPageName = page == "Stretch" ? "Stretch Res" : page;
        HomeVM.RefreshPresence();
    }

    [RelayCommand]
    private void Shutdown()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }

    [RelayCommand]
    private void RestartApp()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                { UseShellExecute = true });
        Shutdown();
    }
}
