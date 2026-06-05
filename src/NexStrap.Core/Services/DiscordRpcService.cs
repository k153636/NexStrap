using DiscordRPC;
using DiscordRPC.Logging;

namespace NexStrap.Core.Services;

public class DiscordRpcService : IDisposable
{
    private readonly SettingsService _settings;
    private DiscordRpcClient? _client;
    private bool _isConnected;
    private string _currentAppId = string.Empty;
    private Timestamps? _startTimestamp;
    private Timestamps? _gameTimestamp;
    private readonly object _lock = new();
    private Timer? _debounceTimer;
    private RichPresence? _pendingPresence;
    private string? _userLabel;

    // ウォームアップ用 — ゲーム参加の API 待機中に次の App ID の接続を事前確立しておく
    private DiscordRpcClient? _warmupClient;
    private string            _warmupAppId  = string.Empty;
    private volatile bool     _warmupReady  = false;

    public DiscordRpcService(SettingsService settings) => _settings = settings;

    public bool IsConnected => _isConnected;
    public event EventHandler<bool>? ConnectionChanged;

    public void SetUserLabel(string? label) { lock (_lock) { _userLabel = label; } }

    public void ResetGameTimestamp() { lock (_lock) { _gameTimestamp = Timestamps.Now; } }

    /// <summary>
    /// 次に使う App ID の Discord 接続を事前確立する。
    /// PlaceJoined の API 待機が始まる前に呼ぶことで、API 完了時には接続済みになり切り替え空白がゼロになる。
    /// </summary>
    public void WarmupNextClient(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId)) return;

        DiscordRpcClient? oldWarmup = null;
        lock (_lock)
        {
            // 既にアクティブ or 既に同 ID でウォームアップ中なら不要
            if ((_currentAppId == applicationId && _client != null) ||
                (_warmupAppId  == applicationId && _warmupClient != null))
                return;

            oldWarmup     = _warmupClient;
            _warmupClient = null;
            _warmupAppId  = string.Empty;
            _warmupReady  = false;
        }
        oldWarmup?.Dispose();

        var client = new DiscordRpcClient(applicationId) { Logger = new NullLogger() };
        // OnReady が来たらウォームアップ完了フラグを立てる（presence は設定しない）
        client.OnReady += (_, _) =>
        {
            lock (_lock) { if (_warmupClient == client) _warmupReady = true; }
        };

        lock (_lock) { _warmupClient = client; _warmupAppId = applicationId; }
        client.Initialize();
    }

    public void Initialize(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId)) return;

        DiscordRpcClient? oldClient;
        DiscordRpcClient? warmup     = null;
        bool              warmupDone = false;

        lock (_lock)
        {
            if (_currentAppId == applicationId && _client != null) return;

            // ウォームアップ済みクライアントを引き取る
            if (_warmupAppId == applicationId && _warmupClient != null)
            {
                warmup        = _warmupClient;
                warmupDone    = _warmupReady;
                _warmupClient = null;
                _warmupAppId  = string.Empty;
                _warmupReady  = false;
            }

            oldClient     = _client;
            _client       = null;
            _currentAppId = applicationId;
            _isConnected  = false;
        }

        if (warmup != null)
        {
            // ── ウォームアップクライアントをアクティブに昇格 ──────────────────────
            warmup.OnClose += (_, _) => { _isConnected = false; ConnectionChanged?.Invoke(this, false); };
            warmup.OnError += (_, _) => { _isConnected = false; ConnectionChanged?.Invoke(this, false); };

            if (warmupDone)
            {
                // 既に接続確立済み — 旧クライアントを今すぐ切っても新クライアントの presence が即送信できる
                lock (_lock) { _client = warmup; _isConnected = true; }
                _startTimestamp = Timestamps.Now;
                oldClient?.Dispose();
            }
            else
            {
                // まだ OnReady 未受信 — OnReady が来てから旧クライアントを切る（空白ゼロ保証）
                warmup.OnReady += (_, _) =>
                {
                    lock (_lock) { if (_client != warmup) return; _isConnected = true; }
                    ConnectionChanged?.Invoke(this, true);
                    oldClient?.Dispose(); // 新接続確立後に旧クライアントを切断
                };
                lock (_lock) { _client = warmup; }
            }
        }
        else
        {
            // ── ウォームアップなし — 新クライアントの OnReady 後に旧クライアントを切る ──
            try
            {
                _startTimestamp = Timestamps.Now;
                var newClient = new DiscordRpcClient(applicationId) { Logger = new NullLogger() };

                newClient.OnReady += (_, _) =>
                {
                    _isConnected = true;
                    ConnectionChanged?.Invoke(this, true);
                    oldClient?.Dispose(); // 新接続確立後に旧クライアントを切断
                };
                newClient.OnClose += (_, _) => { _isConnected = false; ConnectionChanged?.Invoke(this, false); };
                newClient.OnError += (_, _) => { _isConnected = false; ConnectionChanged?.Invoke(this, false); };

                lock (_lock) { _client = newClient; }
                newClient.Initialize();
            }
            catch { oldClient?.Dispose(); } // 例外時は即 Dispose
        }
    }

public void SetPagePresence(string pageName, string? userAvatarUrl = null, string prefix = "NexStrap")
    {
        var s = _settings.Settings;
        if (!s.DiscordShowLauncherPresence) { ClearPresence(); return; }

        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(
            details: s.DiscordShowLauncherDetails ? (prefix == "NexStrap" ? $"{prefix} / {pageName}" : prefix) : null,
            state: null,
            largeImage: "nexstrap",
            largeText: "NexStrap Launcher · Created by K",
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? (label ?? "Profile") : null
        );
    }

    // ゲームプレイ中 — マップアイコンが大、アバターが右下に小さく
    public void SetInGamePresence(string gameName, string? gameIconUrl = null, string? userAvatarUrl = null, string? state = null, string? creator = null, long placeId = 0)
    {
        var s = _settings.Settings;
        string? label; lock (_lock) { label = _userLabel; }

        var details = s.DiscordShowCreator && creator != null
            ? $"{gameName} · by {creator}"
            : gameName;
        var buttons = s.DiscordShowJoinButton && placeId > 0
            ? new Button[] { new() { Label = "Join Game", Url = $"https://www.roblox.com/games/{placeId}" } }
            : null;

        // gameIconUrl がない場合も large = roblox キー（アバターを large にしない）
        var (largeImg, largeCaption, smallImg, smallCaption) = gameIconUrl != null
            ? (gameIconUrl, gameName, userAvatarUrl, userAvatarUrl != null ? (label ?? "Profile") : null)
            : ("roblox",    "Roblox",  userAvatarUrl, userAvatarUrl != null ? (label ?? "Profile") : null);

        Timestamps? gameTs; lock (_lock) { gameTs = _gameTimestamp; }
        SetPresence(
            details: details,
            state: state,
            largeImage: largeImg ?? "roblox",
            largeText: largeCaption,
            smallImage: smallImg,
            smallText: smallCaption,
            buttons: buttons,
            timestamps: gameTs
        );
    }

    public void SetMultiGamePresence(
        IReadOnlyList<string> uniqueNames,
        int totalInstances,
        string? focusedAvatarUrl,
        string? focusedUserLabel)
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
        SetPresence(
            details: "NexStrap / Developer",
            state: null,
            largeImage: "nexstrap",
            largeText: "NexStrap Developer",
            smallImage: null,
            smallText: null
        );
    }

    public void SetLaunchingPresence(string? userAvatarUrl = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(
            details: "Roblox / Launching",
            state: null,
            largeImage: "nexstrap",
            largeText: "NexStrap Launcher · Created by K",
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? (label ?? "Profile") : null
        );
    }

    public void SetUpdatingPresence(string? userAvatarUrl = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(
            details: "Roblox / Updating",
            state: null,
            largeImage: "nexstrap",
            largeText: "NexStrap Launcher · Created by K",
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? (label ?? "Profile") : null
        );
    }

    public void SetStudioHomePresence(string? userAvatarUrl = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(
            details: "Roblox Studio",
            state: null,
            largeImage: "nexstrap",
            largeText: "NexStrap Launcher · Created by K",
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? (label ?? "Profile") : null,
            timestamps: _startTimestamp
        );
    }

    public void SetStudioPlaytestPresence(string? gameName = null, string? userAvatarUrl = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(
            details: "Roblox Studio / Testing",
            state: null,
            largeImage: "nexstrap",
            largeText: "NexStrap Launcher · Created by K",
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? (label ?? "Profile") : null,
            timestamps: _startTimestamp
        );
    }

    public void SetInstallingStudioPresence(string? userAvatarUrl = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(
            details: "Roblox Studio / Install",
            state: null,
            largeImage: "nexstrap",
            largeText: "NexStrap Launcher · Created by K",
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? (label ?? "Profile") : null
        );
    }

    public void SetStudioPresence(string? userAvatarUrl = null, string? placeName = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        var details = string.IsNullOrEmpty(placeName) ? "Roblox Studio / Editing" : $"Roblox Studio / {placeName}";
        SetPresence(
            details: details,
            state: null,
            largeImage: "nexstrap",
            largeText: "NexStrap Launcher · Created by K",
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? (label ?? "Profile") : null,
            timestamps: _startTimestamp
        );
    }

    private void SetPresence(string? details, string? state, string largeImage, string largeText,
        string? smallImage, string? smallText, Button[]? buttons = null, Timestamps? timestamps = null)
    {
        var presence = new RichPresence
        {
            Details = details,
            State   = state,
            Assets  = new Assets
            {
                LargeImageKey  = largeImage,
                LargeImageText = largeText,
                SmallImageKey  = smallImage,
                SmallImageText = smallText
            },
            Timestamps = timestamps ?? _startTimestamp,
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
        bool connected;
        lock (_lock)
        {
            presence = _pendingPresence;
            _pendingPresence = null;
            client    = _client;
            connected = _isConnected;
        }
        if (presence == null || client == null) return;

        if (!connected)
        {
            // 接続確立前 — pending に戻して OnReady → ConnectionChanged → RefreshPresence() で再送させる
            lock (_lock) { _pendingPresence ??= presence; }
            return;
        }

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
        DiscordRpcClient? warmup;
        lock (_lock)
        {
            _debounceTimer?.Dispose(); _debounceTimer = null;
            _pendingPresence = null;
            _currentAppId    = string.Empty;
            client           = _client;
            _client          = null;
            _isConnected     = false;
            warmup           = _warmupClient;
            _warmupClient    = null;
            _warmupAppId     = string.Empty;
            _warmupReady     = false;
        }
        client?.ClearPresence();
        client?.Dispose();
        warmup?.Dispose();
        ConnectionChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        DiscordRpcClient? client;
        DiscordRpcClient? warmup;
        lock (_lock)
        {
            _debounceTimer?.Dispose(); _debounceTimer = null;
            client        = _client;  _client        = null;
            warmup        = _warmupClient; _warmupClient = null;
            _isConnected  = false;
        }
        client?.ClearPresence();
        client?.Dispose();
        warmup?.Dispose();
    }
}
