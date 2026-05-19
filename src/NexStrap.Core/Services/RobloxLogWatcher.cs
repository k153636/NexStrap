using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NexStrap.Core.Services;

public class RobloxLogWatcher : IDisposable
{
    private Timer? _pollTimer;
    private Timer? _scanTimer;
    private Timer? _processTimer;
    private string? _watchedFile;
    private long _filePosition;
    private long _lastPlaceId;

    public event EventHandler<long>? PlaceJoined;
    public event EventHandler<long>? UserIdDetected;
    public event EventHandler? GameLeft;

    private static readonly Regex[] PlaceIdPatterns =
    [
        // placeid:123456789  (GameJoinLoadTime log line)
        new(@"placeid:(\d+)",             RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // placeId=123  or  placeId:123
        new(@"placeId[=:](\d+)",          RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // ! Joining game '...' place 123456789 at ...
        new(@"\bplace\s+(\d{7,})\b",      RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // "placeId" : 123  (JSON format)
        new(@"""placeId""\s*:\s*(\d+)",   RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    // GameJoinLoadTime 行に含まれる userid:XXXXXXXXXX
    private static readonly Regex UserIdPattern =
        new(@"\buserid:(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private long _detectedUserId;

    private static readonly string[] LeaveKeywords =
    [
        "Disconnect was called",
        "GameDisconnect",
        "game left",
        "reportGameDisconnect",
        "Game disconnect",
        "Disconnecting",
        "leaving game",
        "returnToLuaApp",
    ];

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "logs");

    public void Start()
    {
        if (!Directory.Exists(LogDir)) return;

        var latest = GetLatestLogFile();
        if (latest != null)
        {
            if (IsRobloxRunning())
            {
                var (placeId, userId) = ScanForLastPlaceIdAndUser(latest);
                if (userId > 0) { _detectedUserId = userId; UserIdDetected?.Invoke(this, userId); }
                if (placeId > 0) { _lastPlaceId = placeId; PlaceJoined?.Invoke(this, placeId); }
            }
            StartWatchingFile(latest, fromEnd: true);
        }

        // ログ行ポーリング（1秒ごと）
        _pollTimer = new Timer(_ => PollLogFile(), null, 1000, 1000);

        // 新ログファイル検知（FileSystemWatcherの代わりに定期スキャン、2秒ごと）
        _scanTimer = new Timer(_ => CheckForNewLogFile(), null, 2000, 2000);

        // プロセス終了フォールバック（5秒ごと）
        _processTimer = new Timer(_ => CheckProcessExit(), null, 5000, 5000);
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        _scanTimer?.Dispose();
        _processTimer?.Dispose();
        _pollTimer    = null;
        _scanTimer    = null;
        _processTimer = null;
    }

    // 現在監視中のファイルより新しいログファイルがあれば切り替える
    private void CheckForNewLogFile()
    {
        var latest = GetLatestLogFile();
        if (latest == null || latest == _watchedFile) return;

        // 新しいファイルに切り替え（先頭から読む）
        StartWatchingFile(latest, fromEnd: false);
    }

    private void StartWatchingFile(string path, bool fromEnd)
    {
        _watchedFile  = path;
        _lastPlaceId  = 0;
        _filePosition = fromEnd ? GetFileLength(path) : 0;
    }

    private (long placeId, long userId) ScanForLastPlaceIdAndUser(string path)
    {
        long foundPlace = 0, foundUser = 0;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                foreach (var pattern in PlaceIdPatterns)
                {
                    var m = pattern.Match(line);
                    if (m.Success && long.TryParse(m.Groups[1].Value, out var id) && id > 0)
                    { foundPlace = id; break; }
                }
                if (foundUser == 0)
                {
                    var um = UserIdPattern.Match(line);
                    if (um.Success && long.TryParse(um.Groups[1].Value, out var uid) && uid > 0)
                        foundUser = uid;
                }
            }
        }
        catch { }
        return (foundPlace, foundUser);
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
        // userid: 抽出 (GameJoinLoadTime 行に含まれる)
        if (_detectedUserId == 0)
        {
            var um = UserIdPattern.Match(line);
            if (um.Success && long.TryParse(um.Groups[1].Value, out var uid) && uid > 0)
            {
                _detectedUserId = uid;
                UserIdDetected?.Invoke(this, uid);
            }
        }

        foreach (var pattern in PlaceIdPatterns)
        {
            var m = pattern.Match(line);
            if (!m.Success) continue;
            if (!long.TryParse(m.Groups[1].Value, out var placeId)) continue;
            if (placeId <= 0 || placeId == _lastPlaceId) continue;

            _lastPlaceId = placeId;
            PlaceJoined?.Invoke(this, placeId);
            return;
        }

        if (_lastPlaceId != 0)
        {
            foreach (var keyword in LeaveKeywords)
            {
                if (!line.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                _lastPlaceId = 0;
                GameLeft?.Invoke(this, EventArgs.Empty);
                return;
            }
        }
    }

    private void CheckProcessExit()
    {
        if (_lastPlaceId == 0) return;
        if (!IsRobloxRunning())
        {
            _lastPlaceId = 0;
            GameLeft?.Invoke(this, EventArgs.Empty);
        }
    }

    public static bool IsRobloxRunning()
    {
        try
        {
            return Process.GetProcessesByName("RobloxPlayerBeta").Any(p => !p.HasExited)
                || Process.GetProcessesByName("RobloxPlayer").Any(p => !p.HasExited);
        }
        catch { return false; }
    }

    private static long GetFileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
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
