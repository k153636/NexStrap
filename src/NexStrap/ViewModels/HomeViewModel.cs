using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Models;
using NexStrap.Core.Services;
using NexStrap.Services;

namespace NexStrap.ViewModels;

public enum HomeSortMode { RecentFirst, TotalTime }

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
    private readonly FriendNotificationService _friendNotifications;
    private readonly AccountService _accountService;

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

    private async Task ApplyUserLabelAsync(long userId)
    {
        if (!_settings.Settings.DiscordShowRobloxUsername) return;
        var info = await _robloxApi.GetUserInfoAsync(userId);
        if (info is not { } u) return;
        var label = _settings.Settings.DiscordUseDisplayNameFormat
            ? $"{u.displayName} (@{u.username})"
            : $"@{u.username}";
        _discord.SetUserLabel(label);
    }

    public ObservableCollection<GameEntryViewModel> RecentGames { get; } = [];
    public ObservableCollection<GameEntryViewModel> FavoriteGames { get; } = [];
    public string? UserAvatarUrl => _userAvatarUrl;

    [ObservableProperty] private bool         _isRobloxRunning;
    [ObservableProperty] private bool         _isLaunching;
    [ObservableProperty] private bool         _isRobloxInstalled;
    [ObservableProperty] private string       _statusText    = "Ready";
    [ObservableProperty] private string       _robloxVersion = "Not detected";
    [ObservableProperty] private HomeSortMode _homeSortOrder = HomeSortMode.RecentFirst;
    [ObservableProperty] private bool         _isSortMenuOpen;

    public bool   IsSortRecent    => HomeSortOrder == HomeSortMode.RecentFirst;
    public bool   IsSortTotalTime => HomeSortOrder == HomeSortMode.TotalTime;
    public string SortLabel       => HomeSortOrder == HomeSortMode.RecentFirst ? "Recent First" : "Most Played";

    partial void OnHomeSortOrderChanged(HomeSortMode value)
    {
        OnPropertyChanged(nameof(IsSortRecent));
        OnPropertyChanged(nameof(IsSortTotalTime));
        OnPropertyChanged(nameof(SortLabel));
        RebuildGameLists();
    }

    [RelayCommand] private void ToggleSortMenu() => IsSortMenuOpen = !IsSortMenuOpen;

    [RelayCommand]
    private void SetHomeSort(HomeSortMode mode)
    {
        HomeSortOrder  = mode;
        IsSortMenuOpen = false;
    }

    public HomeViewModel(
        RobloxService roblox,
        FastFlagService fastFlags,
        ModService mods,
        SettingsService settings,
        DiscordRpcService discord,
        RobloxLogWatcher logWatcher,
        RobloxApiService robloxApi,
        GameHistoryService history,
        FriendNotificationService friendNotifications,
        AccountService accountService)
    {
        _roblox               = roblox;
        _fastFlags            = fastFlags;
        _mods                 = mods;
        _settings             = settings;
        _discord              = discord;
        _logWatcher           = logWatcher;
        _robloxApi            = robloxApi;
        _history              = history;
        _friendNotifications  = friendNotifications;
        _accountService       = accountService;

        RebuildGameLists();
        UpdateJumpList();

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
                    RobloxStatus.Launching    => "Launching...",
                    RobloxStatus.Updating     => "Updating...",
                    RobloxStatus.NotInstalled => "Roblox not found",
                    RobloxStatus.Running      => "Roblox running",
                    RobloxStatus.Idle         => "Ready",
                    _ => StatusText
                };

                if (_gameDetected) return;

                if (status == RobloxStatus.Updating)
                {
                    _discord.SetUpdatingPresence(_userAvatarUrl);
                    return;
                }

                if (status == RobloxStatus.Launching)
                {
                    _discord.SetLaunchingPresence(_userAvatarUrl);
                    return;
                }
                if (status == RobloxStatus.Running || status == RobloxStatus.Idle)
                {
                    _discord.SetPagePresence("Home", _userAvatarUrl, "Roblox");
                    return;
                }

                if (status == RobloxStatus.Running || status == RobloxStatus.Idle)
                    _discord.SetPagePresence("Home", _userAvatarUrl, "Roblox");
            });
        };

        // ユーザーID検出 → アバター URL 取得してキャッシュ、フレンド通知開始
        _logWatcher.UserIdDetected += async (_, userId) =>
        {
            try
            {
                _settings.Update(s => s.CachedRobloxUserId = userId);
                _friendNotifications.Start(userId);
                _userAvatarUrl = await _robloxApi.GetUserAvatarHeadshotAsync(userId);
                await ApplyUserLabelAsync(userId);
                if (!IsRobloxRunning && !IsLaunching)
                    _discord.SetPagePresence("Home", _userAvatarUrl);
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
                    _discord.SetInGamePresence(_lastGameName, _lastGameIconUrl, _userAvatarUrl, FormatServer(), _lastGameCreator, _lastPlaceId);
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

            // API完了前に基本プレゼンスを即時表示（API失敗時のフォールバックも兼ねる）
            _discord.SetInGamePresence("Roblox", null, _userAvatarUrl, null, null, placeId);

            try
            {
                var (name, iconUrl, creator) = await _robloxApi.GetGameInfoAsync(placeId);
                if (Interlocked.Read(ref _joinSequence) != seq) return;

                _lastGameName    = name;
                _lastGameIconUrl = iconUrl;
                _lastGameCreator = creator;
                _discord.SetInGamePresence(name, iconUrl, _userAvatarUrl, FormatServer(), creator, placeId);

                // 履歴に追加
                var entry = new GameHistoryEntry { PlaceId = placeId, Name = name, IconUrl = iconUrl, PlayedAt = DateTime.Now };
                _history.Add(entry);

                // 起動時間を計測して表示
                var launchMs = _launchStartTime.HasValue
                    ? (DateTime.UtcNow - _launchStartTime.Value).TotalSeconds
                    : (double?)null;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RebuildGameLists();

                    StatusText      = launchMs.HasValue ? $"Launch: {launchMs.Value:F1}s" : $"Playing: {name}";
                    IsRobloxRunning = true;
                    IsLaunching     = false;
                });

                if (launchMs.HasValue)
                {
                    await Task.Delay(3000);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (IsRobloxRunning) StatusText = $"Playing: {name}";
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
                Dispatcher.UIThread.InvokeAsync(RebuildGameLists);
            }
            _gameStartTime = null;

            _discord.SetPagePresence("Home", _userAvatarUrl, "Roblox");
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText      = "Ready";
                IsRobloxRunning = false;
            });
        };

        // Discord 接続時にアバター付きでプレゼンスを設定（ゲーム中は上書きしない）
        _discord.ConnectionChanged += (_, connected) =>
        {
            if (connected && !_gameDetected)
                _discord.SetPagePresence("Home", _userAvatarUrl);
        };

        _logWatcher.Start();

        // 起動時にすでに Roblox が動いていれば IsRobloxRunning を立てる
        if (RobloxLogWatcher.IsRobloxRunning())
            IsRobloxRunning = true;

        // 前回セッションのユーザーIDが保存済みならアバター取得・フレンド通知開始
        var cachedUserId = _settings.Settings.CachedRobloxUserId;
        if (cachedUserId > 0)
        {
            _friendNotifications.Start(cachedUserId);
            _ = Task.Run(async () =>
            {
                _userAvatarUrl = await _robloxApi.GetUserAvatarHeadshotAsync(cachedUserId);
                await ApplyUserLabelAsync(cachedUserId);
                // ゲームプレイ中・Roblox 起動中は上書きしない
                if (!IsRobloxRunning && !_gameDetected)
                    _discord.SetPagePresence("Home", _userAvatarUrl);
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
        StatusText    = "Applying flags...";

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

        StatusText       = "Launching...";
        _launchStartTime = DateTime.UtcNow;
        _discord.SetLaunchingPresence(_userAvatarUrl);

        string? launchArgs = null;
        var activeCookie = _accountService.GetActiveCookie();
        if (activeCookie != null)
        {
            var ticket = await _robloxApi.GetAuthTicketAsync(activeCookie);
            if (ticket != null)
                launchArgs = $"--launchMode app --authenticationTicket {ticket} --authenticationUrl https://auth.roblox.com";
        }

        var launched = await _roblox.LaunchAsync(launchArgs);
        if (!launched)
        {
            IsLaunching = false;
            IsRobloxRunning = false;
            return;
        }

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
        StatusText = "Fast Flags hot reloaded";
        await Task.Delay(2000);
        StatusText = IsRobloxRunning ? $"Playing" : "Ready";
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
                        _discord.SetPagePresence("Home", _userAvatarUrl, "Roblox");
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            StatusText      = "Launching Roblox";
                            IsRobloxRunning = true;
                            IsLaunching     = false;
                        });
                        return;
                    }
                }

                // タイムアウト — 起動失敗
                if (!token.IsCancellationRequested && !_gameDetected)
                {
                    _discord.SetPagePresence("Home", _userAvatarUrl);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsLaunching     = false;
                        IsRobloxRunning = false;
                        StatusText      = "Ready";
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

    private void RebuildGameLists()
    {
        var favorites = _settings.Settings.FavoriteGameIds.ToHashSet();
        RecentGames.Clear();
        FavoriteGames.Clear();

        var grouped = _history.Entries
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var best  = g.OrderByDescending(e => e.PlayedAt).First();
                var total = g.Sum(e => e.DurationSeconds);
                return (Entry: best, TotalSeconds: total);
            });

        var sorted = HomeSortOrder == HomeSortMode.TotalTime
            ? grouped.OrderByDescending(x => x.TotalSeconds)
            : grouped.OrderByDescending(x => x.Entry.PlayedAt);

        foreach (var (entry, total) in sorted)
        {
            var vm = new GameEntryViewModel(entry)
            {
                IsFavorite            = favorites.Contains(entry.PlaceId),
                DisplayDurationSeconds = total
            };
            RecentGames.Add(vm);
            if (vm.IsFavorite) FavoriteGames.Add(vm);
        }
    }

    [RelayCommand]
    private void ToggleFavorite(GameEntryViewModel vm)
    {
        vm.IsFavorite = !vm.IsFavorite;
        var ids = _settings.Settings.FavoriteGameIds;
        if (vm.IsFavorite)
        {
            if (!ids.Contains(vm.PlaceId)) ids.Add(vm.PlaceId);
            if (!FavoriteGames.Any(f => f.PlaceId == vm.PlaceId))
                FavoriteGames.Insert(0, vm);
        }
        else
        {
            ids.Remove(vm.PlaceId);
            var existing = FavoriteGames.FirstOrDefault(f => f.PlaceId == vm.PlaceId);
            if (existing != null) FavoriteGames.Remove(existing);
        }
        _settings.Update(_ => { });
        UpdateJumpList();
    }

    private void UpdateJumpList()
    {
        try { JumpListService.Update(FavoriteGames.Select(g => (g.PlaceId, g.Name))); }
        catch { }
    }

    // "JP → US Server │ 12 Flags" / "US Server │ 12 Flags" / null
    private string? FormatServer()
    {
        if (_currentServerCode == null) return null;
        var server = _myCountryCode != null
            ? $"{_myCountryCode} → {_currentServerCode} Server"
            : $"{_currentServerCode} Server";
        var flagCount = _fastFlags.GetAll().Count;
        return $"{server} · {flagCount} Flags";
    }
    partial void OnIsRobloxRunningChanged(bool value)
    {
        if (Application.Current is App app)
            app.SetPlayingMode(value);
    }
}
