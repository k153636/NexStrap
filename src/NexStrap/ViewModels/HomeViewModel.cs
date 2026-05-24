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
    private readonly SmtcService _smtc;

    internal string CurrentPageName { get; set; } = "Home";
    internal bool IsGameDetected => _gameDetected;

    private bool             _gameDetected;
    private string?          _userAvatarUrl;
    private string?          _myCountryCode;
    private string?          _currentServerCode;
    private string?          _lastGameName;
    private string?          _lastGameIconUrl;
    private string?          _lastGameCreator;
    private long             _lastPlaceId;
    private DateTime?        _launchStartTime;
    private DateTime?        _gameStartTime;
    private long             _joinSequence;
    private long             _currentUniverseId;
    private double           _accumulatedDurationSeconds;
    private GameHistoryEntry? _sessionEntry;

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
    [ObservableProperty] private bool         _hasNowPlaying;
    [ObservableProperty] private string       _nowPlayingTitle  = string.Empty;
    [ObservableProperty] private string       _nowPlayingArtist = string.Empty;
    [ObservableProperty] private string       _nowPlayingService = string.Empty;

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
        AccountService accountService,
        SmtcService smtc)
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
        _smtc                 = smtc;

        _smtc.MediaChanged += (_, info) => Dispatcher.UIThread.InvokeAsync(() =>
        {
            NowPlayingTitle   = info.Title;
            NowPlayingArtist  = info.Artist;
            NowPlayingService = info.ServiceName;
            HasNowPlaying     = true;
        });
        _smtc.MediaStopped += (_, _) => Dispatcher.UIThread.InvokeAsync(() =>
        {
            HasNowPlaying = false;
        });

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
                // Updating (install in progress) keeps the launcher in "busy" state
                IsLaunching = status is RobloxStatus.Launching or RobloxStatus.Updating;

                // Sync IsRobloxRunning directly with the process lifecycle
                if (status == RobloxStatus.Running)
                    IsRobloxRunning = true;
                else if (status is RobloxStatus.Idle or RobloxStatus.NotInstalled)
                    IsRobloxRunning = false;

                StatusText = status switch
                {
                    RobloxStatus.Launching    => "Launching...",
                    RobloxStatus.Updating     => "Updating...",
                    RobloxStatus.NotInstalled => "Roblox not found",
                    RobloxStatus.Running      => "Roblox running",
                    RobloxStatus.Idle         => "Ready",
                    _ => StatusText
                };

                if (_gameDetected) return;

                switch (status)
                {
                    case RobloxStatus.Updating:
                        _discord.SetUpdatingPresence(_userAvatarUrl);
                        break;
                    case RobloxStatus.Launching:
                        _discord.SetLaunchingPresence(_userAvatarUrl);
                        break;
                    case RobloxStatus.Running:
                        _discord.SetPagePresence(CurrentPageName, _userAvatarUrl, "Roblox");
                        break;
                    case RobloxStatus.Idle:
                    case RobloxStatus.NotInstalled:
                        _discord.SetPagePresence(CurrentPageName, _userAvatarUrl);
                        break;
                }
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
                    _discord.SetPagePresence(CurrentPageName, _userAvatarUrl);
            }
            catch { }
        };

        // サーバーIP検出 → 国コード取得、ゲーム参加済みなら presence を更新
        _logWatcher.ServerIpDetected += async (_, ip) =>
        {
            try
            {
                _currentServerCode = await _robloxApi.GetServerCountryCodeAsync(ip);
                if (_gameDetected)
                    _discord.SetInGamePresence(_lastGameName ?? "Roblox", _lastGameIconUrl, _userAvatarUrl, FormatState(), _lastGameCreator, _lastPlaceId);
            }
            catch { }
        };

        // ゲーム参加 / テレポート — universeId で同一ゲーム内かを判定
        _logWatcher.PlaceJoined += async (_, args) =>
        {
            var (placeId, universeIdFromLog) = args;

            // await 前に現在の状態をスナップショット（テレポート判定に使う）
            var prevDetected    = _gameDetected;
            var prevUniverseId  = _currentUniverseId;
            var prevStartTime   = _gameStartTime;
            var prevAccumulated = _accumulatedDurationSeconds;

            var seq = Interlocked.Increment(ref _joinSequence);

            // ログから universeid: が取れていれば API 呼び出しをスキップ
            long newUniverseId = universeIdFromLog;
            if (newUniverseId == 0)
            {
                try { newUniverseId = (await _robloxApi.GetUniverseIdAsync(placeId)) ?? 0; } catch { }
            }

            if (Interlocked.Read(ref _joinSequence) != seq) return;

            // 同じ universeId → テレポート（同一ゲーム内の移動）
            bool isTeleport = prevDetected && newUniverseId != 0 && newUniverseId == prevUniverseId;

            if (isTeleport)
            {
                // 前のサブプレイスの経過時間を累積してタイマーをリセット
                var added = prevStartTime.HasValue
                    ? (DateTime.UtcNow - prevStartTime.Value).TotalSeconds
                    : 0.0;
                _accumulatedDurationSeconds = prevAccumulated + added;
                _gameStartTime     = DateTime.UtcNow;
                _lastPlaceId       = placeId;
                _currentServerCode = null;

                // Discord のみ新しいサブプレイス情報で更新
                try
                {
                    var (name, iconUrl, creator) = await _robloxApi.GetGameInfoAsync(placeId, newUniverseId);
                    if (Interlocked.Read(ref _joinSequence) != seq || !_gameDetected) return;
                    _discord.SetInGamePresence(
                        name ?? _lastGameName ?? "Roblox",
                        iconUrl ?? _lastGameIconUrl,
                        _userAvatarUrl,
                        FormatState(),
                        creator ?? _lastGameCreator,
                        placeId);
                }
                catch { }
                return;
            }

            // ── 新しいゲームセッション ─────────────────────────────────────────
            // 前のセッションがあれば累積時間を確定保存
            if (prevDetected && prevStartTime.HasValue && _sessionEntry != null)
            {
                var elapsed = (DateTime.UtcNow - prevStartTime.Value).TotalSeconds;
                _history.UpdateDuration(_sessionEntry, (int)(prevAccumulated + elapsed));
            }

            _accumulatedDurationSeconds = 0;
            _currentServerCode          = null;
            _gameDetected               = true;
            _currentUniverseId          = newUniverseId;
            _lastPlaceId                = placeId;
            _gameStartTime              = DateTime.UtcNow;
            _sessionEntry               = null;
            _lastGameName               = null;
            _lastGameIconUrl            = null;
            _lastGameCreator            = null;
            _discord.ResetGameTimestamp();
            _discord.SetInGamePresence("Roblox", null, _userAvatarUrl, FormatState(), null, placeId);

            try
            {
                var (name, iconUrl, creator) = await _robloxApi.GetGameInfoAsync(placeId, newUniverseId);
                if (Interlocked.Read(ref _joinSequence) != seq || !_gameDetected) return;

                _lastGameName    = name;
                _lastGameIconUrl = iconUrl;
                _lastGameCreator = creator;
                _discord.SetInGamePresence(name, iconUrl, _userAvatarUrl, FormatState(), creator, placeId);

                var entry = new GameHistoryEntry { PlaceId = placeId, Name = name, IconUrl = iconUrl, PlayedAt = DateTime.Now };
                _history.Add(entry);
                _sessionEntry = entry;

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
            // テレポートをまたいだ累積時間 + 最後のサブプレイス経過時間を合計して確定保存
            if (_gameStartTime.HasValue && _sessionEntry != null)
            {
                var elapsed = (DateTime.UtcNow - _gameStartTime.Value).TotalSeconds;
                _history.UpdateDuration(_sessionEntry, (int)(_accumulatedDurationSeconds + elapsed));
                Dispatcher.UIThread.InvokeAsync(RebuildGameLists);
            }

            _gameDetected               = false;
            _currentUniverseId          = 0;
            _accumulatedDurationSeconds = 0;
            _sessionEntry               = null;
            _currentServerCode          = null;
            _lastGameName               = null;
            _lastGameIconUrl            = null;
            _lastGameCreator            = null;
            _gameStartTime              = null;

            _discord.SetPagePresence(CurrentPageName, _userAvatarUrl, "Roblox");
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText = "Ready";
                // Roblox process may still be open (user returned to menu) — only clear if truly exited
                if (!_roblox.IsNexStrapRobloxRunning() && !RobloxLogWatcher.IsRobloxRunning())
                    IsRobloxRunning = false;
            });
        };

        // Discord 再接続時にゲーム中なら in-game presence を復元、それ以外はページ presence
        _discord.ConnectionChanged += (_, connected) =>
        {
            if (!connected) return;
            if (_gameDetected)
                _discord.SetInGamePresence(_lastGameName ?? "Roblox", _lastGameIconUrl, _userAvatarUrl, FormatState(), _lastGameCreator, _lastPlaceId);
            else
                _discord.SetPagePresence(CurrentPageName, _userAvatarUrl);
        };

        _logWatcher.Start();

        // 起動時にすでに NexStrap が起動した Roblox が動いていれば IsRobloxRunning を立てる
        if (_roblox.IsNexStrapRobloxRunning())
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
                    _discord.SetPagePresence(CurrentPageName, _userAvatarUrl);
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

        _fastFlags.ApplyPerformanceSettings(_settings.Settings);
        await _fastFlags.SaveAsync();
        await _mods.ApplyEnabledModsAsync();

        StatusText = "Checking for updates...";
        _launchStartTime = DateTime.UtcNow;

        string? launchArgs = null;
        var activeCookie = _accountService.GetActiveCookie();
        if (activeCookie != null)
        {
            var ticket = await _robloxApi.GetAuthTicketAsync(activeCookie);
            if (ticket != null)
                launchArgs = $"--launchMode app --authenticationTicket {ticket} --authenticationUrl https://auth.roblox.com";
        }

        var launched = await _roblox.LaunchAsync(launchArgs, autoUpdate: _settings.Settings.AutoUpdateRoblox);
        if (!launched)
        {
            IsLaunching     = false;
            IsRobloxRunning = false;
        }
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
        StatusText = _gameDetected && _lastGameName != null ? $"Playing: {_lastGameName}"
                   : IsRobloxRunning                       ? "Playing"
                   :                                         "Ready";
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

    // "JP → US Server · 12 Flags" / "US Server" / "12 Flags" / null
    private string? FormatState()
    {
        var flagCount = _fastFlags.GetAll().Count;
        var flagStr   = flagCount > 0 ? $"{flagCount} Flags" : null;

        if (_currentServerCode == null)
            return flagStr;

        var server = _myCountryCode != null
            ? $"{_myCountryCode} → {_currentServerCode} Server"
            : $"{_currentServerCode} Server";

        return flagStr != null ? $"{server} · {flagStr}" : server;
    }
    partial void OnIsRobloxRunningChanged(bool value)
    {
        if (Application.Current is App app)
            app.SetPlayingMode(value);
    }
}
