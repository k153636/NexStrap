using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Models;
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
    private readonly GameHistoryService _history;

    private CancellationTokenSource? _launchFallbackCts;
    private bool      _gameDetected;
    private string?   _userAvatarUrl;
    private string?   _myCountryCode;
    private string?   _currentServerCode;
    private string?   _lastGameName;
    private string?   _lastGameIconUrl;
    private string?   _lastGameCreator;
    private long      _lastPlaceId;
    private DateTime? _launchStartTime;
    private DateTime? _gameStartTime;
    private long      _joinSequence;

    public ObservableCollection<GameEntryViewModel> RecentGames { get; } = [];
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
        RobloxApiService robloxApi,
        GameHistoryService history)
    {
        _roblox     = roblox;
        _fastFlags  = fastFlags;
        _mods       = mods;
        _settings   = settings;
        _discord    = discord;
        _logWatcher = logWatcher;
        _robloxApi  = robloxApi;
        _history    = history;

        foreach (var e in history.Entries)
            RecentGames.Add(new GameEntryViewModel(e));

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

        // サーバーIP検出 → 国コード取得、ゲーム参加済みなら presence を更新
        _logWatcher.ServerIpDetected += async (_, ip) =>
        {
            try
            {
                _currentServerCode = await _robloxApi.GetServerCountryCodeAsync(ip);
                if (_gameDetected && _lastGameName != null)
                    _discord.SetInGamePresence(_lastGameName, _lastGameIconUrl, _userAvatarUrl, FormatServer(), _lastGameCreator);
            }
            catch { }
        };

        // ゲーム参加 — API でゲーム名・アイコン取得（高速切り替え時の競合を sequence で防ぐ）
        _logWatcher.PlaceJoined += async (_, placeId) =>
        {
            _gameDetected      = true;
            _currentServerCode = null;
            _lastPlaceId       = placeId;
            _gameStartTime     = DateTime.UtcNow;
            CancelFallback();
            var seq = Interlocked.Increment(ref _joinSequence);

            try
            {
                var (name, iconUrl, creator) = await _robloxApi.GetGameInfoAsync(placeId);
                if (Interlocked.Read(ref _joinSequence) != seq) return;

                _lastGameName    = name;
                _lastGameIconUrl = iconUrl;
                _lastGameCreator = creator;
                _discord.SetInGamePresence(name, iconUrl, _userAvatarUrl, FormatServer(), creator);

                // 履歴に追加
                var entry = new GameHistoryEntry { PlaceId = placeId, Name = name, IconUrl = iconUrl, PlayedAt = DateTime.Now };
                _history.Add(entry);

                // 起動時間を計測して表示
                var launchMs = _launchStartTime.HasValue
                    ? (DateTime.UtcNow - _launchStartTime.Value).TotalSeconds
                    : (double?)null;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RecentGames.Clear();
                    foreach (var e in _history.Entries) RecentGames.Add(new GameEntryViewModel(e));

                    StatusText      = launchMs.HasValue ? $"起動: {launchMs.Value:F1}秒" : $"プレイ中: {name}";
                    IsRobloxRunning = true;
                    IsLaunching     = false;
                });

                if (launchMs.HasValue)
                {
                    await Task.Delay(3000);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (IsRobloxRunning) StatusText = $"プレイ中: {name}";
                    });
                }
            }
            catch { }
        };

        // ゲーム退出
        _logWatcher.GameLeft += (_, _) =>
        {
            _gameDetected      = false;
            _currentServerCode = null;
            _lastGameName      = null;
            _lastGameIconUrl   = null;
            _lastGameCreator   = null;

            if (_lastPlaceId > 0 && _gameStartTime.HasValue)
            {
                var duration = (int)(DateTime.UtcNow - _gameStartTime.Value).TotalSeconds;
                _history.UpdateDuration(_lastPlaceId, duration);
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RecentGames.Clear();
                    foreach (var e in _history.Entries) RecentGames.Add(new GameEntryViewModel(e));
                });
            }
            _gameStartTime = null;

            _discord.SetPagePresence("ホーム", _userAvatarUrl, "Roblox");
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

        // プレイヤー自身の国コードを起動時に取得してキャッシュ
        _ = Task.Run(async () =>
        {
            _myCountryCode = await _robloxApi.GetMyCountryAsync();
        });
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
        {
            _fastFlags.Set("DFIntTaskSchedulerTargetFps", "9999");
            _fastFlags.Set("FFlagTaskSchedulerLimitTargetFpsTo2402", "False");
        }
        else
        {
            _fastFlags.Remove("DFIntTaskSchedulerTargetFps");
            _fastFlags.Remove("FFlagTaskSchedulerLimitTargetFpsTo2402");
        }

        // マルチスレッド設定をフラグに反映
        if (_settings.Settings.MultiThreadingEnabled)
        {
            _fastFlags.Set("FIntRuntimeMaxNumOfThreads", "2400");
            _fastFlags.Set("DFIntTaskSchedulerThreadCount", Environment.ProcessorCount.ToString());
        }
        else
        {
            _fastFlags.Remove("FIntRuntimeMaxNumOfThreads");
            _fastFlags.Remove("DFIntTaskSchedulerThreadCount");
        }

        await _fastFlags.SaveAsync();
        await _mods.ApplyEnabledModsAsync();

        StatusText       = "起動しています...";
        _launchStartTime = DateTime.UtcNow;
        _discord.SetLaunchingPresence(_userAvatarUrl);
        await _roblox.LaunchAsync();

        // ログ検知が失敗した場合のフォールバック（40秒後）
        StartLaunchFallback();
    }

    [RelayCommand]
    private void RejoinGame(GameEntryViewModel vm)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = $"roblox://experiences/start?placeId={vm.PlaceId}",
                UseShellExecute = true
            });
        }
        catch { }
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
                        _discord.SetPagePresence("ホーム", _userAvatarUrl, "Roblox");
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

    // "JP → US Server │ 12 Flags" / "US Server │ 12 Flags" / null
    private string? FormatServer()
    {
        if (_currentServerCode == null) return null;
        var server = _myCountryCode != null
            ? $"{_myCountryCode} → {_currentServerCode} Server"
            : $"{_currentServerCode} Server";
        var flagCount = _fastFlags.GetAll().Count;
        return $"{server}│{flagCount} Flags";
    }
}
