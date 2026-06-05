using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexStrap.Core.Services;

/// <summary>
/// NexStrapStudioRPC.lua プラグインからの HTTP POST を受信し、
/// Studio の Presence データを <see cref="MessageReceived"/> イベントで配信する。
/// </summary>
public sealed class StudioRpcServer : IDisposable
{
    // ── 設定 ──────────────────────────────────────────────────────────────

    private const int    PrimaryPort  = 4876;
    private const int    MaxBodyBytes = 8 * 1024;   // 8 KB — プラグインのペイロードは必ずこれ以下
    private const double PluginTimeoutSeconds = 30; // この秒数メッセージが来なければ未接続扱い

    // ── フィールド ────────────────────────────────────────────────────────

    private readonly HttpListener             _listener = new();
    private readonly CancellationTokenSource  _cts      = new();
    private          DateTime                 _lastSeen = DateTime.MinValue;

    // ── 公開プロパティ ────────────────────────────────────────────────────

    /// <summary>プラグインが最近（<see cref="PluginTimeoutSeconds"/> 秒以内）通信してきたか。</summary>
    public bool IsPluginConnected
        => (DateTime.UtcNow - _lastSeen).TotalSeconds < PluginTimeoutSeconds;

    /// <summary>実際にリッスン中のポート番号。</summary>
    public int Port { get; private set; }

    // ── イベント ──────────────────────────────────────────────────────────

    public event EventHandler<StudioRpcMessage>? MessageReceived;

    // ── 起動 / 停止 ───────────────────────────────────────────────────────

    /// <summary>
    /// HTTP サーバーを起動する。ポートが使用中なら起動をスキップしてログに記録する。
    /// </summary>
    public void Start()
    {
        try
        {
            _listener.Prefixes.Add($"http://localhost:{PrimaryPort}/rpc/");
            _listener.Start();
            Port = PrimaryPort;
            Logger.Instance.Info("StudioRPC", $"リッスン開始 port={Port}");
            _ = AcceptLoopAsync(_cts.Token);
        }
        catch (HttpListenerException ex)
        {
            Logger.Instance.Warning("StudioRPC", $"起動失敗 (port={PrimaryPort}): {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error("StudioRPC", $"予期しない起動エラー: {ex.Message}");
        }
    }

    // ── 受信ループ ────────────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                // GetContextAsync は CancellationToken を直接受け取れないため
                // キャンセル時は Stop() で例外を発生させて抜ける
                ctx = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException)                                  { break; }
            catch { continue; }

            _ = HandleRequestAsync(ctx, ct);
        }
    }

    // ── リクエスト処理 ────────────────────────────────────────────────────

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            // POST 以外は弾く
            if (!ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                Respond(ctx, HttpStatusCode.MethodNotAllowed);
                return;
            }

            // ボディ読み取り（上限付き）
            var body = await ReadBodyAsync(ctx.Request.InputStream, MaxBodyBytes, ct);
            if (body is null)
            {
                Respond(ctx, HttpStatusCode.RequestEntityTooLarge);
                return;
            }

            var envelope = JsonSerializer.Deserialize<RpcEnvelope>(body, JsonOpts);
            if (envelope?.Command is null)
            {
                Respond(ctx, HttpStatusCode.BadRequest);
                return;
            }

            _lastSeen = DateTime.UtcNow;

            var label = envelope.Command == "SetRichPresence" && envelope.Data?.Details is { } d
                ? $" ({d})" : string.Empty;
            Logger.Instance.Info("StudioRPC", $"{envelope.Command}{label}");

            MessageReceived?.Invoke(this, new StudioRpcMessage(envelope.Command, envelope.Data));
            Respond(ctx, HttpStatusCode.OK);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Instance.Error("StudioRPC", $"リクエスト処理エラー: {ex.Message}");
            try { Respond(ctx, HttpStatusCode.InternalServerError); } catch { }
        }
    }

    // ── ヘルパー ──────────────────────────────────────────────────────────

    private static async Task<string?> ReadBodyAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream(maxBytes);
        var buf      = new byte[4096];
        int total    = 0;
        int read;

        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            total += read;
            if (total > maxBytes) return null; // 上限超過
            ms.Write(buf, 0, read);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void Respond(HttpListenerContext ctx, HttpStatusCode status)
    {
        try
        {
            ctx.Response.StatusCode = (int)status;
            ctx.Response.Close();
        }
        catch { }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Dispose ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); }  catch { }
        try { _listener.Close(); } catch { }
        _cts.Dispose();
        Logger.Instance.Info("StudioRPC", "サーバー停止");
    }

    // ── 内部 JSON モデル ──────────────────────────────────────────────────

    private sealed class RpcEnvelope
    {
        [JsonPropertyName("command")] public string?        Command { get; init; }
        [JsonPropertyName("data")]    public StudioRpcData? Data    { get; init; }
    }
}

// ── 公開モデル ─────────────────────────────────────────────────────────────

public sealed record StudioRpcMessage(string Command, StudioRpcData? Data);

public sealed record StudioRpcData
{
    [JsonPropertyName("details")]  public string? Details  { get; init; }
    [JsonPropertyName("testing")]  public bool    Testing  { get; init; }
    [JsonPropertyName("placeId")]  public long    PlaceId  { get; init; }
    [JsonPropertyName("isPublic")] public bool    IsPublic { get; init; }
    [JsonPropertyName("version")]  public string? Version  { get; init; }
    // RPCToggle 用
    [JsonPropertyName("enabled")]    public bool    Enabled   { get; init; }
    [JsonPropertyName("workspace")]  public string? Workspace { get; init; }
}
