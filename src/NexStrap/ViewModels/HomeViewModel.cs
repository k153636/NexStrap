using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly RobloxService _roblox;
    private readonly FastFlagService _fastFlags;
    private readonly ModService _mods;
    private readonly SettingsService _settings;
    private readonly DiscordRpcService _discord;
    private readonly RobloxLogWatcher _logWatcher;
    private readonly RobloxApiService _robloxApi;

    [ObservableProperty] private bool _isRobloxRunning;
    [ObservableProperty] private bool _isLaunching;
    [ObservableProperty] private bool _isRobloxInstalled;
    [ObservableProperty] private string _statusText = "準備完了";
    [ObservableProperty] private string _robloxVersion = "未検出";

    public HomeViewModel(
        RobloxService roblox,
        FastFlagService fastFlags,
        ModService mods,
        SettingsService settings,
        DiscordRpcService discord,
        RobloxLogWatcher logWatcher,
        RobloxApiService robloxApi)
    {
        _roblox = roblox;
        _fastFlags = fastFlags;
        _mods = mods;
        _settings = settings;
        _discord = discord;
        _logWatcher = logWatcher;
        _robloxApi = robloxApi;

        IsRobloxInstalled = roblox.IsInstalled();
        var versionPath = roblox.RobloxVersionPath;
        if (versionPath != null)
            RobloxVersion = new DirectoryInfo(versionPath).Name;

        roblox.StatusChanged += (_, status) =>
        {
            IsRobloxRunning = status == RobloxStatus.Running;
            IsLaunching = status == RobloxStatus.Launching;
            StatusText = status switch
            {
                RobloxStatus.Running   => "Roblox 起動中",
                RobloxStatus.Launching => "起動しています...",
                RobloxStatus.Updating  => "アップデート中...",
                RobloxStatus.NotInstalled => "Roblox が見つかりません",
                _ => "準備完了"
            };
        };

        // ゲーム参加時：Roblox APIからゲーム名・アイコンを取得してRPCに反映
        _logWatcher.PlaceJoined += async (_, placeId) =>
        {
            var (name, iconUrl) = await _robloxApi.GetGameInfoAsync(placeId);
            _discord.SetInGamePresence(name, iconUrl);
            StatusText = $"プレイ中: {name}";
        };

        // ゲーム退出時：ページ表示に戻す
        _logWatcher.GameLeft += (_, _) =>
        {
            _discord.SetPagePresence("ホーム");
            StatusText = "準備完了";
        };

        _logWatcher.Start();
    }

    [RelayCommand]
    private async Task LaunchRobloxAsync()
    {
        if (IsLaunching || IsRobloxRunning) return;

        IsLaunching = true;
        StatusText = "フラグを適用中...";

        await _fastFlags.SaveAsync();
        await _mods.ApplyEnabledModsAsync();

        StatusText = "Roblox を起動中...";
        _discord.SetLaunchingPresence();
        await _roblox.LaunchAsync();
    }

    [RelayCommand]
    private async Task LaunchMultipleAsync()
    {
        await _fastFlags.SaveAsync();
        await _roblox.LaunchMultipleInstanceAsync();
    }

    [RelayCommand]
    private async Task HotReloadFlagsAsync()
    {
        var flags = _fastFlags.GetAll();
        await _fastFlags.HotReloadAsync(flags);
        StatusText = "Fast Flags をホットリロードしました";
        await Task.Delay(2000);
        StatusText = "準備完了";
    }
}
