using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DiscordRpcService _discord;
    private readonly SettingsService _settings;
    private readonly EnvService _env;

    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private bool _isDiscordConnected;
    [ObservableProperty] private bool _isDiscordAppIdMissing;

    public HomeViewModel HomeVM { get; }
    public FastFlagsViewModel FastFlagsVM { get; }
    public ModsViewModel ModsVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public BrowserViewModel BrowserVM { get; } = new();

    public MainWindowViewModel(
        DiscordRpcService discord,
        SettingsService settings,
        EnvService env,
        HomeViewModel homeVM,
        FastFlagsViewModel fastFlagsVM,
        ModsViewModel modsVM,
        SettingsViewModel settingsVM)
    {
        _discord = discord;
        _settings = settings;
        _env = env;

        HomeVM = homeVM;
        FastFlagsVM = fastFlagsVM;
        ModsVM = modsVM;
        SettingsVM = settingsVM;
        _currentPage = homeVM;

        var appId = env.Get("DISCORD_APP_ID");
        IsDiscordAppIdMissing = appId == null;

        if (settings.Settings.DiscordRpcEnabled && appId != null)
            discord.Initialize(appId);

        discord.ConnectionChanged += (_, connected) =>
            IsDiscordConnected = connected;

        // 設定変更時に RPC を再初期化
        settings.SettingsChanged += (_, s) =>
        {
            var id = env.Get("DISCORD_APP_ID");
            if (s.DiscordRpcEnabled && id != null)
                discord.Initialize(id);
            else if (!s.DiscordRpcEnabled)
                discord.ClearPresence();
        };
    }

    [RelayCommand]
    private void NavigateTo(string page)
    {
        CurrentPage = page switch
        {
            "Home"      => HomeVM,
            "FastFlags" => FastFlagsVM,
            "Mods"      => ModsVM,
            "Browser"   => BrowserVM,
            "Settings"  => SettingsVM,
            _           => HomeVM
        };

        var pageName = page switch
        {
            "Home"      => "ホーム",
            "FastFlags" => "Fast Flags",
            "Mods"      => "Mods",
            "Browser"   => "ブラウザ",
            "Settings"  => "設定",
            _           => "ホーム"
        };
        _discord.SetPagePresence(pageName);
    }
}
