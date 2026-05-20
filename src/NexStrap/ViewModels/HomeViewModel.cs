using Avalonia.Threading;
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
    private string? _userAvatarUrl;
    private long _joinSequence;
    public string? UserAvatarUrl => _userAvatarUrl;

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
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLaunching = status == RobloxStatus.Launching;
                StatusText  = status switch
                {
                    RobloxStatus.Launching    => "起動しています...",
                    RobloxStatus.Updating     => "アップデート中...",
                    RobloxStatus.NotInstalled => "Roblox が見つかりません",
                    _ => StatusText
                };
            });
        };

        // ユーザーID検出 → アバター URL 取得してキャッシュ
        _logWatcher.UserIdDetected += async (_, userId) =>
        {
            try
            {
                _settings.Update(s => s.CachedRobloxUserId = userId);
                _userAvatarUrl = await _robloxApi.GetUserAvatarHeadshotAsync(userId);
                if (!IsRobloxRunning && !IsLaunching)
                    _discord.SetPagePresence("ホーム", _userAvatarUrl);
            }
            catch { }
        };

        // ゲーム参加 — API でゲーム名・アイコン取得（高速切り替え時の競合を sequence で防ぐ）
        _logWatcher.PlaceJoined += async (_, placeId) =>
        {
            _gameDetected = true;
            CancelFallback();
            var seq = Interlocked.Increment(ref _joinSequence);

            try
            {
                var (name, iconUrl) = await _robloxApi.GetGameInfoAsync(placeId);
                if (Interlocked.Read(ref _joinSequence) != seq) return;

                _discord.SetInGamePresence(name, iconUrl, _userAvatarUrl);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusText      = $"プレイ中: {name}";
                    IsRobloxRunning = true;
                    IsLaunching     = false;
                });
            }
            catch { }
        };

        // ゲーム退出
        _logWatcher.GameLeft += (_, _) =>
        {
            _gameDetected = false;
            _discord.SetPagePresence("ホーム", _userAvatarUrl);
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText      = "準備完了";
                IsRobloxRunning = false;
            });
        };

        // Discord 接続時にアバター付きでプレゼンスを設定
        _discord.ConnectionChanged += (_, connected) =>
        {
            if (connected)
                _discord.SetPagePresence("ホーム", _userAvatarUrl);
        };

        _logWatcher.Start();

        // 前回セッションのユーザーIDが保存済みならアバターを取得
        var cachedUserId = _settings.Settings.CachedRobloxUserId;
        if (cachedUserId > 0)
        {
            _ = Task.Run(async () =>
            {
                _userAvatarUrl = await _robloxApi.GetUserAvatarHeadshotAsync(cachedUserId);
                _discord.SetPagePresence("ホーム", _userAvatarUrl);
            });
        }
    }

    [RelayCommand]
    private async Task LaunchRobloxAsync()
    {
        if (IsLaunching || IsRobloxRunning) return;

        IsLaunching   = true;
        _gameDetected = false;
        StatusText    = "フラグを適用中...";

        // FPS Unlock 設定をフラグに反映
        if (_settings.Settings.FpsUnlockEnabled)
            _fastFlags.Set("DFIntTaskSchedulerTargetFps", _settings.Settings.TargetFps.ToString());
        else
            _fastFlags.Remove("DFIntTaskSchedulerTargetFps");

        await _fastFlags.SaveAsync();
        await _mods.ApplyEnabledModsAsync();

        StatusText = "起動しています...";
        _discord.SetLaunchingPresence(_userAvatarUrl);
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

    // Roblox プロセスを 2 秒ごとにポーリング — 検出次第即座に「プレイ中」へ
    private void StartLaunchFallback()
    {
        CancelFallback();
        _launchFallbackCts = new CancellationTokenSource();
        var token = _launchFallbackCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(90);

                while (!token.IsCancellationRequested && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(2000, token);
                    if (_gameDetected) return; // PlaceJoined が先に処理済み

                    if (RobloxLogWatcher.IsRobloxRunning())
                    {
                        // プロセス確認できた → ホーム画面にいる状態
                        // GameLeft イベントが IsRobloxRunning=false に戻すので、
                        // ここではフラグを立てるだけで監視は LogWatcher に委譲
                        _discord.SetPagePresence("ホーム", _userAvatarUrl);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            StatusText      = "Roblox を起動中";
                            IsRobloxRunning = true;
                            IsLaunching     = false;
                        });
                        return;
                    }
                }

                // タイムアウト — 起動失敗
                if (!token.IsCancellationRequested && !_gameDetected)
                {
                    _discord.SetPagePresence("ホーム", _userAvatarUrl);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsLaunching     = false;
                        IsRobloxRunning = false;
                        StatusText      = "準備完了";
                    });
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
