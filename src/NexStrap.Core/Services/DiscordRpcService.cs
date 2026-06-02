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

    public DiscordRpcService(SettingsService settings) => _settings = settings;

    public bool IsConnected => _isConnected;
    public event EventHandler<bool>? ConnectionChanged;

    public void SetUserLabel(string? label) { lock (_lock) { _userLabel = label; } }

    public void ResetGameTimestamp() { lock (_lock) { _gameTimestamp = Timestamps.Now; } }

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
        }

        // Dispose the old client outside the lock to avoid holding the lock during blocking calls
        oldClient?.ClearPresence();
        oldClient?.Dispose();

        try
        {
            _startTimestamp = Timestamps.Now;

            var newClient = new DiscordRpcClient(applicationId) { Logger = new NullLogger() };

            newClient.OnReady += (_, _) => { _isConnected = true;  ConnectionChanged?.Invoke(this, true);  };
            newClient.OnClose += (_, _) => { _isConnected = false; ConnectionChanged?.Invoke(this, false); };
            newClient.OnError += (_, _) => { _isConnected = false; ConnectionChanged?.Invoke(this, false); };

            // Assign _client BEFORE Initialize() so FlushPresence never sees null during the handshake window
            lock (_lock) { _client = newClient; }
            newClient.Initialize();
        }
        catch { }
    }

    public void SetMediaPresence(string title, string artist, string serviceKey, string? userAvatarUrl = null)
    {
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(
            details: "Listening",
            state: string.IsNullOrEmpty(artist) ? title : $"{title} — {artist}",
            largeImage: serviceKey,
            largeText: title,
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? (label ?? "Profile") : null
        );
    }

    public void SetPagePresence(string pageName, string? userAvatarUrl = null, string prefix = "NexStrap")
    {
        var s = _settings.Settings;
        if (!s.DiscordShowLauncherPresence) { ClearPresence(); return; }

        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(
            details: s.DiscordShowLauncherDetails ? $"{prefix} / {pageName}" : null,
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
            details:    string.Join(" · ", uniqueNames),
            state:      $"{totalInstances} instances",
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
            details: "Developer Mode",
            state: "NexStrap Internal Tools",
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
            details: "Launching",
            state: null,
            largeImage: "roblox_logo1",
            largeText: "Roblox",
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? (label ?? "Profile") : null
        );
    }

    public void SetUpdatingPresence(string? userAvatarUrl = null)
    {
        if (!_settings.Settings.DiscordShowLauncherPresence) { ClearPresence(); return; }
        string? label; lock (_lock) { label = _userLabel; }
        SetPresence(
            details: "Roblox updating",
            state: null,
            largeImage: "roblox_logo1",
            largeText: "Roblox",
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? (label ?? "Profile") : null
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
        DiscordRpcClient? client;
        lock (_lock) { _debounceTimer?.Dispose(); _debounceTimer = null; client = _client; _client = null; _isConnected = false; }
        client?.ClearPresence();
        client?.Dispose();
    }
}
