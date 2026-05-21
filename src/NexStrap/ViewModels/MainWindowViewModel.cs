using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Services;
using NexStrap.Views;

namespace NexStrap.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DiscordRpcService _discord;
    private readonly SettingsService _settings;
    private readonly EnvService _env;
    private readonly PerformanceMonitorService _perfMonitor;
    private PerformanceOverlayWindow? _overlayWindow;

    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private bool _isDiscordConnected;
    [ObservableProperty] private bool _isDiscordAppIdMissing;

    public HomeViewModel HomeVM { get; }
    public FastFlagsViewModel FastFlagsVM { get; }
    public ModsViewModel ModsVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public BrowserViewModel BrowserVM { get; }
    public ThemeViewModel ThemeVM { get; }
    public StatsViewModel StatsVM { get; }
    public DevViewModel DevVM { get; }

    public MainWindowViewModel(
        DiscordRpcService discord,
        SettingsService settings,
        EnvService env,
        PerformanceMonitorService perfMonitor,
        HomeViewModel homeVM,
        FastFlagsViewModel fastFlagsVM,
        ModsViewModel modsVM,
        SettingsViewModel settingsVM,
        BrowserViewModel browserVM,
        ThemeViewModel themeVM,
        StatsViewModel statsVM,
        DevViewModel devVM)
    {
        _discord = discord;
        _settings = settings;
        _env = env;
        _perfMonitor = perfMonitor;

        HomeVM = homeVM;
        FastFlagsVM = fastFlagsVM;
        ModsVM = modsVM;
        SettingsVM = settingsVM;
        BrowserVM = browserVM;
        ThemeVM = themeVM;
        StatsVM = statsVM;
        DevVM = devVM;
        _currentPage = homeVM;

        browserVM.IsGameActive = () => homeVM.IsRobloxRunning;

        var appId = env.Get("DISCORD_APP_ID");
        IsDiscordAppIdMissing = appId == null;

        if (settings.Settings.DiscordRpcEnabled && appId != null)
            discord.Initialize(appId);

        discord.ConnectionChanged += (_, connected) =>
            IsDiscordConnected = connected;

        // 設定変更時に RPC 再初期化 & オーバーレイ制御
        settings.SettingsChanged += (_, s) =>
        {
            var id = env.Get("DISCORD_APP_ID");
            if (s.DiscordRpcEnabled && id != null)
                discord.Initialize(id);
            else if (!s.DiscordRpcEnabled)
                discord.Disable();

            UpdateOverlayVisibility(s.ShowPerformanceOverlay);
        };

        // Roblox 起動/終了でオーバーレイを自動表示/非表示
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

        if (page == "Dev")
        {
            DevVM.Refresh();
            CurrentPage = DevVM;
            _discord.SetDevPresence();
            return;
        }

        CurrentPage = page switch
        {
            "Home"      => HomeVM,
            "FastFlags" => FastFlagsVM,
            "Mods"      => ModsVM,
            "Browser"   => BrowserVM,
            "Theme"     => ThemeVM,
            "Stats"     => StatsVM,
            "Settings"  => SettingsVM,
            _           => HomeVM
        };

        var pageName = page switch
        {
            "Home"      => "ホーム",
            "FastFlags" => "Fast Flags",
            "Mods"      => "Mods",
            "Browser"   => "ブラウザ",
            "Theme"     => "テーマ",
            "Stats"     => "統計",
            "Settings"  => "設定",
            _           => "ホーム"
        };
        // ゲームプレイ中はページ遷移で Discord の in-game 表示を上書きしない
        if (!HomeVM.IsRobloxRunning)
            _discord.SetPagePresence(pageName, HomeVM.UserAvatarUrl);
    }
}
