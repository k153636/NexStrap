using System.Text.RegularExpressions;

namespace NexStrap.Core.Services;

public class RobloxLogWatcher : IDisposable
{
    private FileSystemWatcher? _dirWatcher;
    private Timer? _pollTimer;
    private string? _watchedFile;
    private long _filePosition;
    private long _lastPlaceId;

    public event EventHandler<long>? PlaceJoined;
    public event EventHandler? GameLeft;

    private static readonly Regex PlaceIdRegex = new(
        @"placeId[=:](\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "logs");

    public void Start()
    {
        if (!Directory.Exists(LogDir)) return;

        var latest = GetLatestLogFile();
        if (latest != null) StartWatchingFile(latest);

        _dirWatcher = new FileSystemWatcher(LogDir, "*.log")
        {
            EnableRaisingEvents = true
        };
        _dirWatcher.Created += (_, e) => StartWatchingFile(e.FullPath);

        _pollTimer = new Timer(_ => PollLogFile(), null, 1000, 1000);
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        _dirWatcher?.Dispose();
        _pollTimer = null;
        _dirWatcher = null;
    }

    private void StartWatchingFile(string path)
    {
        _watchedFile = path;
        _filePosition = 0;
        _lastPlaceId = 0;
    }

    private void PollLogFile()
    {
        if (_watchedFile == null || !File.Exists(_watchedFile)) return;

        try
        {
            using var fs = new FileStream(_watchedFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length <= _filePosition) return;

            fs.Seek(_filePosition, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);

            string? line;
            while ((line = reader.ReadLine()) != null)
                ProcessLine(line);

            _filePosition = fs.Position;
        }
        catch { }
    }

    private void ProcessLine(string line)
    {
        var match = PlaceIdRegex.Match(line);
        if (match.Success &&
            long.TryParse(match.Groups[1].Value, out var placeId) &&
            placeId > 0 &&
            placeId != _lastPlaceId)
        {
            _lastPlaceId = placeId;
            PlaceJoined?.Invoke(this, placeId);
        }

        if (line.Contains("Disconnect", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("game left", StringComparison.OrdinalIgnoreCase))
        {
            if (_lastPlaceId != 0)
            {
                _lastPlaceId = 0;
                GameLeft?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static string? GetLatestLogFile()
    {
        if (!Directory.Exists(LogDir)) return null;
        return Directory.GetFiles(LogDir, "*.log")
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
    }

    public void Dispose() => Stop();
}
