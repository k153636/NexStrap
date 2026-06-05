using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexStrap.Core.Services;

/// <summary>
/// NexStrapStudioRPC.lua プラグインからの HTTP リクエストを受信して
/// Studio の Presence データを配信する。
/// </summary>
public sealed class StudioRpcServer : IDisposable
{
    private const int Port = 4876;

    private readonly HttpListener          _listener = new();
    private readonly CancellationTokenSource _cts    = new();

    public event EventHandler<StudioRpcMessage>? MessageReceived;

    public StudioRpcServer()
    {
        _listener.Prefixes.Add($"http://localhost:{Port}/rpc/");
    }

    public void Start()
    {
        try
        {
            _listener.Start();
            _ = ListenAsync(_cts.Token);
        }
        catch { /* Discord が起動していない等で失敗しても無視 */ }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = HandleAsync(ctx);
            }
            catch (OperationCanceledException) { break; }
            catch { /* 個別エラーはループを止めない */ }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();

            var envelope = JsonSerializer.Deserialize<RpcEnvelope>(body, _jsonOptions);
            if (envelope?.Command != null)
            {
                MessageReceived?.Invoke(this, new StudioRpcMessage(envelope.Command, envelope.Data));
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.Close();
        }
        catch
        {
            try { ctx.Response.StatusCode = 400; ctx.Response.Close(); } catch { }
        }
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _listener.Close();
        _cts.Dispose();
    }

    // ── JSON モデル ──────────────────────────────────────────────────────

    private sealed class RpcEnvelope
    {
        [JsonPropertyName("command")] public string?        Command { get; init; }
        [JsonPropertyName("data")]    public StudioRpcData? Data    { get; init; }
    }
}

public sealed record StudioRpcMessage(string Command, StudioRpcData? Data);

public sealed class StudioRpcData
{
    [JsonPropertyName("details")]    public string?  Details    { get; init; }
    [JsonPropertyName("state")]      public string?  State      { get; init; }
    [JsonPropertyName("testing")]    public bool     Testing    { get; init; }
    [JsonPropertyName("scriptType")] public string?  ScriptType { get; init; }
    [JsonPropertyName("placeId")]    public long     PlaceId    { get; init; }
    [JsonPropertyName("isPublic")]   public bool     IsPublic   { get; init; }
    [JsonPropertyName("devCount")]   public int      DevCount   { get; init; }
    [JsonPropertyName("enabled")]    public bool     Enabled    { get; init; }
    [JsonPropertyName("workspace")]  public string?  Workspace  { get; init; }
}
