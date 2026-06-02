using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Services;
using NexStrap.Views;

namespace NexStrap.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DiscordRpcService _discord;
    private readonly SettingsService _settings;
    private readonly PerformanceMonitorService _perfMonitor;
    private PerformanceOverlayWindow? _overlayWindow;

    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private bool _isDiscordConnected;
    [ObservableProperty] private bool _isDiscordAppIdMissing;

    public HomeViewModel HomeVM { get; }
    public FastFlagsViewModel FastFlagsVM { get; }
    public ModsViewModel ModsVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public DiscordViewModel DiscordVM { get; }
    public BrowserViewModel BrowserVM { get; }
    public ThemeViewModel ThemeVM { get; }
    public StatsViewModel StatsVM { get; }
    public DevViewModel DevVM { get; }
    public AccountViewModel AccountVM { get; }
    public FriendsViewModel FriendsVM { get; }
    public StretchResolutionViewModel StretchVM { get; }

    public MainWindowViewModel(
        DiscordRpcService discord,
        SettingsService settings,
        PerformanceMonitorService perfMonitor,
        HomeViewModel homeVM,
        FastFlagsViewModel fastFlagsVM,
        ModsViewModel modsVM,
        SettingsViewModel settingsVM,
        DiscordViewModel discordVM,
        BrowserViewModel browserVM,
        ThemeViewModel themeVM,
        StatsViewModel statsVM,
        DevViewModel devVM,
        AccountViewModel accountVM,
        FriendsViewModel friendsVM,
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
        BrowserVM = browserVM;
        ThemeVM = themeVM;
        StatsVM = statsVM;
        DevVM = devVM;
        AccountVM = accountVM;
        FriendsVM = friendsVM;
        StretchVM = stretchVM;
        _currentPage = homeVM;

        IsDiscordAppIdMissing = false;

        if (settings.Settings.DiscordRpcEnabled)
            discord.Initialize(AppConstants.DiscordAppId);

        discord.ConnectionChanged += (_, connected) =>
            Dispatcher.UIThread.InvokeAsync(() => IsDiscordConnected = connected);

        settings.SettingsChanged += (_, s) =>
        {
            if (s.DiscordRpcEnabled)
                discord.Initialize(AppConstants.DiscordAppId);
            else if (!s.DiscordRpcEnabled)
                discord.Disable();

            // Refresh presence so Discord-related setting changes take effect immediately
            if (s.DiscordRpcEnabled && discord.IsConnected)
                homeVM.RefreshPresence();

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
        if (page == "Browser")
            BrowserVM.UserAvatarUrl = HomeVM.UserAvatarUrl;

        if (page == "Stats")
            StatsVM.Refresh();

        if (page == "Friends")
            _ = FriendsVM.RefreshAsync();

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
            "Browser" => BrowserVM,
            "Theme" => ThemeVM,
            "Stats" => StatsVM,
            "Discord" => DiscordVM,
            "Settings" => SettingsVM,
            "Account" => AccountVM,
            "Friends" => FriendsVM,
            "Stretch" => StretchVM,
            _ => HomeVM
        };

        HomeVM.CurrentPageName = page;

        if (page != "Browser" && !HomeVM.IsGameDetected)
        {
            var presenceName = page == "Stretch" ? "Stretch Res" : page;
            _discord.SetPagePresence(presenceName, HomeVM.UserAvatarUrl);
        }
    }
}
