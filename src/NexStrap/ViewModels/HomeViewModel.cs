using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    // ══════════════════════════════════════════════════════════════════════
    // サービス依存
    // ══════════════════════════════════════════════════════════════════════

    private readonly RobloxService            _roblox;
    private readonly StudioService            _studio;
    private readonly FastFlagService          _fastFlags;
    private readonly StudioFastFlagService    _studioFastFlags;
    private readonly ModService               _mods;
    private readonly SettingsService          _settings;
    private readonly DiscordRichPresence      _presence;
    private readonly RobloxLogWatcher         _logWatcher;
    private readonly RobloxApiService         _robloxApi;
    private readonly GameHistoryService       _history;
    private readonly FriendNotificationService _friendNotifications;
    private readonly AccountService           _accountService;

    // ══════════════════════════════════════════════════════════════════════
    // ゲームセッション履歴・タイムスタンプ（presence に非依存）
    // ══════════════════════════════════════════════════════════════════════

    private string?           _userAvatarUrl;
    private DateTime?         _launchStartTime;
    private DateTime?         _gameStartTime;
    private double            _accumulatedDurationSeconds;
    private GameHistoryEntry? _sessionEntry;

    // ══════════════════════════════════════════════════════════════════════
    // フォーカス・Stretch Res（presence に非依存）
    // ══════════════════════════════════════════════════════════════════════

    private readonly Dictionary<int, uint> _slotPids     = new();
    private bool                           _robloxHasFocus;
    private Timer?                         _focusTimer;

    private const int FocusTimerInterval   = 500;
    private const int RestartDelay         = 1_500;
    private const int HotReloadStatusDelay = 2_000;
    private const int LaunchStatusDelay    = 3_000;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint   GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    // ══════════════════════════════════════════════════════════════════════
    // 公開プロパティ
    // ══════════════════════════════════════════════════════════════════════

    internal string CurrentPageName
    {
        get => _presence.CurrentPageName ?? "Home";
        set { _presence.SetCurrentPage(value); }
    }

    internal bool IsGameDetected => _presence.GameDetected;
    public   string? UserAvatarUrl => _userAvatarUrl;

    [ObservableProperty] private string?      _userDisplayName;
    [ObservableProperty] private bool         _isMultiInstanceWarningVisible;
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
    private void SetHomeSort(HomeSortMode mode)
    {
        HomeSortOrder  = mode;
        IsSortMenuOpen = false;
    }

    public ObservableCollection<GameEntryViewModel> RecentGames   { get; } = [];
    public ObservableCollection<GameEntryViewModel> FavoriteGames { get; } = [];

    private static IEnumerable<Process> GetRobloxProcesses() =>
        Process.GetProcessesByName("RobloxPlayerBeta")
               .Concat(Process.GetProcessesByName("RobloxPlayer"));

    // ══════════════════════════════════════════════════════════════════════
    // コンストラクタ
    // ══════════════════════════════════════════════════════════════════════

    public HomeViewModel(
        RobloxService roblox,
        StudioService studio,
        FastFlagService fastFlags,
        StudioFastFlagService studioFastFlags,
        ModService mods,
        SettingsService settings,
        DiscordRichPresence presence,
        RobloxLogWatcher logWatcher,
        RobloxApiService robloxApi,
        GameHistoryService history,
        FriendNotificationService friendNotifications,
        AccountService accountService)
    {
        _roblox               = roblox;
        _studio               = studio;
        _fastFlags            = fastFlags;
        _studioFastFlags      = studioFastFlags;
        _mods                 = mods;
        _settings             = settings;
        _presence             = presence;
        _logWatcher           = logWatcher;
        _robloxApi            = robloxApi;
        _history              = history;
        _friendNotifications  = friendNotifications;
        _accountService       = accountService;

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
        if (versionPath != null)
            RobloxVersion = new DirectoryInfo(versionPath).Name;

        // ── Roblox 起動状態変化 ───────────────────────────────────────────
        roblox.StatusChanged += (_, status) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLaunching = status is RobloxStatus.Launching or RobloxStatus.Updating;

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

                if (_presence.GameDetected) return;

                switch (status)
                {
                    case RobloxStatus.Updating:
                        _presence.SetUpdatingPresence(_userAvatarUrl);
                        break;
                    case RobloxStatus.Launching:
                        _presence.SetLaunchingPresence(_userAvatarUrl);
                        break;
                    case RobloxStatus.Running:
                    case RobloxStatus.Idle:
                    case RobloxStatus.NotInstalled:
                        _presence.RefreshPresence();
                        break;
                }
            });
        };

        // ── ユーザーID検出 ────────────────────────────────────────────────
        _logWatcher.UserIdDetected += async (_, userId) =>
        {
            var slot = _logWatcher.CurrentSlotId;
            try
            {
                _settings.Update(s => s.CachedRobloxUserId = userId);
                _friendNotifications.Start(userId);

                var avatarUrl = await _robloxApi.GetUserAvatarHeadshotAsync(userId);
                if (slot == 0)
                {
                    _userAvatarUrl = avatarUrl;
                    _presence.SetUserAvatar(avatarUrl);
                }

                string? userLabel = null;
                var userInfo = await _robloxApi.GetUserInfoAsync(userId);
                if (userInfo is { } u)
                {
                    if (slot == 0)
                        UserDisplayName = string.IsNullOrEmpty(u.displayName) ? u.username : u.displayName;
                    if (_settings.Settings.DiscordShowRobloxUsername)
                        userLabel = _settings.Settings.DiscordUseDisplayNameFormat
                            ? $"{u.displayName} (@{u.username})"
                            : $"@{u.username}";
                }

                if (slot == 0)
                    _presence.SetUserLabel(userLabel);

                _presence.NotifyUserUpdated(slot, avatarUrl, userLabel);
            }
            catch { }
        };

        // ── サーバーIP検出 ─────────────────────────────────────────────────
        _logWatcher.ServerIpDetected += async (_, ip) =>
        {
            try
            {
                var code = await _robloxApi.GetServerCountryCodeAsync(ip);
                // _gameDetected が false でも最大 3 秒待つ（UDMUX が PlaceJoined より先の場合）
                for (int i = 0; i < 6; i++)
                {
                    if (_presence.GameDetected) { _presence.NotifyServerCode(code); return; }
                    await Task.Delay(500);
                }
            }
            catch { }
        };

        // ── ゲーム参加 / テレポート ───────────────────────────────────────
        _logWatcher.PlaceJoined += async (_, args) =>
        {
            var (placeId, universeIdFromLog) = args;
            if (_logWatcher.IsWatchingStudioLog) return;

            var currentSlot = _logWatcher.CurrentSlotId;
            RefreshSlotPids();

            await _presence.HandlePlaceJoinedAsync(placeId, universeIdFromLog, currentSlot);
        };

        // ── DiscordRichPresence からのゲーム履歴イベント ──────────────────
        _presence.SessionEnded += (_, _) =>
        {
            // 新規セッション開始前に前のセッションを確定保存
            if (_gameStartTime.HasValue && _sessionEntry != null)
            {
                var elapsed = (DateTime.UtcNow - _gameStartTime.Value).TotalSeconds;
                _history.UpdateDuration(_sessionEntry, (int)(_accumulatedDurationSeconds + elapsed));
            }
            _accumulatedDurationSeconds = 0;
            _sessionEntry = null;
        };

        _presence.TeleportOccurred += (_, _) =>
        {
            var added = _gameStartTime.HasValue ? (DateTime.UtcNow - _gameStartTime.Value).TotalSeconds : 0.0;
            _accumulatedDurationSeconds += added;
            _gameStartTime = DateTime.UtcNow;
        };

        _presence.GameInfoFetched += async (_, args) =>
        {
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
                ? (DateTime.UtcNow - _launchStartTime.Value).TotalSeconds
                : (double?)null;
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
        _logWatcher.StudioPlaySoloStarted += (_, _) => _presence.NotifyStudioPlaytestStarted();
        _logWatcher.StudioPlaySoloStopped += (_, _) => _presence.NotifyStudioPlaytestStopped();

        // ── ゲーム退出 ─────────────────────────────────────────────────────
        _logWatcher.GameLeft += (_, _) =>
        {
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

            _presence.HandleGameLeft(_logWatcher.CurrentSlotId, robloxCount);

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
            if (!connected) return;
            _presence.RefreshPresence();
        };

        // ── マルチインスタンス: スロット↔PID マッピング ────────────────────
        _logWatcher.InstanceSlotChanged += (_, slotId) =>
        {
            try
            {
                var newest = Process.GetProcessesByName("RobloxPlayerBeta")
                    .Concat(Process.GetProcessesByName("RobloxPlayer"))
                    .Where(p => !p.HasExited)
                    .OrderByDescending(p => p.StartTime)
                    .FirstOrDefault();
                if (newest != null) _slotPids[slotId] = (uint)newest.Id;
            }
            catch { }
        };

        _logWatcher.Start();

        try
        {
            var oldest = GetRobloxProcesses()
                .Where(p => !p.HasExited)
                .OrderBy(p => p.StartTime)
                .FirstOrDefault();
            if (oldest != null) _slotPids[0] = (uint)oldest.Id;
        }
        catch { }

        // ── フォーカスタイマー（Stretch Res + マルチインスタンス presence） ─
        _focusTimer = new Timer(_ =>
        {
            try
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out uint focusPid);
                if (focusPid == 0) return;

                int? matched = null;
                lock (_slotPids)
                {
                    foreach (var kv in _slotPids)
                        if (kv.Value == focusPid) { matched = kv.Key; break; }
                }

                bool nowFocused = matched != null;
                if (nowFocused != _robloxHasFocus && IsRobloxRunning)
                {
                    _robloxHasFocus = nowFocused;
                    var s = _settings.Settings;
                    if (s.StretchResolutionEnabled)
                    {
                        if (nowFocused)
                            _roblox.ApplyStretchResolution(s.StretchResolutionWidth, s.StretchResolutionHeight);
                        else
                            _roblox.RestoreResolution();
                    }
                }

                if (matched == null) return;
                _presence.NotifyFocusChanged(matched.Value);
            }
            catch { }
        }, null, FocusTimerInterval, FocusTimerInterval);

        // ── Studio 状態変化 ────────────────────────────────────────────────
        _studio.StatusChanged += (_, status) =>
        {
            if (_presence.GameDetected) return;
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                switch (status)
                {
                    case RobloxStatus.Updating:
                        _presence.SetInstallingStudioPresence(_userAvatarUrl);
                        break;
                    case RobloxStatus.Launching:
                        _presence.SetLaunchingPresence(_userAvatarUrl);
                        break;
                    case RobloxStatus.Running:
                        break; // ウィンドウタイトル監視に委ねる
                    case RobloxStatus.Idle:
                        _presence.RefreshPresence();
                        break;
                }
            });
        };

        // ── 起動時 ─────────────────────────────────────────────────────────
        if (_roblox.IsNexStrapRobloxRunning())
            IsRobloxRunning = true;

        var activeAccount = _accountService.Accounts.FirstOrDefault(a => a.IsActive)
                         ?? _accountService.Accounts.FirstOrDefault();
        var startupUserId = activeAccount?.UserId > 0
            ? activeAccount.UserId
            : _settings.Settings.CachedRobloxUserId;

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
                _presence.RefreshPresence();
            });
        }

        _isMultiInstanceWarningVisible = _settings.Settings.MultiInstanceEnabled;
    }

    // ══════════════════════════════════════════════════════════════════════
    // 公開 API（MainWindowViewModel / DiscordViewModel が使う）
    // ══════════════════════════════════════════════════════════════════════

    public void RefreshPresence() => _presence.RefreshPresence();

    // ══════════════════════════════════════════════════════════════════════
    // コマンド・アクション
    // ══════════════════════════════════════════════════════════════════════

    [RelayCommand] private void DismissMultiInstanceWarning() => IsMultiInstanceWarningVisible = false;

    [RelayCommand]
    private async Task RestartRobloxAsync()
    {
        foreach (var proc in GetRobloxProcesses())
            try { proc.Kill(entireProcessTree: true); } catch { }
        await Task.Delay(RestartDelay);
        await LaunchRobloxAsync();
    }

    [RelayCommand]
    private async Task LaunchRobloxAsync()
    {
        var multiInstance = _settings.Settings.MultiInstanceEnabled;
        if (IsLaunching || (IsRobloxRunning && !multiInstance)) return;

        IsLaunching   = true;
        StatusText    = "Applying flags...";

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

        var s = _settings.Settings;
        var opts = new NexStrap.Core.Services.LaunchOptions(
            MultiInstance:       s.MultiInstanceEnabled,
            SuppressCrashHandler: s.SuppressCrashHandler,
            CpuCoreLimit:        s.CpuAffinityEnabled ? s.CpuCoreLimit : 0,
            MemoryOptimization:  s.MemoryOptimizationEnabled,
            CleanupOldVersions:  s.CleanupOldVersions,
            CookieToInject:      cookie,
            StretchResolution:   s.StretchResolutionEnabled,
            StretchWidth:        s.StretchResolutionWidth,
            StretchHeight:       s.StretchResolutionHeight
        );
        var launched = await _roblox.LaunchAsync(launchArgs, autoUpdate: s.AutoUpdateRoblox, options: opts);
        if (!launched)
        {
            StatusText      = "Launch failed";
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
    private async Task LaunchStudioAsync()
    {
        if (IsStudioLaunching) return;

        IsStudioLaunching = true;
        StatusText        = "Launching Studio...";

        _presence.SetLaunchingPresence(_userAvatarUrl);

        await _studioFastFlags.SaveAsync();
        var launched = await _studio.LaunchAsync();
        if (!launched)
            StatusText = "Studio launch failed";
        else
            StatusText = IsRobloxRunning ? "Roblox running" : "Ready";

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
    // ゲームリスト管理
    // ══════════════════════════════════════════════════════════════════════

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
                IsFavorite             = favorites.Contains(entry.PlaceId),
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

    // ══════════════════════════════════════════════════════════════════════
    // IsRobloxRunning 変化時
    // ══════════════════════════════════════════════════════════════════════

    partial void OnIsRobloxRunningChanged(bool value)
    {
        if (!value)
        {
            _presence.NotifyRobloxRunningChanged(false);

            if (_settings.Settings.StretchResolutionEnabled)
                _roblox.RestoreResolution();
        }
        else
        {
            var s = _settings.Settings;
            if (s.StretchResolutionEnabled)
                _roblox.ApplyStretchResolution(s.StretchResolutionWidth, s.StretchResolutionHeight);
        }
        if (Application.Current is App app)
            app.SetPlayingMode(value);
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
            ? $"{u.displayName} (@{u.username})"
            : $"@{u.username}";
        _presence.SetUserLabel(label);
    }

    private void RefreshSlotPids()
    {
        try
        {
            var procs = GetRobloxProcesses()
                .Where(p => !p.HasExited)
                .OrderBy(p => p.StartTime)
                .ToList();
            lock (_slotPids)
            {
                _slotPids.Clear();
                for (int i = 0; i < procs.Count; i++)
                    _slotPids[i] = (uint)procs[i].Id;
            }
        }
        catch { }
    }
}
