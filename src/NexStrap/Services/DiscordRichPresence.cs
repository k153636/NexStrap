using System.Diagnostics;
using System.Threading.Channels;
using DiscordRPC;
using DiscordRPC.Logging;
using NexStrap.Core.Services;

namespace NexStrap.Services;

/// <summary>
/// Discord Rich Presence の全管理クラス。
/// 単一チャネル + 状態機械により、複数イベントの同時発火でも競合しない設計。
/// 外部から直接 presence を変更することはできない。
/// </summary>
public sealed class DiscordRichPresence : IDisposable
{
    // ══════════════════════════════════════════════════════════════════════
    // 状態フェーズ
    // ══════════════════════════════════════════════════════════════════════

    private enum Phase
    {
        NexStrapIdle,  // NexStrap のみ
        RobloxMenu,    // Roblox 起動（メニュー）
        FetchingGame,  // API 取得中
        InGame,        // ゲームプレイ中
        Studio,        // Studio 使用中
    }

    // ══════════════════════════════════════════════════════════════════════
    // イベント定義（全ての状態変化はこれを通じてキューに入れる）
    // ══════════════════════════════════════════════════════════════════════

    private abstract record Ev;
    private record EvRoblox(bool Running)                                               : Ev;
    private record EvLaunch                                                             : Ev;
    private record EvPlaceJoined(long PlaceId, long UniverseId, int Slot)              : Ev;
    private record EvGameLeft(int Slot, int RobloxCount)                               : Ev;
    private record EvGameInfo(long PlaceId, long Seq, string? Name, string? Icon, string? Creator) : Ev;
    private record EvServerCode(string Code)                                            : Ev;
    private record EvUserUpdated(int Slot, string? Url, string? Label)                 : Ev;
    private record EvAvatar(string? Url)                                               : Ev;
    private record EvLabel(string? Label)                                              : Ev;
    private record EvPage(string Name)                                                 : Ev;
    private record EvDiscordReady                                                      : Ev;
    private record EvStudio(bool Detected, string? PlaceName, bool Testing)            : Ev;
    private record EvCountry(string Code)                                              : Ev;
    private record EvHeartbeat                                                         : Ev;
    private record EvFocus(int? Slot)                                                  : Ev;
    private record EvRefresh                                                           : Ev;

    // ══════════════════════════════════════════════════════════════════════
    // 処理ループのみが読み書きする状態（外部からは読み取り専用プロパティ経由）
    // ══════════════════════════════════════════════════════════════════════

    private readonly record struct SlotGame(
        string? Name, string? IconUrl, string? Creator,
        long PlaceId, string? AvatarUrl, string? UserLabel);

    private Phase   _phase            = Phase.NexStrapIdle;
    private string  _appId            = AppConstants.DiscordAppId;
    private long    _placeId;
    private long    _universeId;
    private string? _gameName;
    private string? _gameIcon;
    private string? _gameCreator;
    private string? _serverCode;
    private string  _pageName         = "Home";
    private string? _avatarUrl;
    private string? _userLabel;
    private string? _myCountry;
    private bool    _studioDetected;
    private string? _studioPlaceName;
    private bool    _studioTesting;
    private int     _activeFocusedSlot = -1;
    private int     _currentSlot;
    private int     _robloxCount;
    private int     _fetchRetries;
    private long    _joinSeq;
    private const int MaxFetchRetries = 5;

    private readonly Dictionary<int, SlotGame>                     _games    = new();
    private readonly Dictionary<int, (string? Url, string? Label)> _users    = new();

    // ══════════════════════════════════════════════════════════════════════
    // 外部サービス
    // ══════════════════════════════════════════════════════════════════════

    private readonly SettingsService  _settings;
    private readonly RobloxApiService _robloxApi;
    private readonly FastFlagService  _fastFlags;

    // ══════════════════════════════════════════════════════════════════════
    // イベントチャネル（SingleReader で競合排除）
    // ══════════════════════════════════════════════════════════════════════

    private readonly Channel<Ev> _ch = Channel.CreateUnbounded<Ev>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly CancellationTokenSource _cts = new();

    // ══════════════════════════════════════════════════════════════════════
    // Discord RPC クライアント（処理ループ外で使うため別ロック）
    // ══════════════════════════════════════════════════════════════════════

    private DiscordRpcClient? _client;
    private bool              _rpcConnected;
    private string            _currentAppId = string.Empty;
    private Timestamps?       _startTs;
    private Timestamps?       _gameTs;
    private readonly object   _rpcLock  = new();
    private Timer?            _debounce;
    private RichPresence?     _pending;

    // ══════════════════════════════════════════════════════════════════════
    // タイマー
    // ══════════════════════════════════════════════════════════════════════

    private Timer? _heartbeatTimer;
    private Timer? _studioTimer;
    private const int HeartbeatMs  = 15_000;
    private const int StudioPollMs =  3_000;

    // ══════════════════════════════════════════════════════════════════════
    // 公開状態（読み取り専用）
    // ══════════════════════════════════════════════════════════════════════

    public bool    IsConnected       => _rpcConnected;
    public bool    GameDetected      => _phase is Phase.InGame or Phase.FetchingGame;
    public long    CurrentUniverseId => _universeId;
    public string  CurrentPageName   => _pageName;

    // ══════════════════════════════════════════════════════════════════════
    // 外部向けイベント（HomeViewModel が subscribe する）
    // ══════════════════════════════════════════════════════════════════════

    public event EventHandler<bool>?               ConnectionChanged;
    public event EventHandler<GameInfoFetchedArgs>? GameInfoFetched;
    public event EventHandler?                     SessionEnded;
    public event EventHandler?                     TeleportOccurred;

    public sealed record GameInfoFetchedArgs(
        long PlaceId, long UniverseId, string Name, string IconUrl,
        DateTime PlayedAt, DateTime StartedAt);

    // ══════════════════════════════════════════════════════════════════════
    // コンストラクタ
    // ══════════════════════════════════════════════════════════════════════

    public DiscordRichPresence(SettingsService settings, RobloxApiService robloxApi, FastFlagService fastFlags)
    {
        _settings  = settings;
        _robloxApi = robloxApi;
        _fastFlags = fastFlags;

        _ = ProcessLoopAsync(_cts.Token);

        _heartbeatTimer = new Timer(_ => Enqueue(new EvHeartbeat()), null, HeartbeatMs, HeartbeatMs);
        _studioTimer    = new Timer(_ => CheckStudioAndEnqueue(), null, StudioPollMs, StudioPollMs);

        _ = Task.Run(async () =>
        {
            var code = await _robloxApi.GetMyCountryAsync();
            if (code != null) Enqueue(new EvCountry(code));
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    // 公開 API（全て同期でキューに入れるだけ）
    // ══════════════════════════════════════════════════════════════════════

    public void EnqueueRobloxChanged(bool running)   => Enqueue(running ? new EvRoblox(true) : new EvRoblox(false));
    public void EnqueueLaunchStarted()               => Enqueue(new EvLaunch());
    public void EnqueueGameLeft(int slot, int count) => Enqueue(new EvGameLeft(slot, count));
    public void EnqueueUserUpdated(int slot, string? url, string? label) => Enqueue(new EvUserUpdated(slot, url, label));
    public void EnqueueServerCode(string? code)      { if (code != null) Enqueue(new EvServerCode(code)); }
    public void EnqueueStudioPlaytestStarted()       => Enqueue(new EvStudio(_studioDetected, _studioPlaceName, true));
    public void EnqueueStudioPlaytestStopped()       => Enqueue(new EvStudio(_studioDetected, _studioPlaceName, false));
    public void EnqueueFocusChanged(int? slot)       => Enqueue(new EvFocus(slot));
    public void EnqueueRefresh()                     => Enqueue(new EvRefresh());
    public void SetCurrentPage(string page)          => Enqueue(new EvPage(page));
    public void SetUserAvatar(string? url)           => Enqueue(new EvAvatar(url));
    public void SetUserLabel(string? label)          => Enqueue(new EvLabel(label));

    /// <summary>ゲーム参加を非同期で処理し完了を待つ必要はない。内部でシリアル処理される。</summary>
    public void EnqueuePlaceJoined(long placeId, long universeIdFromLog, int slot)
        => Enqueue(new EvPlaceJoined(placeId, universeIdFromLog, slot));

    // ── RPC 管理（Initialize は内部で処理ループから呼ぶ） ─────────────────

    public void SetDiscordEnabled(bool enabled, string? explicitAppId = null)
    {
        if (!enabled) { Disable(); return; }
        var appId = explicitAppId
            ?? (GameDetected ? AppConstants.DiscordRobloxAppId : AppConstants.DiscordAppId);
        RpcInitialize(appId);
        Enqueue(new EvRefresh());
    }

    // 特殊表示（起動中・インストール中・Dev）— フェーズに干渉しない一時オーバーレイ
    public void EnqueueLaunchingPresence()        => Enqueue(new EvOverlay(OverlayKind.Launching));
    public void EnqueueInstallingStudioPresence() => Enqueue(new EvOverlay(OverlayKind.InstallingStudio));
    public void SetDevPresence()                  => Enqueue(new EvOverlay(OverlayKind.Dev));

    private enum OverlayKind { None, Launching, InstallingStudio, Dev }
    private record EvOverlay(OverlayKind Kind) : Ev;
    private OverlayKind _overlay;

    public void ResetGameTimestamp() => Enqueue(new EvGameTimestampReset());
    private record EvGameTimestampReset : Ev;

    // ══════════════════════════════════════════════════════════════════════
    // 処理ループ（唯一の状態書き換え者）
    // ══════════════════════════════════════════════════════════════════════

    private async Task ProcessLoopAsync(CancellationToken ct)
    {
        await foreach (var ev in _ch.Reader.ReadAllAsync(ct))
        {
            try { await HandleEventAsync(ev); }
            catch { /* イベント処理の例外はループを止めない */ }
        }
    }

    private async Task HandleEventAsync(Ev ev)
    {
        switch (ev)
        {
            // ── Roblox 起動 / 終了 ──────────────────────────────────────────
            case EvRoblox { Running: true }:
                _games.Clear();
                _phase         = Phase.RobloxMenu;
                _fetchRetries  = 0;
                await SwitchAppIdAsync(AppConstants.DiscordRobloxAppId);
                ApplyPresence();
                break;

            case EvRoblox { Running: false }:
                _games.Clear();
                _phase         = Phase.NexStrapIdle;
                _universeId    = 0;
                _fetchRetries  = 0;
                await SwitchAppIdAsync(AppConstants.DiscordAppId);
                ApplyPresence();
                break;

            // ── Roblox 起動開始（前セッションをクリア） ─────────────────────
            case EvLaunch:
                _games.Clear();
                _phase         = Phase.RobloxMenu;
                _fetchRetries  = 0;
                break;

            // ── ゲーム参加 ──────────────────────────────────────────────────
            case EvPlaceJoined { PlaceId: var pid, UniverseId: var uid, Slot: var slot }:
                await HandlePlaceJoinedAsync(pid, uid, slot);
                break;

            // ── ゲーム退出 ──────────────────────────────────────────────────
            case EvGameLeft { Slot: var slot, RobloxCount: var count }:
                HandleGameLeft(slot, count);
                await SwitchAppIdAsync(
                    count > 0 ? AppConstants.DiscordRobloxAppId : AppConstants.DiscordAppId);
                _phase = count > 0
                    ? (_games.Count > 0 ? Phase.InGame : Phase.RobloxMenu)
                    : Phase.NexStrapIdle;
                ApplyPresence();
                break;

            // ── API 取得結果 ─────────────────────────────────────────────────
            case EvGameInfo { PlaceId: var pid, Seq: var seq, Name: var name, Icon: var icon, Creator: var creator }:
                if (_phase != Phase.FetchingGame) break;
                if (seq != _joinSeq) break; // 古いシーケンスは捨てる
                if (icon == null)
                {
                    _fetchRetries++;
                    if (_fetchRetries < MaxFetchRetries)
                        _phase = Phase.FetchingGame; // ハートビートでリトライ
                    else
                        _phase = Phase.RobloxMenu;   // 諦める
                    ApplyPresence();
                    break;
                }
                _gameName     = name;
                _gameIcon     = icon;
                _gameCreator  = creator;
                _fetchRetries = 0;
                _users.TryGetValue(_currentSlot, out var su);
                _games[_currentSlot] = new SlotGame(name, icon, creator, pid, su.Url, su.Label);
                _phase = Phase.InGame;
                await SwitchAppIdAsync(AppConstants.DiscordRobloxAppId);
                ApplyPresence();
                var startedAt = DateTime.UtcNow;
                GameInfoFetched?.Invoke(this, new GameInfoFetchedArgs(pid, _universeId, name!, icon!, DateTime.Now, startedAt));
                break;

            // ── サーバー国コード ─────────────────────────────────────────────
            case EvServerCode { Code: var code }:
                _serverCode = code;
                if (_phase == Phase.InGame) ApplyPresence();
                break;

            // ── ユーザー情報更新 ─────────────────────────────────────────────
            case EvUserUpdated { Slot: var slot, Url: var url, Label: var label }:
                _users[slot] = (url, label);
                if (_games.TryGetValue(slot, out var g))
                    _games[slot] = g with { AvatarUrl = url, UserLabel = label };
                if (slot == 0) { _avatarUrl = url; }
                ApplyPresence();
                break;

            case EvAvatar { Url: var url }:
                _avatarUrl = url;
                ApplyPresence();
                break;

            case EvLabel { Label: var label }:
                _userLabel = label;
                ApplyPresence();
                break;

            // ── ページ切り替え ────────────────────────────────────────────────
            case EvPage { Name: var name }:
                _pageName = name;
                ApplyPresence();
                break;

            // ── Discord 接続確立 ─────────────────────────────────────────────
            case EvDiscordReady:
                _rpcConnected = true;
                ApplyPresence(); // 接続後に現在の状態を再送
                ConnectionChanged?.Invoke(this, true);
                break;

            // ── Studio 状態 ──────────────────────────────────────────────────
            case EvStudio { Detected: var det, PlaceName: var place, Testing: var test }:
                _studioDetected  = det;
                _studioPlaceName = place;
                _studioTesting   = test;
                if (det) _overlay = OverlayKind.None; // Studio 検出時は Launching 等の overlay をクリア
                if (_phase == Phase.NexStrapIdle || _phase == Phase.Studio)
                {
                    _phase = det ? Phase.Studio : Phase.NexStrapIdle;
                    await SwitchAppIdAsync(det ? AppConstants.DiscordStudioAppId : AppConstants.DiscordAppId);
                    ApplyPresence();
                }
                break;

            // ── 国コード ─────────────────────────────────────────────────────
            case EvCountry { Code: var code }:
                _myCountry = code;
                if (_phase == Phase.InGame) ApplyPresence();
                break;

            // ── ハートビート ─────────────────────────────────────────────────
            case EvHeartbeat:
                if (_phase == Phase.InGame)
                    ApplyPresence(); // タイムスタンプ等の再送
                else if (_phase == Phase.FetchingGame && _fetchRetries < MaxFetchRetries)
                    _ = FetchGameInfoAsync(_placeId, _universeId, _joinSeq); // リトライ
                break;

            // ── フォーカス ────────────────────────────────────────────────────
            case EvFocus { Slot: var slot }:
                _activeFocusedSlot = slot ?? -1;
                if (_phase == Phase.InGame) ApplyPresence();
                break;

            // ── 強制リフレッシュ ──────────────────────────────────────────────
            case EvRefresh:
                _overlay = OverlayKind.None;
                ApplyPresence();
                break;

            // ── 特殊オーバーレイ（起動中・インストール中・Dev） ───────────────
            case EvOverlay { Kind: var kind }:
                _overlay = kind;
                ApplyPresence();
                break;

            // ── ゲームタイムスタンプリセット ──────────────────────────────────
            case EvGameTimestampReset:
                lock (_rpcLock) { _gameTs = Timestamps.Now; }
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // ゲーム参加処理（処理ループ内から呼ぶ）
    // ══════════════════════════════════════════════════════════════════════

    private async Task HandlePlaceJoinedAsync(long placeId, long universeIdFromLog, int slot)
    {
        var prevPhase    = _phase;
        var prevUniverse = _universeId;
        var seq          = ++_joinSeq;

        _currentSlot = slot;
        _phase       = Phase.FetchingGame;

        long universe = universeIdFromLog;
        if (universe == 0)
        {
            try { universe = (await _robloxApi.GetUniverseIdAsync(placeId)) ?? 0; } catch { }
        }

        if (_joinSeq != seq) return; // 新しいイベントに抜かされた

        bool isTeleport = prevPhase == Phase.InGame && universe != 0 && universe == prevUniverse;

        if (isTeleport)
        {
            _placeId = placeId;
            _serverCode = null;
            TeleportOccurred?.Invoke(this, EventArgs.Empty);
            _ = FetchGameInfoAsync(placeId, universe, seq);
            return;
        }

        // 新規セッション
        SessionEnded?.Invoke(this, EventArgs.Empty);
        _universeId   = universe;
        _placeId      = placeId;
        _serverCode   = null;
        _fetchRetries = 0;
        lock (_rpcLock) { _gameTs = Timestamps.Now; }
        ClearGameInfo();
        ApplyPresence();

        _ = FetchGameInfoAsync(placeId, universe, seq);
    }

    private async Task FetchGameInfoAsync(long placeId, long universe, long seq)
    {
        try
        {
            var (name, icon, creator) = await _robloxApi.GetGameInfoAsync(placeId, universe);
            Enqueue(new EvGameInfo(placeId, seq, name, icon, creator));
        }
        catch
        {
            Enqueue(new EvGameInfo(placeId, seq, null, null, null));
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // ゲーム退出処理（処理ループ内から呼ぶ）
    // ══════════════════════════════════════════════════════════════════════

    private void HandleGameLeft(int slot, int count)
    {
        _robloxCount = count;
        _games.Remove(slot);
        while (_games.Count > count)
            _games.Remove(_games.Keys.Min());

        ClearGameInfo();
        _universeId = 0;
        _serverCode = null;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Presence 計算・適用（状態から決定論的に計算）
    // ══════════════════════════════════════════════════════════════════════

    private void ApplyPresence()
    {
        if (!_settings.Settings.DiscordRpcEnabled) { SchedulePresence(null); return; }
        SchedulePresence(ComputePresence());
    }

    private RichPresence? ComputePresence()
    {
        var s = _settings.Settings;
        if (!s.DiscordShowLauncherPresence && _overlay != OverlayKind.Dev) { /* ラウンチャー表示オフでも Dev は表示 */ }

        // オーバーレイが設定されている場合は最優先
        if (_overlay != OverlayKind.None)
        {
            if (!s.DiscordShowLauncherPresence) return null;
            string? label; lock (_rpcLock) { label = _userLabel; }
            return _overlay switch
            {
                OverlayKind.Launching        => Build("Roblox / Launching", null, "nexstrap", "NexStrap Launcher · Created by K", _avatarUrl, label),
                OverlayKind.InstallingStudio => Build("Roblox Studio",      null, "nexstrap", "NexStrap Launcher · Created by K", _avatarUrl, label),
                OverlayKind.Dev              => Build("NexStrap / Developer", null, "nexstrap", "NexStrap Developer",             null,        null),
                _                            => null
            };
        }

        switch (_phase)
        {
            case Phase.NexStrapIdle:
            case Phase.Studio:
                if (!s.DiscordShowLauncherPresence) return null;
                return StudioOrPagePresence(s);

            case Phase.RobloxMenu:
            case Phase.FetchingGame:
                return null; // メニュー・API取得中は表示なし

            case Phase.InGame:
                return ComputeInGamePresence(s);

            default:
                return null;
        }
    }

    private RichPresence? StudioOrPagePresence(Core.Models.AppSettings s)
    {
        string? label; lock (_rpcLock) { label = _userLabel; }
        Timestamps? ts; lock (_rpcLock) { ts = _startTs; }

        if (_phase == Phase.Studio)
        {
            var details = string.IsNullOrEmpty(_studioPlaceName) ? null : $"Working on {_studioPlaceName}";
            var state   = _studioTesting ? "Testing"
                        : string.IsNullOrEmpty(_studioPlaceName) ? null
                        : "Editing";
            return Build(s.DiscordShowLauncherDetails ? details : null, state, "nexstrap",
                "NexStrap Launcher · Created by K", _avatarUrl, label, timestamps: ts);
        }

        var pageDetails = s.DiscordShowLauncherDetails ? $"NexStrap / {_pageName}" : null;
        return Build(pageDetails, null, "nexstrap", "NexStrap Launcher · Created by K",
            _avatarUrl, label);
    }

    private RichPresence? ComputeInGamePresence(Core.Models.AppSettings s)
    {
        if (_games.Count == 0) return null;

        string? label; lock (_rpcLock) { label = _userLabel; }
        Timestamps? gameTs; lock (_rpcLock) { gameTs = _gameTs; }

        int count;
        try { count = Math.Max(1, CountRobloxProcesses()); } catch { count = 1; }

        if (count == 1)
        {
            var g = _games[_games.Keys.Max()];
            var details = s.DiscordShowCreator && g.Creator != null
                ? $"{g.Name} · by {g.Creator}" : g.Name ?? "Roblox";
            var buttons = s.DiscordShowJoinButton && g.PlaceId > 0
                ? new Button[] { new() { Label = "Join Game", Url = $"https://www.roblox.com/games/{g.PlaceId}" } }
                : null;
            return Build(details, FormatState(s), g.IconUrl ?? "roblox",
                g.Name ?? "Roblox", g.AvatarUrl ?? _avatarUrl,
                g.AvatarUrl != null || _avatarUrl != null ? (g.UserLabel ?? label ?? "Profile") : null,
                buttons, gameTs);
        }

        // マルチインスタンス
        var unique = _games.Values.Select(g => g.Name ?? "Roblox")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var focused = _activeFocusedSlot >= 0 && _games.TryGetValue(_activeFocusedSlot, out var fg)
            ? fg : _games[_games.Keys.Max()];
        return Build($"Roblox / {count} instances", string.Join(" · ", unique),
            "roblox", "Playing Roblox",
            focused.AvatarUrl ?? _avatarUrl,
            focused.AvatarUrl != null || _avatarUrl != null ? (focused.UserLabel ?? label ?? "Profile") : null,
            timestamps: _startTs);
    }

    private string? FormatState(Core.Models.AppSettings s)
    {
        var flagStr = s.DiscordShowFlagCount && _fastFlags.GetAll().Count > 0
            ? $"{_fastFlags.GetAll().Count} Flags" : null;
        if (!s.DiscordShowServerRegion || _serverCode == null) return flagStr;
        var serverFlag = ToFlagEmoji(_serverCode);
        var server = _myCountry != null
            ? $"{ToFlagEmoji(_myCountry)} → {serverFlag} Server"
            : $"{serverFlag} Server";
        return flagStr != null ? $"{server} · {flagStr}" : server;
    }

    private static RichPresence Build(string? details, string? state,
        string largeImage, string largeText,
        string? smallImage, string? smallText,
        Button[]? buttons = null, Timestamps? timestamps = null)
    {
        Timestamps? ts = timestamps;
        return new RichPresence
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
            Timestamps = ts,
            Buttons    = buttons
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    // RPC 送信（単一ルート）
    // ══════════════════════════════════════════════════════════════════════

    private void SchedulePresence(RichPresence? presence)
    {
        lock (_rpcLock)
        {
            _pending = presence;
            if (_debounce == null)
                _debounce = new Timer(_ => FlushPresence(), null, 300, Timeout.Infinite);
            else
                _debounce.Change(300, Timeout.Infinite);
        }
    }

    private void FlushPresence()
    {
        RichPresence? presence;
        DiscordRpcClient? client;
        lock (_rpcLock)
        {
            presence = _pending;
            _pending = null;
            client   = _client;
        }
        if (client == null) return;
        try
        {
            if (presence == null)
            {
                client.ClearPresence();
            }
            else
            {
                // タイムスタンプの fallback を適用（RichPresence は record でないため直接変更）
                if (presence.Timestamps == null)
                {
                    Timestamps? ts; lock (_rpcLock) { ts = _startTs; }
                    presence.Timestamps = ts;
                }
                client.SetPresence(presence);
            }
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════════════════
    // App ID 切り替え（接続確立まで待つ）
    // ══════════════════════════════════════════════════════════════════════

    private async Task SwitchAppIdAsync(string appId)
    {
        bool alreadyConnected;
        lock (_rpcLock)
        {
            alreadyConnected = _currentAppId == appId && _client != null && _rpcConnected;
        }
        if (alreadyConnected) return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnReady(object? _, bool c) { if (c) tcs.TrySetResult(true); }
        ConnectionChanged += OnReady;
        RpcInitialize(appId);
        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        ConnectionChanged -= OnReady;
    }

    private void RpcInitialize(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return;

        DiscordRpcClient? oldClient;
        lock (_rpcLock)
        {
            if (_currentAppId == appId && _client != null) return;
            oldClient      = _client;
            _client        = null;
            _currentAppId  = appId;
            _rpcConnected  = false;
            _debounce?.Dispose(); _debounce = null;
            _pending       = null;
        }

        try
        {
            var ts = Timestamps.Now;
            lock (_rpcLock) { _startTs = ts; }

            var nc = new DiscordRpcClient(appId) { Logger = new NullLogger() };
            nc.OnReady += (_, _) =>
            {
                lock (_rpcLock) { _rpcConnected = true; }
                Task.Run(() =>
                {
                    Enqueue(new EvDiscordReady());
                    ConnectionChanged?.Invoke(this, true);
                    oldClient?.Dispose();
                });
            };
            nc.OnClose += (_, _) =>
            {
                lock (_rpcLock) { _rpcConnected = false; }
                Task.Run(() => ConnectionChanged?.Invoke(this, false));
            };
            nc.OnError += (_, _) =>
            {
                lock (_rpcLock) { _rpcConnected = false; }
                Task.Run(() => ConnectionChanged?.Invoke(this, false));
            };
            lock (_rpcLock) { _client = nc; }
            nc.Initialize();
        }
        catch { oldClient?.Dispose(); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Studio 監視（タイマーから呼ぶ）
    // ══════════════════════════════════════════════════════════════════════

    private string _lastStudioPresenceKey = string.Empty;
    private int    _studioHomeConfirmCount;
    private const int StudioHomeConfirmThreshold = 3; // 3 ポーリング（9秒）連続でホームが確認されたら確定

    private void CheckStudioAndEnqueue()
    {
        try
        {
            var proc = Process.GetProcessesByName("RobloxStudioBeta")
                .Concat(Process.GetProcessesByName("RobloxStudio"))
                .FirstOrDefault(p => !p.HasExited);
            var detected = proc != null;

            if (detected != _studioDetected)
            {
                _lastStudioPresenceKey  = string.Empty;
                _studioHomeConfirmCount = 0;
                Enqueue(new EvStudio(detected, null, false));
                return;
            }

            if (!detected || GameDetected || _studioTesting) return;

            var title = proc!.MainWindowTitle;
            if (string.IsNullOrEmpty(title)) return;

            if (title.Contains(" - Roblox Studio"))
            {
                // プレースが開いている — * (未保存マーク) を除去してから使う
                _studioHomeConfirmCount = 0;
                var placeName = title.Replace(" - Roblox Studio", "").Trim().TrimStart('*').Trim();
                var key       = placeName; // プレース名をキーにする
                if (key == _lastStudioPresenceKey) return;
                _lastStudioPresenceKey = key;
                Enqueue(new EvStudio(true, placeName, false));
            }
            else
            {
                // ホーム or 不明なタイトル — 一時的な変化を無視するため複数回確認
                _studioHomeConfirmCount++;
                if (_studioHomeConfirmCount < StudioHomeConfirmThreshold) return;
                if (_lastStudioPresenceKey == "home") return;
                _lastStudioPresenceKey = "home";
                Enqueue(new EvStudio(true, null, false));
            }
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════════════════
    // ヘルパー
    // ══════════════════════════════════════════════════════════════════════

    private void Enqueue(Ev ev) => _ch.Writer.TryWrite(ev);

    private void ClearGameInfo()
    {
        _gameName    = null;
        _gameIcon    = null;
        _gameCreator = null;
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

    // ══════════════════════════════════════════════════════════════════════
    // Disable / Dispose
    // ══════════════════════════════════════════════════════════════════════

    public void Disable()
    {
        DiscordRpcClient? client;
        lock (_rpcLock)
        {
            _debounce?.Dispose(); _debounce = null;
            _pending     = null;
            _currentAppId = string.Empty;
            client       = _client;
            _client      = null;
            _rpcConnected = false;
        }
        client?.ClearPresence();
        client?.Dispose();
        ConnectionChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _ch.Writer.Complete();
        _heartbeatTimer?.Dispose();
        _studioTimer?.Dispose();
        DiscordRpcClient? client;
        lock (_rpcLock)
        {
            _debounce?.Dispose(); _debounce = null;
            client       = _client; _client = null;
            _rpcConnected = false;
        }
        client?.ClearPresence();
        client?.Dispose();
        _cts.Dispose();
    }
}
