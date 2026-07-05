using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Models;
using NexStrap.Services;

namespace NexStrap.ViewModels;

public enum HomeSortMode { RecentFirst, TotalTime }

public partial class HomeViewModel : ViewModelBase
{
    // ── サービス ──────────────────────────────────────────────────────────
    private readonly RobloxService             _roblox;
    private readonly StudioService             _studio;
    private readonly FastFlagService           _fastFlags;
    private readonly StudioFastFlagService     _studioFastFlags;
    private readonly ModService                _mods;
    private readonly SettingsService           _settings;
    private readonly DiscordRichPresence       _presence;
    private readonly RobloxLogWatcher          _logWatcher;
    private readonly RobloxApiService          _robloxApi;
    private readonly GameHistoryService        _history;
    private readonly FriendNotificationService                  _friendNotifications;
    private readonly AccountService                             _accountService;
    private readonly StudioRpcServer                      _studioRpcServer;

    // ── ゲームセッション履歴 ──────────────────────────────────────────────
    private string?           _userAvatarUrl;
    private DateTime?         _launchStartTime;
    private DateTime?         _gameStartTime;
    private double            _accumulatedDurationSeconds;
    private GameHistoryEntry? _sessionEntry;
    private readonly object   _userRefreshLock = new();
    private readonly Dictionary<int, long> _slotUserIds = new();
    private readonly Dictionary<int, Task> _slotUserRefreshes = new();
    private readonly Dictionary<int, Task> _slotPlaceJoins = new();
    private readonly Dictionary<int, long> _slotGenerations = new();

    // ── フォーカス・Stretch Res ────────────────────────────────────────────
    private bool _robloxHasFocus;
    private Timer?                         _focusTimer;
    private int?                           _lastDiscordFocusedSlot = null;

    private const int FocusTimerInterval   = 500;
    private const int RestartDelay         = 1_500;
    private const int HotReloadStatusDelay = 2_000;
    private const int LaunchStatusDelay    = 3_000;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint   GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    // ── 公開プロパティ ────────────────────────────────────────────────────
    internal string CurrentPageName
    {
        get => _presence.CurrentPageName;
        set => _presence.SetCurrentPage(value);
    }

    internal bool    IsGameDetected => _presence.GameDetected;
    public   string? UserAvatarUrl  => _userAvatarUrl;

    [ObservableProperty] private string?      _userDisplayName;
    [ObservableProperty] private bool         _isRobloxRunning;
    [ObservableProperty] private bool         _isLaunching;
    [ObservableProperty] private bool         _isStudioLaunching;
    [ObservableProperty] private bool         _isRobloxInstalled;
    [ObservableProperty] private string       _statusText    = "Ready";
    [ObservableProperty] private string       _robloxVersion = "Not detected";
    [ObservableProperty] private HomeSortMode _homeSortOrder = HomeSortMode.RecentFirst;
    [ObservableProperty] private bool         _isSortMenuOpen;

    public static string AppVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            return v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v1.1";
        }
    }

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
    private void SetHomeSort(HomeSortMode mode) { HomeSortOrder = mode; IsSortMenuOpen = false; }

    public ObservableCollection<GameEntryViewModel> RecentGames   { get; } = [];
    public ObservableCollection<GameEntryViewModel> FavoriteGames { get; } = [];

    private static IEnumerable<Process> GetRobloxProcesses() =>
        Process.GetProcessesByName("RobloxPlayerBeta")
               .Concat(Process.GetProcessesByName("RobloxPlayer"));

    // ══════════════════════════════════════════════════════════════════════
    // コンストラクタ
    // ══════════════════════════════════════════════════════════════════════

    public HomeViewModel(
        RobloxService roblox, StudioService studio, FastFlagService fastFlags,
        StudioFastFlagService studioFastFlags, ModService mods, SettingsService settings,
        DiscordRichPresence presence, RobloxLogWatcher logWatcher, RobloxApiService robloxApi,
        GameHistoryService history, FriendNotificationService friendNotifications,
        AccountService accountService, StudioRpcServer studioRpcServer)
    {
        _roblox              = roblox;
        _studio              = studio;
        _fastFlags           = fastFlags;
        _studioFastFlags     = studioFastFlags;
        _mods                = mods;
        _settings            = settings;
        _presence            = presence;
        _logWatcher          = logWatcher;
        _robloxApi           = robloxApi;
        _history             = history;
        _friendNotifications = friendNotifications;
        _accountService      = accountService;

        // Studio RPC サーバー起動（プラグインからのデータを受信）
        _studioRpcServer = studioRpcServer;
        _studioRpcServer.MessageReceived += (_, msg) =>
        {
            if (msg.Data == null) return;
            if (msg.Command == "Initialize")
                _presence.EnqueueStudioInitialized(msg.Data);
            else if (msg.Command == "SetRichPresence")
                _presence.EnqueueStudioRpcMessage(msg.Data);
        };
        _studioRpcServer.Start();

        roblox.PreLaunchAsync = async () =>
        {
            _fastFlags.ApplyPerformanceSettings(_settings.Settings);
            await _fastFlags.SaveAsync();
            await _mods.ApplyEnabledModsAsync();
        };

        RebuildGameLists();
        UpdateJumpList();
        IsRobloxInstalled = roblox.IsInstalled();
        var versionPath = roblox.RobloxVersionPath;
        if (versionPath != null) RobloxVersion = new DirectoryInfo(versionPath).Name;

        // ── Roblox 起動状態 ────────────────────────────────────────────────
        roblox.StatusChanged += (_, status) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLaunching = status is RobloxStatus.Launching or RobloxStatus.Updating;

                if (status == RobloxStatus.Running)        IsRobloxRunning = true;
                else if (status is RobloxStatus.Idle
                              or RobloxStatus.NotInstalled) IsRobloxRunning = false;

                StatusText = status switch
                {
                    RobloxStatus.Launching    => "Launching...",
                    RobloxStatus.Updating     => "Updating...",
                    RobloxStatus.NotInstalled => "Roblox not found",
                    RobloxStatus.Running      => "Roblox running",
                    RobloxStatus.Idle         => "Ready",
                    _ => StatusText
                };

                UpdateRobloxPresence(status);
            });
        };

        // ── ユーザーID検出 ────────────────────────────────────────────────
        _logWatcher.UserIdDetected += (_, e) =>
        {
            _settings.Update(s => s.CachedRobloxUserId = e.UserId);
            _friendNotifications.Start(e.UserId);

            lock (_userRefreshLock)
            {
                _slotUserIds[e.Slot] = e.UserId;
                var generation = _slotGenerations.GetValueOrDefault(e.Slot);
                var refresh = RefreshRobloxAccountAsync(e.Slot, e.UserId, generation);
                _slotUserRefreshes[e.Slot] = refresh;
            }
        };

        // ── サーバーIP検出 ─────────────────────────────────────────────────
        _logWatcher.ServerIpDetected += async (_, e) =>
        {
            try
            {
                var code = await _robloxApi.GetServerCountryCodeAsync(e.Ip);
                for (int i = 0; i < 6; i++)
                {
                    if (_presence.GameDetected) { _presence.EnqueueServerCode(e.Slot, code); return; }
                    await Task.Delay(500);
                }
            }
            catch { }
        };

        // ── ゲーム参加 ─────────────────────────────────────────────────────
        _logWatcher.PlaceJoined += (_, args) =>
        {
            if (_logWatcher.IsWatchingStudioLog) return;

            lock (_userRefreshLock)
            {
                var previous = _slotPlaceJoins.GetValueOrDefault(args.Slot, Task.CompletedTask);
                var generation = _slotGenerations.GetValueOrDefault(args.Slot);
                _slotPlaceJoins[args.Slot] = ProcessPlaceJoinedAsync(args, previous, generation);
            }
        };

        // ── DiscordRichPresence → HomeViewModel イベント ──────────────────
        _presence.SessionEnded += (_, _) =>
        {
            if (_gameStartTime.HasValue && _sessionEntry != null)
            {
                var elapsed = (DateTime.UtcNow - _gameStartTime.Value).TotalSeconds;
                _history.UpdateDuration(_sessionEntry, (int)(_accumulatedDurationSeconds + elapsed));
            }
            _accumulatedDurationSeconds = 0;
            _sessionEntry               = null;
        };

        _presence.TeleportOccurred += (_, _) =>
        {
            var added = _gameStartTime.HasValue
                ? (DateTime.UtcNow - _gameStartTime.Value).TotalSeconds : 0.0;
            _accumulatedDurationSeconds += added;
            _gameStartTime               = DateTime.UtcNow;
        };

        _presence.GameInfoFetched += async (_, args) =>
        {
            _logWatcher.ConfirmGame(args.Slot);
            _gameStartTime = args.StartedAt;

            var entry = new GameHistoryEntry
            {
                PlaceId    = args.PlaceId,
                UniverseId = args.UniverseId,
                Name       = args.Name,
                IconUrl    = args.IconUrl,
                PlayedAt   = args.PlayedAt
            };
            _history.Add(entry);
            _sessionEntry = entry;

            var launchMs = _launchStartTime.HasValue
                ? (DateTime.UtcNow - _launchStartTime.Value).TotalSeconds : (double?)null;
            _launchStartTime = null;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RebuildGameLists();
                StatusText      = launchMs.HasValue ? $"Launch: {launchMs.Value:F1}s" : $"Playing: {args.Name}";
                IsRobloxRunning = true;
                IsLaunching     = false;
            });

            if (launchMs.HasValue)
            {
                await Task.Delay(LaunchStatusDelay);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsRobloxRunning) StatusText = $"Playing: {args.Name}";
                });
            }
        };

        // ── Studio テストプレイ ────────────────────────────────────────────
        _logWatcher.StudioPlaySoloStarted += (_, _) => _presence.EnqueueStudioPlaytestStarted();
        _logWatcher.StudioPlaySoloStopped += (_, _) => _presence.EnqueueStudioPlaytestStopped();

        // ── ゲーム退出 ─────────────────────────────────────────────────────
        _logWatcher.GameLeft += (_, e) =>
        {
            lock (_userRefreshLock)
            {
                _slotUserIds.Remove(e.Slot);
                _slotUserRefreshes.Remove(e.Slot);
                _slotPlaceJoins.Remove(e.Slot);
                _slotGenerations[e.Slot] = _slotGenerations.GetValueOrDefault(e.Slot) + 1;
            }

            if (_gameStartTime.HasValue && _sessionEntry != null)
            {
                var elapsed = (DateTime.UtcNow - _gameStartTime.Value).TotalSeconds;
                _history.UpdateDuration(_sessionEntry, (int)(_accumulatedDurationSeconds + elapsed));
                Dispatcher.UIThread.InvokeAsync(RebuildGameLists);
            }
            _accumulatedDurationSeconds = 0;
            _sessionEntry               = null;
            _gameStartTime              = null;

            var robloxCount = RobloxLogWatcher.IsRobloxRunning() || _roblox.IsNexStrapRobloxRunning()
                ? GetRobloxProcesses().Count() : 0;
            _presence.EnqueueGameLeft(e.Slot, robloxCount);

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText = "Ready";
                if (!_roblox.IsNexStrapRobloxRunning() && !RobloxLogWatcher.IsRobloxRunning())
                    IsRobloxRunning = false;
            });
        };

        // ── Discord 再接続 ─────────────────────────────────────────────────
        _presence.ConnectionChanged += (_, connected) =>
        {
            if (connected) _presence.EnqueueRefresh();
        };

        _logWatcher.Start();

        // ── フォーカスタイマー ────────────────────────────────────────────
        _focusTimer = new Timer(_ =>
        {
            try
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out uint focusPid);
                if (focusPid == 0) return;

                int? matched = _logWatcher.TryGetSlotForPid(focusPid, out int focusSlot)
                    ? focusSlot : (int?)null;

                bool nowFocused = matched != null;
                if (nowFocused != _robloxHasFocus && IsRobloxRunning)
                {
                    _robloxHasFocus = nowFocused;
                    var s = _settings.Settings;
                    if (s.StretchResolutionEnabled)
                    {
                        if (nowFocused) _roblox.ApplyStretchResolution(s.StretchResolutionWidth, s.StretchResolutionHeight);
                        else            _roblox.RestoreResolution();
                    }
                }

                if (matched != _lastDiscordFocusedSlot)
                {
                    _lastDiscordFocusedSlot = matched;
                    _presence.EnqueueFocusChanged(matched);
                }
            }
            catch { }
        }, null, FocusTimerInterval, FocusTimerInterval);

        // ── Studio 状態 ───────────────────────────────────────────────────
        _studio.StatusChanged += (_, status) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateStudioPresence(status);
            });
        };

        // ── 起動時 ────────────────────────────────────────────────────────
        if (_roblox.IsNexStrapRobloxRunning())
        {
            _presence.EnqueueRobloxChanged(true);
            IsRobloxRunning = true;
        }

        var activeAccount = _accountService.Accounts.FirstOrDefault(a => a.IsActive)
                         ?? _accountService.Accounts.FirstOrDefault();
        var startupUserId = activeAccount?.UserId > 0
            ? activeAccount.UserId : _settings.Settings.CachedRobloxUserId;

        if (startupUserId > 0)
        {
            if (activeAccount?.AvatarUrl != null)
            {
                _userAvatarUrl = activeAccount.AvatarUrl;
                _presence.SetUserAvatar(activeAccount.AvatarUrl);
            }
            if (!string.IsNullOrEmpty(activeAccount?.DisplayName))
                UserDisplayName = activeAccount.DisplayName;
            else if (!string.IsNullOrEmpty(activeAccount?.Username))
                UserDisplayName = activeAccount.Username;

            _friendNotifications.Start(startupUserId);

            _ = Task.Run(async () =>
            {
                _userAvatarUrl = await _robloxApi.GetUserAvatarHeadshotAsync(startupUserId);
                _presence.SetUserAvatar(_userAvatarUrl);
                await ApplyUserLabelAsync(startupUserId);
                _presence.EnqueueRefresh();
            });
        }

    }

    // ══════════════════════════════════════════════════════════════════════
    // 公開 API
    // ══════════════════════════════════════════════════════════════════════

    public void RefreshPresence() => _presence.EnqueueRefresh();

    private async Task RefreshRobloxAccountAsync(int slot, long userId, long generation)
    {
        try
        {
            var avatarTask = _robloxApi.GetUserAvatarHeadshotAsync(userId, forceRefresh: true);
            var userInfoTask = _robloxApi.GetUserInfoAsync(userId);
            await Task.WhenAll(avatarTask, userInfoTask);
            var avatarUrl = await avatarTask;
            var userInfo = await userInfoTask;

            lock (_userRefreshLock)
            {
                if (_slotGenerations.GetValueOrDefault(slot) != generation
                    || !_slotUserIds.TryGetValue(slot, out var currentUserId)
                    || currentUserId != userId)
                    return;
            }

            if (slot == 0)
            {
                _userAvatarUrl = avatarUrl;
                _presence.SetUserAvatar(avatarUrl);
            }

            string? label = null;
            if (userInfo is { } u)
            {
                if (slot == 0)
                    UserDisplayName = string.IsNullOrEmpty(u.displayName) ? u.username : u.displayName;
                if (_settings.Settings.DiscordShowRobloxUsername)
                    label = _settings.Settings.DiscordUseDisplayNameFormat
                        ? $"{u.displayName} (@{u.username})" : $"@{u.username}";
            }

            if (slot == 0) _presence.SetUserLabel(label);
            _presence.EnqueueUserUpdated(slot, avatarUrl, label);
        }
        catch { }
    }

    private async Task ProcessPlaceJoinedAsync(PlaceJoinedArgs args, Task previous, long generation)
    {
        await previous;

        Task? refresh;
        long userId;
        lock (_userRefreshLock)
        {
            _slotUserRefreshes.TryGetValue(args.Slot, out refresh);
            _slotUserIds.TryGetValue(args.Slot, out userId);
            if (refresh == null && userId > 0)
            {
                refresh = RefreshRobloxAccountAsync(args.Slot, userId, generation);
                _slotUserRefreshes[args.Slot] = refresh;
            }
        }

        if (refresh != null) await refresh;

        lock (_userRefreshLock)
        {
            if (_slotGenerations.GetValueOrDefault(args.Slot) != generation) return;
            if (refresh != null
                && _slotUserRefreshes.TryGetValue(args.Slot, out var current)
                && ReferenceEquals(current, refresh))
                _slotUserRefreshes.Remove(args.Slot);
        }

        _presence.EnqueuePlaceJoined(args.PlaceId, args.UniverseId, args.Slot);
    }

    private void UpdateRobloxPresence(RobloxStatus status)
    {
        if (_presence.GameDetected) return;

        switch (status)
        {
            case RobloxStatus.Updating:
            case RobloxStatus.Launching:
                _presence.EnqueueLaunchingPresence();
                break;
            case RobloxStatus.Running:
            case RobloxStatus.Idle:
            case RobloxStatus.NotInstalled:
                _presence.EnqueueRefresh();
                break;
        }
    }

    private void UpdateStudioPresence(RobloxStatus status)
    {
        if (_presence.GameDetected) return;

        switch (status)
        {
            case RobloxStatus.Updating:
            case RobloxStatus.Launching:
                _presence.EnqueueInstallingStudioPresence();
                break;
            case RobloxStatus.Idle:
                _presence.EnqueueRefresh();
                break;
        }
    }

    private async Task InstallStudioPluginAsync()
    {
        var isUpdate = StudioPluginInstaller.IsInstalled; // 既存ファイルあり = アップデート扱い
        var vm = new BootstrapperViewModel(_settings);
        vm.ReportProgress(isUpdate ? "Updating Studio plugin..." : "Installing Studio plugin...", 0, true);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var win = new NexStrap.Views.BootstrapperWindow(vm);
            win.Show();
        });

        await StudioPluginInstaller.DownloadAndInstallAsync(
            progress: new Progress<(string Msg, double Pct, bool Indet)>(p =>
                vm.ReportProgress(p.Msg, p.Pct, p.Indet)));

        await Dispatcher.UIThread.InvokeAsync(() => vm.RequestClose());
    }

    // ══════════════════════════════════════════════════════════════════════
    // コマンド
    // ══════════════════════════════════════════════════════════════════════


    [RelayCommand]
    private async Task RestartRobloxAsync()
    {
        foreach (var proc in GetRobloxProcesses())
            try { proc.Kill(entireProcessTree: true); } catch { }
        await Task.Delay(RestartDelay);
        await LaunchRobloxAsync();
    }

    [RelayCommand]
    public async Task<bool> LaunchRobloxAsync()
    {
        var multiInstance = _settings.Settings.MultiInstanceEnabled;
        if (IsLaunching || (IsRobloxRunning && !multiInstance)) return false;

        IsLaunching = true;
        _presence.EnqueueLaunchStarted();
        StatusText = "Applying flags...";

        _fastFlags.ApplyPerformanceSettings(_settings.Settings);
        await _fastFlags.SaveAsync();
        await _mods.ApplyEnabledModsAsync();

        StatusText       = "Checking for updates...";
        _launchStartTime = DateTime.UtcNow;

        string? launchArgs = null;
        string? cookie = _accountService.GetActiveCookie();
        if (cookie != null)
        {
            var ticket = await _robloxApi.GetAuthTicketAsync(cookie);
            if (ticket != null)
                launchArgs = $"--launchMode app --authenticationTicket {ticket} --authenticationUrl https://auth.roblox.com";
        }

        var s    = _settings.Settings;
        var opts = new NexStrap.Services.LaunchOptions(
            MultiInstance: s.MultiInstanceEnabled, SuppressCrashHandler: s.SuppressCrashHandler,
            CpuCoreLimit: s.CpuAffinityEnabled ? s.CpuCoreLimit : 0,
            MemoryOptimization: s.MemoryOptimizationEnabled, CleanupOldVersions: s.CleanupOldVersions,
            CookieToInject: cookie, StretchResolution: s.StretchResolutionEnabled,
            StretchWidth: s.StretchResolutionWidth, StretchHeight: s.StretchResolutionHeight);

        var launched = await _roblox.LaunchAsync(launchArgs, autoUpdate: s.AutoUpdateRoblox, options: opts);
        if (!launched)
        {
            StatusText = "Launch failed";
            IsLaunching = false;
            IsRobloxRunning = false;
            return false;
        }

        return true;
    }

    [RelayCommand]
    private void RejoinGame(GameEntryViewModel vm)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"roblox://experiences/start?placeId={vm.PlaceId}", UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private async Task LaunchStudioAsync()
    {
        if (IsStudioLaunching) return;
        IsStudioLaunching = true;
        StatusText        = "Checking for updates...";
        _presence.EnqueueInstallingStudioPresence();

        // GitHub と比較して未インストールまたは更新がある場合はインストーラー UI を表示
        if (await StudioPluginInstaller.IsUpdateAvailableAsync())
            await InstallStudioPluginAsync();
        await _studioFastFlags.SaveAsync();
        StatusText = "Launching Studio...";
        var launched = await _studio.LaunchAsync();
        StatusText        = launched ? (IsRobloxRunning ? "Roblox running" : "Ready") : "Studio launch failed";
        IsStudioLaunching = false;
    }

    [RelayCommand]
    private async Task HotReloadFlagsAsync()
    {
        var flags = _fastFlags.GetAll();
        await _fastFlags.HotReloadAsync(flags);
        StatusText = "Fast Flags hot reloaded";
        await Task.Delay(HotReloadStatusDelay);
        StatusText = _presence.GameDetected && _history.Entries.Any()
            ? $"Playing: {_history.Entries.Last().Name}"
            : IsRobloxRunning ? "Playing" : "Ready";
    }

    // ══════════════════════════════════════════════════════════════════════
    // IsRobloxRunning 変化
    // ══════════════════════════════════════════════════════════════════════

    partial void OnIsRobloxRunningChanged(bool value)
    {
        _presence.EnqueueRobloxChanged(value);
        if (!value && _settings.Settings.StretchResolutionEnabled) _roblox.RestoreResolution();
        if (value)
        {
            var s = _settings.Settings;
            if (s.StretchResolutionEnabled)
                _roblox.ApplyStretchResolution(s.StretchResolutionWidth, s.StretchResolutionHeight);
        }
        if (Application.Current is App app) app.SetPlayingMode(value);
    }

    // ══════════════════════════════════════════════════════════════════════
    // ゲームリスト
    // ══════════════════════════════════════════════════════════════════════

    private void RebuildGameLists()
    {
        var favorites = _settings.Settings.FavoriteGameIds.ToHashSet();
        RecentGames.Clear(); FavoriteGames.Clear();
        var placeIdToUniverseMap = GameHistoryService.BuildPlaceIdToUniverseMap(_history.Entries);
        var grouped = _history.Entries
            .GroupBy(e => GameHistoryService.ResolveGroupKey(e, placeIdToUniverseMap))
            .Select(g => { var best = g.OrderByDescending(e => e.PlayedAt).First(); return (Entry: best, Total: g.Sum(e => e.DurationSeconds)); });
        var sorted = HomeSortOrder == HomeSortMode.TotalTime
            ? grouped.OrderByDescending(x => x.Total)
            : grouped.OrderByDescending(x => x.Entry.PlayedAt);
        foreach (var (entry, total) in sorted)
        {
            var vm = new GameEntryViewModel(entry) { IsFavorite = favorites.Contains(entry.PlaceId), DisplayDurationSeconds = total };
            RecentGames.Add(vm);
            if (vm.IsFavorite) FavoriteGames.Add(vm);
        }
    }

    [RelayCommand]
    private void ToggleFavorite(GameEntryViewModel vm)
    {
        vm.IsFavorite = !vm.IsFavorite;
        var ids = _settings.Settings.FavoriteGameIds;
        if (vm.IsFavorite) { if (!ids.Contains(vm.PlaceId)) ids.Add(vm.PlaceId); if (!FavoriteGames.Any(f => f.PlaceId == vm.PlaceId)) FavoriteGames.Insert(0, vm); }
        else { ids.Remove(vm.PlaceId); var ex = FavoriteGames.FirstOrDefault(f => f.PlaceId == vm.PlaceId); if (ex != null) FavoriteGames.Remove(ex); }
        _settings.Update(_ => { });
        UpdateJumpList();
    }

    private void UpdateJumpList()
    {
        try { JumpListService.Update(FavoriteGames.Select(g => (g.PlaceId, g.Name))); } catch { }
    }

    // ══════════════════════════════════════════════════════════════════════
    // ヘルパー
    // ══════════════════════════════════════════════════════════════════════

    private async Task ApplyUserLabelAsync(long userId)
    {
        if (!_settings.Settings.DiscordShowRobloxUsername) return;
        var info = await _robloxApi.GetUserInfoAsync(userId);
        if (info is not { } u) return;
        var label = _settings.Settings.DiscordUseDisplayNameFormat
            ? $"{u.displayName} (@{u.username})" : $"@{u.username}";
        _presence.SetUserLabel(label);
    }
}
