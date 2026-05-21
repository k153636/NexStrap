using DiscordRPC;
using DiscordRPC.Logging;

namespace NexStrap.Core.Services;

public class DiscordRpcService : IDisposable
{
    private DiscordRpcClient? _client;
    private bool _isConnected;
    private string _currentAppId = string.Empty;
    private Timestamps? _startTimestamp;
    private readonly object _lock = new();
    // デバウンス: 最後のプレゼンス更新から300ms以内の重複を抑制
    private Timer? _debounceTimer;
    private RichPresence? _pendingPresence;

    public bool IsConnected => _isConnected;
    public event EventHandler<bool>? ConnectionChanged;

    public void Initialize(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId)) return;
        lock (_lock)
        {
            if (_currentAppId == applicationId && _client?.IsInitialized == true) return;
        }

        _client?.ClearPresence();
        _client?.Dispose();
        _client = null;
        _isConnected = false;

        try
        {
            _currentAppId = applicationId;
            _startTimestamp = Timestamps.Now; // 起動タイマーをここで固定

            _client = new DiscordRpcClient(applicationId)
            {
                Logger = new NullLogger()
            };

            _client.OnReady += (_, _) =>
            {
                _isConnected = true;
                ConnectionChanged?.Invoke(this, true);
            };

            _client.OnClose += (_, _) =>
            {
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
            };

            _client.OnError += (_, _) =>
            {
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
            };

            _client.Initialize();
        }
        catch { }
    }

    // メディア再生中 (SMTC)
    public void SetMediaPresence(string title, string artist, string serviceKey, string? userAvatarUrl = null)
    {
        SetPresence(
            details: "視聴中",
            state: string.IsNullOrEmpty(artist) ? title : $"{title} — {artist}",
            largeImage: serviceKey,
            largeText: title,
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? "プロフィール" : null
        );
    }

    // ホーム・ページ切り替え時 — NexStrapアイコンが大、アバターが小
    public void SetPagePresence(string pageName, string? userAvatarUrl = null, string prefix = "NexStrap")
    {
        SetPresence(
            details: $"{prefix} / {pageName}",
            state: null,
            largeImage: "nexstrap",
            largeText: "NexStrap Launcher・Created by K",
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? "プロフィール" : null
        );
    }

    // ゲームプレイ中 — マップアイコンが大、アバターが右下に小さく
    public void SetInGamePresence(string gameName, string? gameIconUrl = null, string? userAvatarUrl = null, string? state = null, string? creator = null, long placeId = 0)
    {
        var buttons = placeId > 0
            ? new Button[] { new() { Label = "Join Game", Url = $"https://www.roblox.com/games/{placeId}" } }
            : null;

        SetPresence(
            details: creator != null ? $"{gameName}・by {creator}" : gameName,
            state: state,
            largeImage: gameIconUrl ?? "roblox_logo1",
            largeText: "Roblox",
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? "プロフィール" : null,
            buttons: buttons
        );
    }

    // 作者専用開発者ページ
    public void SetDevPresence()
    {
        SetPresence(
            details: "開発者モード",
            state: "NexStrap 内部ツール",
            largeImage: "nexstrap",
            largeText: "NexStrap Developer",
            smallImage: null,
            smallText: null
        );
    }

    // 起動中 — Robloxロゴが大、アバターが小
    public void SetLaunchingPresence(string? userAvatarUrl = null)
    {
        SetPresence(
            details: "起動中",
            state: null,
            largeImage: "roblox_logo1",
            largeText: "Roblox",
            smallImage: userAvatarUrl,
            smallText: userAvatarUrl != null ? "プロフィール" : null
        );
    }

    private void SetPresence(string details, string? state, string largeImage, string largeText,
        string? smallImage, string? smallText, Button[]? buttons = null)
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
            Timestamps = _startTimestamp,
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
        lock (_lock) { presence = _pendingPresence; _pendingPresence = null; }
        if (presence == null) return;
        try { _client?.SetPresence(presence); } catch { }
    }

    public void ClearPresence()
    {
        lock (_lock) { _debounceTimer?.Dispose(); _debounceTimer = null; _pendingPresence = null; }
        _client?.ClearPresence();
    }

    public void Disable()
    {
        lock (_lock) { _debounceTimer?.Dispose(); _debounceTimer = null; _pendingPresence = null; _currentAppId = string.Empty; }
        _client?.ClearPresence();
        _client?.Dispose();
        _client = null;
        _isConnected = false;
    }

    public void Dispose()
    {
        lock (_lock) { _debounceTimer?.Dispose(); _debounceTimer = null; }
        _client?.ClearPresence();
        _client?.Dispose();
    }
}
