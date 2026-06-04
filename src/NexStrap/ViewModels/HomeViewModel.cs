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
    private readonly RobloxService _roblox;
    private readonly StudioService _studio;
    private readonly FastFlagService _fastFlags;
    private readonly StudioFastFlagService _studioFastFlags;
    private readonly ModService _mods;
    private readonly SettingsService _settings;
    private readonly DiscordRpcService _discord;
    private readonly RobloxLogWatcher _logWatcher;
    private readonly RobloxApiService _robloxApi;
    private readonly GameHistoryService _history;
    private readonly FriendNotificationService _friendNotifications;
    private readonly AccountService _accountService;

    internal string CurrentPageName { get; set; } = "Home";
    internal bool IsGameDetected => _gameDetected;

    private volatile bool    _gameDetected;
    private volatile bool    _awaitingGameInfo;
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

    // スロット別ゲーム情報（マルチインスタンス presence 集約用）
    private readonly record struct SlotGame(string? Name, string? IconUrl, string? Creator, long PlaceId, string? AvatarUrl, string? UserLabel);
    private readonly Dictionary<int, SlotGame>                    _activeGames   = new();
    private readonly Dictionary<int, (string? Url, string? Label)> _slotUsers    = new();
    private readonly Dictionary<int, uint>                         _slotPids      = new();
    private readonly object _gamesLock = new();
    private int    _activeFocusedSlot  = -1;
    private bool   _robloxHasFocus     = false;
    private volatile bool   _studioDetected     = false;
    private volatile bool   _studioPlaytesting  = false;
    private volatile string _lastStudioPresence = string.Empty;
    private Timer? _focusTimer;
    private Timer? _presenceHeartbeat;
    private Timer? _studioTimer;

    private const int FocusTimerInterval   = 500;
    private const int PresenceHeartbeat    = 15_000;
    private const int RestartDelay         = 1_500;
    private const int HotReloadStatusDelay = 2_000;
    private const int LaunchStatusDelay    = 3_000;
    private const int StudioPollInterval   = 3_000;

    private static IEnumerable<Process> GetRobloxProcesses() =>
        Process.GetProcessesByName("RobloxPlayerBeta")
               .Concat(Process.GetProcessesByName("RobloxPlayer"));

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint   GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

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

    [ObservableProperty] private string? _userDisplayName;

    public static string AppVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            return v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v1.1";
        }
    }

    [ObservableProperty] private bool         _isMultiInstanceWarningVisible;
    [ObservableProperty] private bool         _isRobloxRunning;
    [ObservableProperty] private bool         _isLaunching;
    [ObservableProperty] private bool         _isStudioLaunching;
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
        StudioService studio,
        FastFlagService fastFlags,
        StudioFastFlagService studioFastFlags,
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
        _studio               = studio;
        _fastFlags            = fastFlags;
        _studioFastFlags      = studioFastFlags;
        _mods                 = mods;
        _settings             = settings;
        _discord              = discord;
        _logWatcher           = logWatcher;
        _robloxApi            = robloxApi;
        _history              = history;
        _friendNotifications  = friendNotifications;
        _accountService       = accountService;

        // 初回インストール後にバージョンフォルダが確定したタイミングでフラグ・Modを適用
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
                        if (_studioDetected)
                            _discord.SetStudioHomePresence(_userAvatarUrl);
                        else
                            _discord.SetPagePresence(CurrentPageName, _userAvatarUrl);
                        break;
                }
            });
        };

        // ユーザーID検出 → アバター URL 取得してキャッシュ、フレンド通知開始
        _logWatcher.UserIdDetected += async (_, userId) =>
        {
            // スロットIDは await 前にスナップショット（await 後は変わっている可能性がある）
            var slot = _logWatcher.CurrentSlotId;
            try
            {
                _settings.Update(s => s.CachedRobloxUserId = userId);
                _friendNotifications.Start(userId);

                var avatarUrl = await _robloxApi.GetUserAvatarHeadshotAsync(userId);

                // スロット0（最初のインスタンス）はグローバルアバター・表示名も更新
                if (slot == 0) _userAvatarUrl = avatarUrl;

                // ユーザー情報を取得（表示名 + Discord ラベル）
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

                // スロット別に保存
                lock (_gamesLock)
                {
                    _slotUsers[slot] = (avatarUrl, userLabel);
                    if (_activeGames.TryGetValue(slot, out var game))
                        _activeGames[slot] = game with { AvatarUrl = avatarUrl, UserLabel = userLabel };
                }

                if (slot == 0)
                    _discord.SetUserLabel(userLabel);

                if (_gameDetected)
                    UpdateGamePresence();
                else if (_studioDetected)
                    _discord.SetStudioPresence(_userAvatarUrl);
                else if (!IsRobloxRunning && !IsLaunching)
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
                // _gameDetected が false でも最大 3 秒待つ（UDMUX が PlaceJoined より先の場合）
                for (int i = 0; i < 6; i++)
                {
                    if (_gameDetected)
                    {
                        bool hasGames;
                        lock (_gamesLock) { hasGames = _activeGames.Count > 0; }
                        if (hasGames) { UpdateGamePresence(); return; }
                    }
                    await Task.Delay(500);
                }
            }
            catch { }
        };

        // ゲーム参加 / テレポート — universeId で同一ゲーム内かを判定
        _logWatcher.PlaceJoined += async (_, args) =>
        {
            var (placeId, universeIdFromLog) = args;

            // Studio ログの PlaceJoined はエディタ開いた時も発火するため無視
            if (_logWatcher.IsWatchingStudioLog) return;

            // スロットIDは await 後にログファイルが切り替わると変わるため、最初にスナップショット
            var currentSlot     = _logWatcher.CurrentSlotId;
            var prevDetected    = _gameDetected;
            var prevUniverseId  = _currentUniverseId;
            var prevStartTime   = _gameStartTime;
            var prevAccumulated = _accumulatedDurationSeconds;

            var seq = Interlocked.Increment(ref _joinSequence);

            // _gameDetected を await より前に true にして ConnectionChanged が
            // page presence を送り込む競合ウィンドウを閉じる
            bool wasPreviouslyDetected = prevDetected;
            _gameDetected = true;

            // ログから universeid: が取れていれば API 呼び出しをスキップ
            long newUniverseId = universeIdFromLog;
            if (newUniverseId == 0)
            {
                try { newUniverseId = (await _robloxApi.GetUniverseIdAsync(placeId)) ?? 0; } catch { }
            }

            if (Interlocked.Read(ref _joinSequence) != seq)
            {
                // 別の join が来たので _gameDetected はそちらに委ねる
                return;
            }

            // 同じ universeId → テレポート（同一ゲーム内の移動）
            bool isTeleport = wasPreviouslyDetected && newUniverseId != 0 && newUniverseId == prevUniverseId;

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

                try
                {
                    var (name, iconUrl, creator) = await _robloxApi.GetGameInfoAsync(placeId, newUniverseId);
                    if (Interlocked.Read(ref _joinSequence) != seq || !_gameDetected) return;

                    // _lastGame* と _activeGames を新しいサブプレイスで更新。
                    // 更新しないと Join ボタンが古い placeId を指し、RefreshPresence が古い情報を返す。
                    var resolvedName    = name    ?? _lastGameName    ?? "Roblox";
                    var resolvedIcon    = iconUrl ?? _lastGameIconUrl;
                    var resolvedCreator = creator ?? _lastGameCreator;

                    // アイコンなし = API失敗 — 古い情報を維持して不完全 Presence を送らない
                    if (resolvedIcon == null) return;

                    _lastGameName    = resolvedName;
                    _lastGameIconUrl = resolvedIcon;
                    _lastGameCreator = resolvedCreator;
                    _slotUsers.TryGetValue(currentSlot, out var tpUser);
                    _activeGames[currentSlot] = new SlotGame(resolvedName, resolvedIcon, resolvedCreator, placeId, tpUser.Url, tpUser.Label);

                    _discord.SetInGamePresence(resolvedName, resolvedIcon, _userAvatarUrl, FormatState(), resolvedCreator, placeId);
                }
                catch { }
                return;
            }

            // ── 新しいゲームセッション ─────────────────────────────────────────
            // 前のセッションがあれば累積時間を確定保存
            if (wasPreviouslyDetected && prevStartTime.HasValue && _sessionEntry != null)
            {
                var elapsed = (DateTime.UtcNow - prevStartTime.Value).TotalSeconds;
                _history.UpdateDuration(_sessionEntry, (int)(prevAccumulated + elapsed));
            }

            _accumulatedDurationSeconds = 0;
            // _currentServerCode はクリアしない — UDMUX が PlaceJoined より先に来た場合の値を保持
            // _gameDetected はすでに true（await より前にセット済み）
            _currentUniverseId          = newUniverseId;
            _lastPlaceId                = placeId;
            _gameStartTime              = DateTime.UtcNow;
            _sessionEntry               = null;
            ClearLastGameInfo();
            _awaitingGameInfo           = true;

            // ゲーム参加のたびに PID マッピングを更新（起動後初参加でも正確に追跡）
            RefreshSlotPids();

            // 情報が揃うまで presence は更新しない（API 完了後に初めて表示）
            _discord.ResetGameTimestamp();

            try
            {
                var (name, iconUrl, creator) = await _robloxApi.GetGameInfoAsync(placeId, newUniverseId);
                if (Interlocked.Read(ref _joinSequence) != seq || !_gameDetected) { _awaitingGameInfo = false; return; }

                // iconUrl == null はAPI取得失敗 — 不完全 Presence は送らない
                if (iconUrl == null) { _awaitingGameInfo = false; return; }

                _lastGameName    = name;
                _lastGameIconUrl = iconUrl;
                _lastGameCreator = creator;
                lock (_gamesLock)
                {
                    _slotUsers.TryGetValue(currentSlot, out var su);
                    _activeGames[currentSlot] = new SlotGame(name, iconUrl, creator, placeId, su.Url, su.Label);
                }
                _awaitingGameInfo = false;
                // 新接続の場合は OnReady → ConnectionChanged → RefreshPresence() が presence を確実に送る。
                // 既に同 App ID で接続済み（early return）の場合は直接更新する。
                if (!_discord.Initialize(AppConstants.DiscordRobloxAppId))
                    UpdateGamePresence();

                var entry = new GameHistoryEntry { PlaceId = placeId, UniverseId = newUniverseId, Name = name, IconUrl = iconUrl, PlayedAt = DateTime.Now };
                _history.Add(entry);
                _sessionEntry = entry;

                var launchMs = _launchStartTime.HasValue
                    ? (DateTime.UtcNow - _launchStartTime.Value).TotalSeconds
                    : (double?)null;
                _launchStartTime = null;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RebuildGameLists();
                    StatusText      = launchMs.HasValue ? $"Launch: {launchMs.Value:F1}s" : $"Playing: {name}";
                    IsRobloxRunning = true;
                    IsLaunching     = false;
                });

                if (launchMs.HasValue)
                {
                    await Task.Delay(LaunchStatusDelay);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (IsRobloxRunning) StatusText = $"Playing: {name}";
                    });
                }
            }
            catch { _awaitingGameInfo = false; }
        };

        // Studio テストプレイ開始
        _logWatcher.StudioPlaySoloStarted += (_, _) =>
        {
            _studioPlaytesting = true;
            _discord.SetStudioPlaytestPresence(null, _userAvatarUrl);
        };

        // Studio テストプレイ終了 → タイマーにウィンドウタイトル再判定させる
        _logWatcher.StudioPlaySoloStopped += (_, _) =>
        {
            _studioPlaytesting = false;
            _lastStudioPresence = string.Empty;
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
            ClearLastGameInfo();
            _gameStartTime              = null;

            // このスロットのエントリを削除
            var robloxCount = RobloxLogWatcher.IsRobloxRunning() || _roblox.IsNexStrapRobloxRunning()
                ? GetRobloxProcesses().Count()
                : 0;
            bool hasRemaining;
            SlotGame remaining = default;
            lock (_gamesLock)
            {
                _activeGames.Remove(_logWatcher.CurrentSlotId);
                while (_activeGames.Count > robloxCount)
                    _activeGames.Remove(_activeGames.Keys.Min());
                hasRemaining = _activeGames.Count > 0;
                if (hasRemaining)
                    remaining = _activeGames[_activeGames.Keys.Max()];
            }

            if (hasRemaining)
            {
                // まだ別インスタンスがプレイ中
                _lastGameName    = remaining.Name;
                _lastGameIconUrl = remaining.IconUrl;
                _lastGameCreator = remaining.Creator;
                _lastPlaceId     = remaining.PlaceId;
                _gameDetected    = true;
                UpdateGamePresence();
            }
            else
            {
                _discord.Initialize(AppConstants.DiscordAppId);
                if (_studioDetected)
                    _discord.SetStudioPresence(_userAvatarUrl);
                else
                    _discord.SetPagePresence(CurrentPageName, _userAvatarUrl, "Roblox");
            }
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText = "Ready";
                // Roblox process may still be open (user returned to menu) — only clear if truly exited
                if (!_roblox.IsNexStrapRobloxRunning() && !RobloxLogWatcher.IsRobloxRunning())
                    IsRobloxRunning = false;
            });
        };

        // Discord 再接続時は RefreshPresence で正しく復元（_activeGames が空の場合も考慮）
        _discord.ConnectionChanged += (_, connected) =>
        {
            if (!connected) return;
            RefreshPresence();
        };

        // 新しい Roblox インスタンス起動時: 最新のプロセスをそのスロットに関連付ける
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

        // スロット0 の PID = 最も古い（最初の）Roblox プロセス
        try
        {
            var oldest = GetRobloxProcesses()
                .Where(p => !p.HasExited)
                .OrderBy(p => p.StartTime)
                .FirstOrDefault();
            if (oldest != null) _slotPids[0] = (uint)oldest.Id;
        }
        catch { }

        // アクティブウィンドウを500msごとに監視
        _focusTimer = new Timer(_ =>
        {
            try
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out uint focusPid);
                if (focusPid == 0) return;

                // _slotPids に一致する PID があるか検索
                int? matched = null;
                lock (_gamesLock)
                {
                    foreach (var kv in _slotPids)
                    {
                        if (kv.Value == focusPid) { matched = kv.Key; break; }
                    }
                }

                // Stretch Res: Roblox ウィンドウのフォーカス変化を検出
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
                if (matched.Value == _activeFocusedSlot) return;
                _activeFocusedSlot = matched.Value;
                if (_gameDetected) UpdateGamePresence();
            }
            catch { }
        }, null, FocusTimerInterval, FocusTimerInterval);

        // 15秒ごとにゲーム中の presence を再送する。
        // PlaceJoined 中の API 待機レースや Discord 側の無音ドロップで presence が消えた場合の保険。
        // API取得失敗時は _activeGames が空のままなのでリトライもここで行う。
        _presenceHeartbeat = new Timer(async _ =>
        {
            try
            {
                if (!_gameDetected) return;
                bool hasGames;
                lock (_gamesLock) { hasGames = _activeGames.Count > 0; }
                if (hasGames)
                    UpdateGamePresence();
                else if (!_awaitingGameInfo)
                    await TryFetchGameInfoAndUpdateAsync();
            }
            catch { }
        }, null, PresenceHeartbeat, PresenceHeartbeat);

        // Studio インストール / 起動状態 → Discord presence
        _studio.StatusChanged += (_, status) =>
        {
            if (_gameDetected) return;
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                switch (status)
                {
                    case RobloxStatus.Updating:
                        _discord.SetInstallingStudioPresence(_userAvatarUrl);
                        break;
                    case RobloxStatus.Launching:
                        _discord.SetLaunchingPresence(_userAvatarUrl);
                        break;
                    case RobloxStatus.Running:
                        // presence はウィンドウタイトル監視の _studioTimer に委ねる
                        _lastStudioPresence = string.Empty;
                        break;
                    case RobloxStatus.Idle:
                        if (!_studioDetected)
                            _discord.SetPagePresence(CurrentPageName, _userAvatarUrl);
                        break;
                }
            });
        };

        // Studio プロセスを3秒ごとに監視して presence を切り替える
        _studioTimer = new Timer(_ => CheckStudioProcess(), null, StudioPollInterval, StudioPollInterval);

        // 起動時にすでに NexStrap が起動した Roblox が動いていれば IsRobloxRunning を立てる
        if (_roblox.IsNexStrapRobloxRunning())
            IsRobloxRunning = true;

        // 起動時にアカウント情報を取得: アクティブアカウント → キャッシュIDの順で参照
        var activeAccount  = _accountService.Accounts.FirstOrDefault(a => a.IsActive)
                          ?? _accountService.Accounts.FirstOrDefault();
        var startupUserId  = activeAccount?.UserId > 0
            ? activeAccount.UserId
            : _settings.Settings.CachedRobloxUserId;

        if (startupUserId > 0)
        {
            // アカウントに保存済みのアバターURLをすぐに適用（ネット取得より高速）
            if (activeAccount?.AvatarUrl != null)
                _userAvatarUrl = activeAccount.AvatarUrl;
            if (!string.IsNullOrEmpty(activeAccount?.DisplayName))
                UserDisplayName = activeAccount.DisplayName;
            else if (!string.IsNullOrEmpty(activeAccount?.Username))
                UserDisplayName = activeAccount.Username;

            _friendNotifications.Start(startupUserId);

            _ = Task.Run(async () =>
            {
                // 最新のアバターURLとユーザーラベルをAPIから取得
                _userAvatarUrl = await _robloxApi.GetUserAvatarHeadshotAsync(startupUserId);
                await ApplyUserLabelAsync(startupUserId);
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

        // Multi Instance ON の場合、起動時に1度だけ警告バナーを表示
        _isMultiInstanceWarningVisible = _settings.Settings.MultiInstanceEnabled;
    }

    [RelayCommand]
    private void DismissMultiInstanceWarning() => IsMultiInstanceWarningVisible = false;

    [RelayCommand]
    private async Task RestartRobloxAsync()
    {
        // 起動中の Roblox を強制終了してから再起動
        foreach (var proc in GetRobloxProcesses())
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
        }
        await Task.Delay(RestartDelay);
        await LaunchRobloxAsync();
    }

    [RelayCommand]
    private async Task LaunchRobloxAsync()
    {
        var multiInstance = _settings.Settings.MultiInstanceEnabled;
        if (IsLaunching || (IsRobloxRunning && !multiInstance)) return;

        IsLaunching   = true;
        _gameDetected = false;
        StatusText    = "Applying flags...";

        _fastFlags.ApplyPerformanceSettings(_settings.Settings);
        await _fastFlags.SaveAsync();
        await _mods.ApplyEnabledModsAsync();

        StatusText = "Checking for updates...";
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
    private async Task LaunchStudioAsync()
    {
        if (IsStudioLaunching) return;

        IsStudioLaunching = true;
        StatusText        = "Launching Studio...";

        _discord.SetLaunchingPresence(_userAvatarUrl);

        await _studioFastFlags.SaveAsync();
        var launched = await _studio.LaunchAsync();
        if (!launched)
            StatusText = "Studio launch failed";
        else
            StatusText = IsRobloxRunning ? "Roblox running" : "Ready";

        IsStudioLaunching = false;
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
        await Task.Delay(HotReloadStatusDelay);
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

    private void RefreshSlotPids()
    {
        try
        {
            var procs = GetRobloxProcesses()
                .Where(p => !p.HasExited)
                .OrderBy(p => p.StartTime)
                .ToList();
            lock (_gamesLock)
            {
                _slotPids.Clear();
                for (int i = 0; i < procs.Count; i++)
                    _slotPids[i] = (uint)procs[i].Id;
            }
        }
        catch { }
    }

    private async Task TryFetchGameInfoAndUpdateAsync()
    {
        if (!_gameDetected || _awaitingGameInfo || _lastPlaceId == 0) return;

        var placeId    = _lastPlaceId;
        var universeId = _currentUniverseId;
        var slot       = _logWatcher.CurrentSlotId;
        var seq        = Interlocked.Read(ref _joinSequence);

        _awaitingGameInfo = true;
        _discord.SetPagePresence(CurrentPageName, _userAvatarUrl); // リトライ中も参加前の状況を維持
        try
        {
            var (name, iconUrl, creator) = await _robloxApi.GetGameInfoAsync(placeId, universeId);

            if (Interlocked.Read(ref _joinSequence) != seq || !_gameDetected) { _awaitingGameInfo = false; return; }
            if (iconUrl == null) { _awaitingGameInfo = false; return; }

            _lastGameName    = name;
            _lastGameIconUrl = iconUrl;
            _lastGameCreator = creator;
            lock (_gamesLock)
            {
                _slotUsers.TryGetValue(slot, out var su);
                _activeGames[slot] = new SlotGame(name, iconUrl, creator, placeId, su.Url, su.Label);
            }
            _awaitingGameInfo = false;
            if (!_discord.Initialize(AppConstants.DiscordRobloxAppId))
                UpdateGamePresence();
        }
        catch { _awaitingGameInfo = false; }
    }

    public void RefreshPresence()
    {
        bool hasGames;
        lock (_gamesLock) { hasGames = _activeGames.Count > 0; }
        if (hasGames)
            UpdateGamePresence();
        else if (_gameDetected)
        {
            if (_awaitingGameInfo)
                _discord.SetPagePresence(CurrentPageName, _userAvatarUrl); // 初回フェッチ中 → 参加前の状況を維持
            else
                _ = TryFetchGameInfoAndUpdateAsync(); // フェッチ失敗後 → リトライ（page presence を先に表示）
        }
        else if (_studioDetected)
            _discord.SetStudioPresence(_userAvatarUrl);
        else
            _discord.SetPagePresence(CurrentPageName, _userAvatarUrl);
    }

    private void UpdateGamePresence()
    {
        List<SlotGame> games;
        lock (_gamesLock) { games = _activeGames.Values.ToList(); }
        if (games.Count == 0)
        {
            // ゲーム情報未取得（API待機中・失敗中・非ゲーム状態）→ 参加前の状況（page presence）を維持
            _discord.SetPagePresence(CurrentPageName, _userAvatarUrl);
            return;
        }

        // スロット追跡はズレることがあるため、実際のRobloxプロセス数で判定
        int robloxCount;
        try
        {
            robloxCount = Math.Max(1, GetRobloxProcesses().Count());
        }
        catch { robloxCount = 1; }

        if (robloxCount == 1)
        {
            SlotGame g;
            lock (_gamesLock) { g = _activeGames[_activeGames.Keys.Max()]; }
            _discord.SetInGamePresence(
                g.Name ?? "Roblox", g.IconUrl,
                g.AvatarUrl ?? _userAvatarUrl, FormatState(),
                g.Creator, g.PlaceId);
            return;
        }

        // マルチインスタンス（プロセス数ベース）
        var unique = games
            .Select(g => g.Name ?? "Roblox")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // フォーカス中スロットのアバターを使用（フォーカス不明なら最新スロット）
        SlotGame focusedGame;
        lock (_gamesLock)
        {
            focusedGame = _activeFocusedSlot >= 0 && _activeGames.TryGetValue(_activeFocusedSlot, out var fg)
                ? fg : _activeGames[_activeGames.Keys.Max()];
        }

        _discord.SetMultiGamePresence(
            unique, robloxCount,
            focusedGame.AvatarUrl ?? _userAvatarUrl,
            focusedGame.UserLabel);
    }

    private void ClearLastGameInfo()
    {
        _lastGameName    = null;
        _lastGameIconUrl = null;
        _lastGameCreator = null;
    }

    private static bool IsStudioRunning()
    {
        try
        {
            return Process.GetProcessesByName("RobloxStudioBeta").Any(p => !p.HasExited)
                || Process.GetProcessesByName("RobloxStudio").Any(p => !p.HasExited);
        }
        catch { return false; }
    }

    private void CheckStudioProcess()
    {
        try
        {
            var studioProc = Process.GetProcessesByName("RobloxStudioBeta")
                .Concat(Process.GetProcessesByName("RobloxStudio"))
                .FirstOrDefault(p => !p.HasExited);
            var running = studioProc != null;

            if (running != _studioDetected)
            {
                _studioDetected = running;
                _lastStudioPresence = string.Empty;

                if (!running)
                {
                    // Studio 終了 — ゲームか通常ページに戻す
                    if (!_gameDetected)
                    {
                        if (IsRobloxRunning)
                            _discord.SetPagePresence(CurrentPageName, _userAvatarUrl, "Roblox");
                        else
                            _discord.SetPagePresence(CurrentPageName, _userAvatarUrl);
                    }
                    return;
                }
            }

            if (!running || _gameDetected || _studioPlaytesting) return;

            // ウィンドウタイトルで Home / Editing を判定
            // Home: "Roblox Studio"  / Editing: "PlaceName - Roblox Studio"
            var title = studioProc!.MainWindowTitle;
            if (string.IsNullOrEmpty(title)) return; // Studio 起動中はタイトル未確定のためスキップ
            var newPresence = title.Contains(" - Roblox Studio") ? "Editing" : "Home";
            if (newPresence == _lastStudioPresence) return;
            _lastStudioPresence = newPresence;

            if (newPresence == "Editing")
            {
                var placeName = title.Replace(" - Roblox Studio", "").Trim();
                _discord.SetStudioPresence(_userAvatarUrl, placeName);
            }
            else
                _discord.SetStudioHomePresence(_userAvatarUrl);
        }
        catch { }
    }

    // ISO 3166-1 alpha-2 国コード → 国旗絵文字 (例: "JP" → "🇯🇵")
    // A-Z 以外の文字（Tor "T1"、Bogon "XX" など）はそのままコードを返す
    private static string ToFlagEmoji(string code)
    {
        if (code.Length != 2) return code;
        var c0 = char.ToUpperInvariant(code[0]);
        var c1 = char.ToUpperInvariant(code[1]);
        if (c0 < 'A' || c0 > 'Z' || c1 < 'A' || c1 > 'Z') return code;
        return char.ConvertFromUtf32(0x1F1E6 + (c0 - 'A'))
             + char.ConvertFromUtf32(0x1F1E6 + (c1 - 'A'));
    }

    // "🇯🇵 → 🇸🇬 Server · 12 Flags" / "🇸🇬 Server" / "12 Flags" / null
    private string? FormatState()
    {
        var s = _settings.Settings;

        var flagCount = _fastFlags.GetAll().Count;
        var flagStr   = s.DiscordShowFlagCount && flagCount > 0 ? $"{flagCount} Flags" : null;

        if (!s.DiscordShowServerRegion || _currentServerCode == null)
            return flagStr;

        var serverFlag = ToFlagEmoji(_currentServerCode);
        var server = _myCountryCode != null
            ? $"{ToFlagEmoji(_myCountryCode)} → {serverFlag} Server"
            : $"{serverFlag} Server";

        return flagStr != null ? $"{server} · {flagStr}" : server;
    }
    partial void OnIsRobloxRunningChanged(bool value)
    {
        if (!value)
        {
            // 全インスタンス終了 → スロットをすべてクリア
            lock (_gamesLock) { _activeGames.Clear(); }
            _gameDetected     = false;
            _awaitingGameInfo = false;
            _discord.Initialize(AppConstants.DiscordAppId);

            // Stretch Res: Roblox 終了時に解像度を復元
            if (_settings.Settings.StretchResolutionEnabled)
                _roblox.RestoreResolution();
        }
        else
        {
            // Stretch Res: Roblox 起動時に自動で解像度を適用
            var s = _settings.Settings;
            if (s.StretchResolutionEnabled)
                _roblox.ApplyStretchResolution(s.StretchResolutionWidth, s.StretchResolutionHeight);
        }
        if (Application.Current is App app)
            app.SetPlayingMode(value);
    }
}
