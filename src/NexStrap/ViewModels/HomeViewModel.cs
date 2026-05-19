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

    private CancellationTokenSource? _launchFallbackCts;
    private bool _gameDetected;

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
        _roblox    = roblox;
        _fastFlags = fastFlags;
        _mods      = mods;
        _settings  = settings;
        _discord   = discord;
        _logWatcher = logWatcher;
        _robloxApi  = robloxApi;

        IsRobloxInstalled = roblox.IsInstalled();
        var versionPath = roblox.RobloxVersionPath;
        if (versionPath != null)
            RobloxVersion = new DirectoryInfo(versionPath).Name;

        roblox.StatusChanged += (_, status) =>
        {
            IsLaunching     = status == RobloxStatus.Launching;
            StatusText = status switch
            {
                RobloxStatus.Launching    => "起動しています...",
                RobloxStatus.Updating     => "アップデート中...",
                RobloxStatus.NotInstalled => "Roblox が見つかりません",
                _ => StatusText  // Running / Idle は logWatcher 側で制御
            };
        };

        // ゲーム参加 — API でゲーム名・アイコン取得
        _logWatcher.PlaceJoined += async (_, placeId) =>
        {
            _gameDetected = true;
            CancelFallback();

            var (name, iconUrl) = await _robloxApi.GetGameInfoAsync(placeId);
            _discord.SetInGamePresence(name, iconUrl);
            StatusText      = $"プレイ中: {name}";
            IsRobloxRunning = true;
            IsLaunching     = false;
        };

        // ゲーム退出
        _logWatcher.GameLeft += (_, _) =>
        {
            _gameDetected = false;
            _discord.SetPagePresence("ホーム");
            StatusText      = "準備完了";
            IsRobloxRunning = false;
        };

        _logWatcher.Start();
    }

    [RelayCommand]
    private async Task LaunchRobloxAsync()
    {
        if (IsLaunching || IsRobloxRunning) return;

        IsLaunching   = true;
        _gameDetected = false;
        StatusText    = "フラグを適用中...";

        await _fastFlags.SaveAsync();
        await _mods.ApplyEnabledModsAsync();

        StatusText = "起動しています...";
        _discord.SetLaunchingPresence();
        await _roblox.LaunchAsync();

        // ログ検知が失敗した場合のフォールバック（40秒後）
        StartLaunchFallback();
    }

    [RelayCommand]
    private async Task LaunchMultipleAsync()
    {
        await _fastFlags.SaveAsync();
        await _roblox.LaunchMultipleInstanceAsync();
        StartLaunchFallback();
    }

    [RelayCommand]
    private async Task HotReloadFlagsAsync()
    {
        var flags = _fastFlags.GetAll();
        await _fastFlags.HotReloadAsync(flags);
        StatusText = "Fast Flags をホットリロードしました";
        await Task.Delay(2000);
        StatusText = IsRobloxRunning ? $"プレイ中" : "準備完了";
    }

    // ログ検知が 40 秒以内に起きなかった場合、プロセス確認で補完
    private void StartLaunchFallback()
    {
        CancelFallback();
        _launchFallbackCts = new CancellationTokenSource();
        var token = _launchFallbackCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(40_000, token);
                if (token.IsCancellationRequested) return;
                if (_gameDetected) return;

                if (RobloxLogWatcher.IsRobloxRunning())
                {
                    _discord.SetInGamePresence("Roblox", null);
                    StatusText      = "Roblox をプレイ中";
                    IsRobloxRunning = true;
                    IsLaunching     = false;
                }
                else
                {
                    // 起動に失敗 or すでに終了
                    IsLaunching     = false;
                    IsRobloxRunning = false;
                    StatusText      = "準備完了";
                    _discord.SetPagePresence("ホーム");
                }
            }
            catch (TaskCanceledException) { }
        });
    }

    private void CancelFallback()
    {
        _launchFallbackCts?.Cancel();
        _launchFallbackCts = null;
    }
}
