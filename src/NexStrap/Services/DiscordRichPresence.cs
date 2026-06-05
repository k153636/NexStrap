using System.Diagnostics;
using DiscordRPC;
using DiscordRPC.Logging;
using NexStrap.Core.Services;

namespace NexStrap.Services;

public sealed class DiscordRichPresence : IDisposable
{
    // ── RPC クライアント ────────────────────────────────────────────────

    private DiscordRpcClient? _client;
    private bool              _isConnected;
    private string            _currentAppId   = string.Empty;
    private Timestamps?       _startTimestamp;
    private Timestamps?       _gameTimestamp;
    private readonly object   _lock           = new();
    private Timer?            _debounceTimer;
    private RichPresence?     _pendingPresence;
    private string?           _userLabel;

    // ── 外部サービス ───────────────────────────────────────────────────

    private readonly SettingsService  _settings;
    private readonly RobloxApiService _robloxApi;
    private readonly FastFlagService  _fastFlags;

    // ── Presence 状態 ──────────────────────────────────────────────────

    private readonly record struct SlotGame(
        string? Name, string? IconUrl, string? Creator,
        long PlaceId, string? AvatarUrl, string? UserLabel);

    private volatile bool   _gameDetected;
    private volatile bool   _awaitingGameInfo;
    private string?         _lastGameName;
    private string?         _lastGameIconUrl;
    private string?         _lastGameCreator;
    private long            _lastPlaceId;
    private long            _currentUniverseId;
    private string?         _currentServerCode;
    private volatile bool   _studioDetected;
    private volatile bool   _studioPlaytesting;
    private volatile string _lastStudioPresence = string.Empty;
    private string?         _userAvatarUrl;
    private string?         _myCountryCode;
    private int             _activeFocusedSlot  = -1;
    private int             _currentSlotId;
    private long            _joinSequence;

    private readonly Dictionary<int, SlotGame>                     _activeGames = new();
    private readonly Dictionary<int, (string? Url, string? Label)> _slotUsers   = new();
    private readonly object _gamesLock = new();

    // ── タイマー ───────────────────────────────────────────────────────

    private Timer? _heartbeatTimer;
    private Timer? _studioTimer;

    private const int HeartbeatMs  = 15_000;
    private const int StudioPollMs =  3_000;

    // ── 公開状態 ───────────────────────────────────────────────────────

    public bool    IsConnected       => _isConnected;
    public bool    GameDetected      => _gameDetected;
    public long    CurrentUniverseId => _currentUniverseId;
    public string? CurrentPageName   { get; private set; } = "Home";

    // ── イベント ───────────────────────────────────────────────────────

    public event EventHandler<bool>?             ConnectionChanged;
    public event EventHandler<GameInfoFetchedArgs>? GameInfoFetched;
    public event EventHandler?                   SessionEnded;
    public event EventHandler?                   TeleportOccurred;

    public sealed record GameInfoFetchedArgs(
        long PlaceId, long UniverseId, string Name, string IconUrl, DateTime PlayedAt, DateTime StartedAt);

    // ── コンストラクタ ─────────────────────────────────────────────────

    public DiscordRichPresence(SettingsService settings, RobloxApiService robloxApi, FastFlagService fastFlags)
    {
        _settings  = settings;
        _robloxApi = robloxApi;
        _fastFlags = fastFlags;

        _heartbeatTimer = new Timer(async _ =>
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
        }, null, HeartbeatMs, HeartbeatMs);

        _studioTimer = new Timer(_ => CheckStudioProcess(), null, StudioPollMs, StudioPollMs);

        _ = Task.Run(async () =>
        {
            _myCountryCode = await _robloxApi.GetMyCountryAsync();
        });
    }

    // ── RPC 接続管理 ───────────────────────────────────────────────────

    public void Initialize(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId)) return;

        DiscordRpcClient? oldClient;
        lock (_lock)
        {
            if (_currentAppId == applicationId && _client != null) return;
            oldClient        = _client;
            _client          = null;
            _currentAppId    = applicationId;
            _isConnected     = false;
            _debounceTimer?.Dispose(); _debounceTimer = null;
            _pendingPresence = null;
        }

        try
        {
            Timestamps? ts = Timestamps.Now;
            lock (_lock) { _startTimestamp = ts; }
            var newClient = new DiscordRpcClient(applicationId) { Logger = new NullLogger() };

            newClient.OnReady += (_, _) =>
            {
                _isConnected = true;
                Task.Run(() =>
                {
                    ConnectionChanged?.Invoke(this, true);
                    oldClient?.Dispose();
                });
            };
            newClient.OnClose += (_, _) => { _isConnected = false; Task.Run(() => ConnectionChanged?.Invoke(this, false)); };
            newClient.OnError += (_, _) => { _isConnected = false; Task.Run(() => ConnectionChanged?.Invoke(this, false)); };

            lock (_lock) { _client = newClient; }
            newClient.Initialize();
        }
        catch { oldClient?.Dispose(); }
    }

    /// <summary>
    /// Initialize() を呼んだ後 OnReady（接続確立）まで待つ。
    /// App ID 切り替え後に確実に接続済み状態で presence を送信するために使う。
    /// タイムアウト以内に接続できなくても例外は投げない。
    /// </summary>
    private async Task InitializeAndWaitReadyAsync(string applicationId, int timeoutMs = 3000)
    {
        bool alreadyConnected;
        lock (_lock) { alreadyConnected = _currentAppId == applicationId && _client != null && _isConnected; }
        if (alreadyConnected) return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnConnected(object? _, bool connected) { if (connected) tcs.TrySetResult(true); }
        ConnectionChanged += OnConnected;

        Initialize(applicationId);

        await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        ConnectionChanged -= OnConnected;
    }

    public void SetUserLabel(string? label) { lock (_lock) { _userLabel = label; } }
    public void ResetGameTimestamp()        { lock (_lock) { _gameTimestamp = Timestamps.Now; } }
    public void SetCurrentPage(string page) => CurrentPageName = page;
    public void SetUserAvatar(string? url)  => _userAvatarUrl = url;

    // ── 外部通知（HomeViewModel から呼ぶ） ─────────────────────────────

    public void NotifyRobloxRunningChanged(bool running)
    {
        if (!running)
        {
            lock (_gamesLock) { _activeGames.Clear(); }
            _gameDetected     = false;
            _awaitingGameInfo = false;
            Initialize(AppConstants.DiscordAppId);
        }
    }

    /// <summary>Roblox 起動開始時に呼ぶ。前のセッションの残留状態をクリアする。</summary>
    public void NotifyLaunchStarted()
    {
        lock (_gamesLock) { _activeGames.Clear(); }
        _gameDetected     = false;
        _awaitingGameInfo = false;
    }

    /// <summary>PlaceJoined イベントの presence 処理を担当する。</summary>
    public async Task HandlePlaceJoinedAsync(long placeId, long universeIdFromLog, int currentSlot)
    {
        var prevDetected   = _gameDetected;
        var prevUniverseId = _currentUniverseId;
        var seq            = Interlocked.Increment(ref _joinSequence);

        _gameDetected  = true;
        _currentSlotId = currentSlot;

        long newUniverseId = universeIdFromLog;
        if (newUniverseId == 0)
        {
            try { newUniverseId = (await _robloxApi.GetUniverseIdAsync(placeId)) ?? 0; } catch { }
        }

        if (Interlocked.Read(ref _joinSequence) != seq) return;

        bool isTeleport = prevDetected && newUniverseId != 0 && newUniverseId == prevUniverseId;

        if (isTeleport)
        {
            _lastPlaceId       = placeId;
            _currentServerCode = null;

            try
            {
                var (name, iconUrl, creator) = await _robloxApi.GetGameInfoAsync(placeId, newUniverseId);
                if (Interlocked.Read(ref _joinSequence) != seq || !_gameDetected) return;

                var resolvedName    = name    ?? _lastGameName    ?? "Roblox";
                var resolvedIcon    = iconUrl ?? _lastGameIconUrl;
                var resolvedCreator = creator ?? _lastGameCreator;

                if (resolvedIcon == null) return;

                _lastGameName    = resolvedName;
                _lastGameIconUrl = resolvedIcon;
                _lastGameCreator = resolvedCreator;
                lock (_gamesLock)
                {
                    _slotUsers.TryGetValue(currentSlot, out var tpUser);
                    _activeGames[currentSlot] = new SlotGame(resolvedName, resolvedIcon, resolvedCreator, placeId, tpUser.Url, tpUser.Label);
                }
                UpdateGamePresence();
            }
            catch { }

            TeleportOccurred?.Invoke(this, EventArgs.Empty);
            return;
        }

        // ── 新規ゲームセッション ──────────────────────────────────────────
        SessionEnded?.Invoke(this, EventArgs.Empty); // HomeViewModel が前セッションを確定保存

        var startedAt       = DateTime.UtcNow;       // セッション開始時刻（履歴用）
        _currentUniverseId  = newUniverseId;
        _lastPlaceId        = placeId;
        _currentServerCode  = null;
        ClearLastGameInfo();
        _awaitingGameInfo   = true;
        ResetGameTimestamp();

        try
        {
            var (name, iconUrl, creator) = await _robloxApi.GetGameInfoAsync(placeId, newUniverseId);
            if (Interlocked.Read(ref _joinSequence) != seq || !_gameDetected) { _awaitingGameInfo = false; return; }
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
            // OnReady まで待ってから presence を送信する — 接続前に SetPresence を呼ぶと
            // discord-rpc-csharp がキューに積まず、Discord 側で更新されない問題の根本対処
            await InitializeAndWaitReadyAsync(AppConstants.DiscordRobloxAppId);
            UpdateGamePresence();

            GameInfoFetched?.Invoke(this, new GameInfoFetchedArgs(placeId, newUniverseId, name!, iconUrl!, DateTime.Now, startedAt));
        }
        catch { _awaitingGameInfo = false; }
    }

    public void HandleGameLeft(int currentSlotId, int robloxCount)
    {
        _gameDetected      = false;
        _currentUniverseId = 0;
        _currentServerCode = null;
        ClearLastGameInfo();

        bool hasRemaining;
        SlotGame remaining = default;
        lock (_gamesLock)
        {
            _activeGames.Remove(currentSlotId);
            while (_activeGames.Count > robloxCount)
                _activeGames.Remove(_activeGames.Keys.Min());
            hasRemaining = _activeGames.Count > 0;
            if (hasRemaining)
                remaining = _activeGames[_activeGames.Keys.Max()];
        }

        if (hasRemaining)
        {
            _lastGameName    = remaining.Name;
            _lastGameIconUrl = remaining.IconUrl;
            _lastGameCreator = remaining.Creator;
            _lastPlaceId     = remaining.PlaceId;
            _gameDetected    = true;
            RefreshPresence();
        }
        else
        {
            Initialize(AppConstants.DiscordAppId);
            RefreshPresence();
        }
    }

    public void NotifyUserUpdated(int slotId, string? avatarUrl, string? userLabel)
    {
        lock (_gamesLock)
        {
            _slotUsers[slotId] = (avatarUrl, userLabel);
            if (_activeGames.TryGetValue(slotId, out var game))
                _activeGames[slotId] = game with { AvatarUrl = avatarUrl, UserLabel = userLabel };
        }
        RefreshPresence();
    }

    public void NotifyServerCode(string? code)
    {
        if (code == null) return;
        _currentServerCode = code;
        bool hasGames;
        lock (_gamesLock) { hasGames = _activeGames.Count > 0; }
        if (_gameDetected && hasGames)
            UpdateGamePresence();
    }

    public void NotifyStudioPlaytestStarted()
    {
        _studioPlaytesting = true;
        RefreshPresence();
    }

    public void NotifyStudioPlaytestStopped()
    {
        _studioPlaytesting  = false;
        _lastStudioPresence = string.Empty;
    }

    public void NotifyFocusChanged(int? focusedSlot)
    {
        _activeFocusedSlot = focusedSlot ?? -1;
        if (_gameDetected) UpdateGamePresence();
    }

    // ── Presence 更新ロジック ──────────────────────────────────────────

    public void RefreshPresence()
    {
        bool hasGames;
        lock (_gamesLock) { hasGames = _activeGames.Count > 0; }

        if (hasGames)
            UpdateGamePresence();
        else if (_gameDetected)
        {
            if (_awaitingGameInfo)
                return; // API 取得完了まで何も変えない（完了後に UpdateGamePresence が呼ばれる）
            else
                _ = TryFetchGameInfoAndUpdateAsync();
        }
        else if (_studioPlaytesting)
            SetStudioPlaytestPresence(_userAvatarUrl);
        else if (_studioDetected)
            CheckStudioProcess();
        else
            SetPagePresence(CurrentPageName ?? "Home", _userAvatarUrl);
    }

    private void UpdateGamePresence()
    {
        List<SlotGame> games;
        lock (_gamesLock) { games = _activeGames.Values.ToList(); }
        if (games.Count == 0)
        {
            SetPagePresence(CurrentPageName ?? "Home", _userAvatarUrl);
            return;
        }

        int robloxCount;
        try { robloxCount = Math.Max(1, CountRobloxProcesses()); }
        catch { robloxCount = 1; }

        if (robloxCount == 1)
        {
            SlotGame g;
            lock (_gamesLock) { g = _activeGames[_activeGames.Keys.Max()]; }
            SetInGamePresence(g.Name ?? "Roblox", g.IconUrl, g.AvatarUrl ?? _userAvatarUrl, FormatState(), g.Creator, g.PlaceId);
            return;
        }

        var unique = games.Select(g => g.Name ?? "Roblox").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        SlotGame focusedGame;
        lock (_gamesLock)
        {
            focusedGame = _activeFocusedSlot >= 0 && _activeGames.TryGetValue(_activeFocusedSlot, out var fg)
                ? fg : _activeGames[_activeGames.Keys.Max()];
        }
        SetMultiGamePresence(unique, robloxCount, focusedGame.AvatarUrl ?? _userAvatarUrl, focusedGame.UserLabel);
    }

    private async Task TryFetchGameInfoAndUpdateAsync()
    {
        if (!_gameDetected || _awaitingGameInfo || _lastPlaceId == 0) return;

        var placeId    = _lastPlaceId;
        var universeId = _currentUniverseId;
        var slot       = _currentSlotId;
        var seq        = Interlocked.Read(ref _joinSequence);

        _awaitingGameInfo = true;
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
            await InitializeAndWaitReadyAsync(AppConstants.DiscordRobloxAppId);
            UpdateGamePresence();
        }
        catch { _awaitingGameInfo = false; }
    }

    private string? FormatState()
    {
        var s         = _settings.Settings;
        var flagCount = _fastFlags.GetAll().Count;
        var flagStr   = s.DiscordShowFlagCount && flagCount > 0 ? $"{flagCount} Flags" : null;

        if (!s.DiscordShowServerRegion || _currentServerCode == null)
            return flagStr;

        var serverFlag = ToFlagEmoji(_currentServerCode);
        var server     = _myCountryCode != null
            ? $"{ToFlagEmoji(_myCountryCode)} → {serverFlag} Server"
            : $"{serverFlag} Server";

        return flagStr != null ? $"{server} · {flagStr}" : server;
    }

    private void ClearLastGameInfo()
    {
        _lastGameName    = null;
        _lastGameIconUrl = null;
        _lastGameCreator = null;
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
                _studioDetected     = running;
                _lastStudioPresence = string.Empty;
                if (!running)
                {
                    if (!_gameDetected) RefreshPresence();
                    return;
                }
            }

            if (!running || _gameDetected || _studioPlaytesting) return;

            var title = studioProc!.MainWindowTitle;
            if (string.IsNullOrEmpty(title)) return;
            var newPresence = title.Contains(" - Roblox Studio") ? "Editing" : "Home";
            if (newPresence == _lastStudioPresence) return;
            _lastStudioPresence = newPresence;

            if (newPresence == "Editing")
                SetStudioPresence(_userAvatarUrl, title.Replace(" - Roblox Studio", "").Trim());
            else
                SetStudioHomePresence(_userAvatarUrl);
        }
        catch { }
    }

    private static int CountRobloxProcesses() =>
        Process.GetProcessesByName("RobloxPlayerBeta")
               .Concat(Process.GetProcessesByName("RobloxPlayer"))
               .Count();

    private static string ToFlagEmoji(string code)
    {
        if (code.Length != 2) return code;
        var c0 = char.ToUpperInvariant(code[0]);
        var c1 = char.ToUpperInvariant(code[1]);
        if (c0 < 'A' || c0 > 'Z' || c1 < 'A' || c1 > 'Z') return code;
        return char.ConvertFromUtf32(0x1F1E6 + (c0 - 'A')) + char.ConvertFromUtf32(0x1F1E6 + (c1 - 'A'));
    }

    // ── Presence 送信 ─────────────────────────────────────────────────

    public void SetPagePresence(string pageName, string? userAvatarUrl = null, string prefix = "NexStrap")
    {
        var s = _settings.Settings;
        if (!s.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(
            details:    s.DiscordShowLauncherDetails ? (prefix == "NexStrap" ? $"{prefix} / {pageName}" : prefix) : null,
            state:      null,
            largeImage: "nexstrap",
            largeText:  "NexStrap Launcher · Created by K",
            smallImage: userAvatarUrl,
            smallText:  userAvatarUrl != null ? (label ?? "Profile") : null
        );
    }

    public void SetInGamePresence(string gameName, string? gameIconUrl = null, string? userAvatarUrl = null, string? state = null, string? creator = null, long placeId = 0)
    {
        var s = _settings.Settings;
        string? label; lock (_lock) { label = _userLabel; }

        var details = s.DiscordShowCreator && creator != null ? $"{gameName} · by {creator}" : gameName;
        var buttons = s.DiscordShowJoinButton && placeId > 0
            ? new Button[] { new() { Label = "Join Game", Url = $"https://www.roblox.com/games/{placeId}" } }
            : null;
        var (largeImg, largeCaption, smallImg, smallCaption) = gameIconUrl != null
            ? (gameIconUrl, gameName,  userAvatarUrl, userAvatarUrl != null ? (label ?? "Profile") : null)
            : ("roblox",   "Roblox",   userAvatarUrl, userAvatarUrl != null ? (label ?? "Profile") : null);

        Timestamps? gameTs; lock (_lock) { gameTs = _gameTimestamp; }
        SetPresence(details: details, state: state, largeImage: largeImg ?? "roblox", largeText: largeCaption, smallImage: smallImg, smallText: smallCaption, buttons: buttons, timestamps: gameTs);
    }

    private void SetMultiGamePresence(IReadOnlyList<string> uniqueNames, int totalInstances, string? focusedAvatarUrl, string? focusedUserLabel)
    {
        SetPresence(
            details:    $"Roblox / {totalInstances} instances",
            state:      string.Join(" · ", uniqueNames),
            largeImage: "roblox",
            largeText:  "Playing Roblox",
            smallImage: focusedAvatarUrl,
            smallText:  focusedAvatarUrl != null ? (focusedUserLabel ?? "Profile") : null,
            timestamps: _startTimestamp
        );
    }

    public void SetDevPresence()
    {
        SetPresence(details: "NexStrap / Developer", state: null, largeImage: "nexstrap", largeText: "NexStrap Developer", smallImage: null, smallText: null);
    }

    public void SetLaunchingPresence(string? userAvatarUrl = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(details: "Roblox / Launching", state: null, largeImage: "nexstrap", largeText: "NexStrap Launcher · Created by K", smallImage: userAvatarUrl, smallText: userAvatarUrl != null ? (label ?? "Profile") : null);
    }

    public void SetUpdatingPresence(string? userAvatarUrl = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(details: "Roblox / Updating", state: null, largeImage: "nexstrap", largeText: "NexStrap Launcher · Created by K", smallImage: userAvatarUrl, smallText: userAvatarUrl != null ? (label ?? "Profile") : null);
    }

    private void SetStudioHomePresence(string? userAvatarUrl = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(details: "Roblox Studio", state: null, largeImage: "nexstrap", largeText: "NexStrap Launcher · Created by K", smallImage: userAvatarUrl, smallText: userAvatarUrl != null ? (label ?? "Profile") : null, timestamps: _startTimestamp);
    }

    private void SetStudioPlaytestPresence(string? userAvatarUrl = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(details: "Roblox Studio / Testing", state: null, largeImage: "nexstrap", largeText: "NexStrap Launcher · Created by K", smallImage: userAvatarUrl, smallText: userAvatarUrl != null ? (label ?? "Profile") : null, timestamps: _startTimestamp);
    }

    public void SetInstallingStudioPresence(string? userAvatarUrl = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(details: "Roblox Studio / Install", state: null, largeImage: "nexstrap", largeText: "NexStrap Launcher · Created by K", smallImage: userAvatarUrl, smallText: userAvatarUrl != null ? (label ?? "Profile") : null);
    }

    private void SetStudioPresence(string? userAvatarUrl = null, string? placeName = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        var details = string.IsNullOrEmpty(placeName) ? "Roblox Studio / Editing" : $"Roblox Studio / {placeName}";
        SetPresence(details: details, state: null, largeImage: "nexstrap", largeText: "NexStrap Launcher · Created by K", smallImage: userAvatarUrl, smallText: userAvatarUrl != null ? (label ?? "Profile") : null, timestamps: _startTimestamp);
    }

    // ── 低レベル送信 ───────────────────────────────────────────────────

    private void SetPresence(string? details, string? state, string largeImage, string largeText,
        string? smallImage, string? smallText, Button[]? buttons = null, Timestamps? timestamps = null)
    {
        Timestamps? fallbackTs; lock (_lock) { fallbackTs = _startTimestamp; }
        var presence = new RichPresence
        {
            Details    = details,
            State      = state,
            Assets     = new Assets
            {
                LargeImageKey  = largeImage,
                LargeImageText = largeText,
                SmallImageKey  = smallImage,
                SmallImageText = smallText
            },
            Timestamps = timestamps ?? fallbackTs,
            Buttons    = buttons
        };
        lock (_lock)
        {
            _pendingPresence = presence;
            if (_debounceTimer == null)
                _debounceTimer = new Timer(_ => FlushPresence(), null, 300, Timeout.Infinite);
            else
                _debounceTimer.Change(300, Timeout.Infinite);
        }
    }

    private void FlushPresence()
    {
        RichPresence? presence;
        DiscordRpcClient? client;
        lock (_lock) { presence = _pendingPresence; _pendingPresence = null; client = _client; }
        if (presence == null || client == null) return;
        try { client.SetPresence(presence); } catch { }
    }

    public void ClearPresence()
    {
        DiscordRpcClient? client;
        lock (_lock) { _debounceTimer?.Dispose(); _debounceTimer = null; _pendingPresence = null; client = _client; }
        client?.ClearPresence();
    }

    public void Disable()
    {
        DiscordRpcClient? client;
        lock (_lock)
        {
            _debounceTimer?.Dispose(); _debounceTimer = null;
            _pendingPresence = null;
            _currentAppId    = string.Empty;
            client           = _client;
            _client          = null;
            _isConnected     = false;
        }
        client?.ClearPresence();
        client?.Dispose();
        ConnectionChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose(); _heartbeatTimer = null;
        _studioTimer?.Dispose();    _studioTimer    = null;
        DiscordRpcClient? client;
        lock (_lock) { _debounceTimer?.Dispose(); _debounceTimer = null; client = _client; _client = null; _isConnected = false; }
        client?.ClearPresence();
        client?.Dispose();
    }
}
