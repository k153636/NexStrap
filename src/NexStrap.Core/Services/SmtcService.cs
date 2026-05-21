using Windows.Media.Control;

namespace NexStrap.Core.Services;

public class SmtcService : IDisposable
{
    public record MediaInfo(string Title, string Artist, string ServiceKey, string ServiceName);

    public event EventHandler<MediaInfo>? MediaChanged;
    public event EventHandler? MediaStopped;

    private Timer? _timer;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private MediaInfo? _lastInfo;
    private bool _lastWasPlaying;

    private static readonly (string suffix, string key, string name)[] ServiceSuffixes =
    [
        (" - YouTube",          "youtube",   "YouTube"),
        (" — YouTube",     "youtube",   "YouTube"),
        (" | NicoNico Video",   "nicovideo", "NicoNico"),
        (" - ニコニコ動画",     "nicovideo", "NicoNico"),
    ];

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _timer = new Timer(_ => _ = PollAsync(), null, 0, 1500);
        }
        catch { }
    }

    private async Task PollAsync()
    {
        if (_manager == null) return;
        try
        {
            var session = _manager.GetCurrentSession();
            if (session == null)
            {
                FireStopped();
                return;
            }

            var playback = session.GetPlaybackInfo();
            if (playback.PlaybackStatus !=
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                FireStopped();
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            var rawTitle = props.Title ?? string.Empty;
            var artist   = props.Artist ?? string.Empty;
            var appId    = session.SourceAppUserModelId ?? string.Empty;

            var (title, key, name) = ParseService(rawTitle, appId);
            var info = new MediaInfo(title, artist, key, name);

            if (info == _lastInfo) return;
            _lastInfo = info;
            _lastWasPlaying = true;
            MediaChanged?.Invoke(this, info);
        }
        catch { }
    }

    private void FireStopped()
    {
        if (!_lastWasPlaying) return;
        _lastWasPlaying = false;
        _lastInfo = null;
        MediaStopped?.Invoke(this, EventArgs.Empty);
    }

    private static (string title, string key, string name) ParseService(string rawTitle, string appId)
    {
        foreach (var (suffix, key, name) in ServiceSuffixes)
        {
            if (rawTitle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return (rawTitle[..^suffix.Length].Trim(), key, name);
        }

        var appLower = appId.ToLower();
        if (appLower.Contains("youtube"))   return (rawTitle, "youtube",   "YouTube");
        if (appLower.Contains("nicovideo")) return (rawTitle, "nicovideo", "NicoNico");
        if (appLower.Contains("spotify"))   return (rawTitle, "nexstrap",  "Spotify");

        return (rawTitle, "nexstrap", string.Empty);
    }

    public void SetBackgroundMode(bool background)
        => _timer?.Change(0, background ? 8_000 : 1_500);

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();
}
