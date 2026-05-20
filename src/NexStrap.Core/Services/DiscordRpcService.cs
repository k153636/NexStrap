using DiscordRPC;
using DiscordRPC.Logging;

namespace NexStrap.Core.Services;

public class DiscordRpcService : IDisposable
{
    private DiscordRpcClient? _client;
    private bool _isConnected;
    private string _currentAppId = string.Empty;
    // 起動時に一度だけ記録 — ページ切り替えでリセットしない
    private Timestamps? _startTimestamp;

    public bool IsConnected => _isConnected;
    public event EventHandler<bool>? ConnectionChanged;

    public void Initialize(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId)) return;
        if (_currentAppId == applicationId && _client?.IsInitialized == true) return;

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
                SetPagePresence("ホーム");
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

    // ホーム・ページ切り替え時 — アバターがあれば大アイコンとして表示
    public void SetPagePresence(string pageName, string? userAvatarUrl = null)
    {
        SetPresence(
            details: "Test Build",
            state: pageName,
            largeImage: userAvatarUrl ?? "roblox_logo",
            largeText: "プロフィール",
            smallImage: null,
            smallText: null
        );
    }

    // ゲームプレイ中 — マップアイコンが大、アバターが右下に小さく
    public void SetInGamePresence(string gameName, string? gameIconUrl = null, string? userAvatarUrl = null)
    {
        SetPresence(
            details: gameName,
            state: "プレイ中",
            largeImage: gameIconUrl ?? "roblox_logo",
            largeText: gameName,
            smallImage: userAvatarUrl,
            smallText: "プロフィール"
        );
    }

    // 起動中 — アバターがあれば大アイコン、なければ roblox_logo
    public void SetLaunchingPresence(string? userAvatarUrl = null)
    {
        SetPresence(
            details: "起動中...",
            state: "Roblox を起動しています",
            largeImage: userAvatarUrl ?? "roblox_logo",
            largeText: "Roblox",
            smallImage: null,
            smallText: null
        );
    }

    private void SetPresence(string details, string state, string largeImage, string largeText,
        string? smallImage, string? smallText)
    {
        if (_client == null || !_client.IsInitialized) return;

        _client.SetPresence(new RichPresence
        {
            Details = details,
            State = state,
            Assets = new Assets
            {
                LargeImageKey = largeImage,
                LargeImageText = largeText,
                SmallImageKey = smallImage ?? string.Empty,
                SmallImageText = smallText ?? string.Empty
            },
            Timestamps = _startTimestamp  // 起動時のタイムスタンプを使い回す
        });
    }

    public void ClearPresence() => _client?.ClearPresence();

    public void Dispose()
    {
        _client?.ClearPresence();
        _client?.Dispose();
    }
}
