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
    private bool _wasRunning;
    private readonly object _lock = new();

    public event EventHandler<long>?   PlaceJoined;
    public event EventHandler<long>?   UserIdDetected;
    public event EventHandler?         GameLeft;
    public event EventHandler<string>? ServerIpDetected;

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

    // UDMUX Address = X.X.X.X (ゲームサーバーIP)
    private static readonly Regex UdmuxPattern =
        new(@"UDMUX Address = (\d+\.\d+\.\d+\.\d+)", RegexOptions.Compiled);

    private long   _detectedUserId;
    private string _detectedIp = string.Empty;

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
                _wasRunning = true;
                var (placeId, userId, ip) = ScanForLastPlaceIdAndUser(latest);
                if (userId > 0) { _detectedUserId = userId; UserIdDetected?.Invoke(this, userId); }
                if (placeId > 0) { _lastPlaceId = placeId; PlaceJoined?.Invoke(this, placeId); }
                if (!string.IsNullOrEmpty(ip)) { _detectedIp = ip; ServerIpDetected?.Invoke(this, ip); }
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

    public void SetBackgroundMode(bool background)
    {
        if (background)
        {
            _pollTimer?.Change(5_000, 5_000);
            _scanTimer?.Change(15_000, 15_000);
            _processTimer?.Change(20_000, 20_000);
        }
        else
        {
            _pollTimer?.Change(1_000, 1_000);
            _scanTimer?.Change(2_000, 2_000);
            _processTimer?.Change(5_000, 5_000);
        }
    }

    // 現在監視中のファイルより新しいログファイルがあれば切り替える
    private void CheckForNewLogFile()
    {
        var latest = GetLatestLogFile();
        if (latest == null) return;
        lock (_lock)
        {
            if (latest == _watchedFile) return;
            StartWatchingFileUnsafe(latest, fromEnd: false);
        }
    }

    private void StartWatchingFile(string path, bool fromEnd)
    {
        lock (_lock) { StartWatchingFileUnsafe(path, fromEnd); }
    }

    private void StartWatchingFileUnsafe(string path, bool fromEnd)
    {
        _watchedFile  = path;
        _lastPlaceId  = 0;
        _filePosition = fromEnd ? GetFileLength(path) : 0;
    }

    private (long placeId, long userId, string ip) ScanForLastPlaceIdAndUser(string path)
    {
        long foundPlace = 0, foundUser = 0;
        string foundIp = string.Empty;
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
                var rm = UdmuxPattern.Match(line);
                if (rm.Success) foundIp = rm.Groups[1].Value;
            }
        }
        catch { }
        return (foundPlace, foundUser, foundIp);
    }

    private void PollLogFile()
    {
        string? file;
        long position;
        lock (_lock) { file = _watchedFile; position = _filePosition; }

        if (file == null || !File.Exists(file)) return;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length <= position) return;

            fs.Seek(position, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            string? line;
            while ((line = reader.ReadLine()) != null)
                ProcessLine(line);

            lock (_lock)
            {
                // ファイルが切り替わっていなければ位置を更新
                if (_watchedFile == file)
                    _filePosition = fs.Position;
            }
        }
        catch { }
    }

    private void ProcessLine(string line)
    {
        long   fireUserId  = 0;
        long   firePlaceId = 0;
        bool   fireLeave   = false;
        string fireIp      = string.Empty;

        lock (_lock)
        {
            if (_detectedUserId == 0)
            {
                var um = UserIdPattern.Match(line);
                if (um.Success && long.TryParse(um.Groups[1].Value, out var uid) && uid > 0)
                {
                    _detectedUserId = uid;
                    fireUserId = uid;
                }
            }

            foreach (var pattern in PlaceIdPatterns)
            {
                var m = pattern.Match(line);
                if (!m.Success) continue;
                if (!long.TryParse(m.Groups[1].Value, out var placeId)) continue;
                if (placeId <= 0 || placeId == _lastPlaceId) continue;
                _lastPlaceId = placeId;
                firePlaceId = placeId;
                break;
            }

            var rm = UdmuxPattern.Match(line);
            if (rm.Success && rm.Groups[1].Value != _detectedIp)
            {
                _detectedIp = rm.Groups[1].Value;
                fireIp = _detectedIp;
            }

            if (firePlaceId == 0 && _lastPlaceId != 0)
            {
                foreach (var keyword in LeaveKeywords)
                {
                    if (!line.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                    _lastPlaceId = 0;
                    _detectedIp  = string.Empty;
                    fireLeave = true;
                    break;
                }
            }
        }

        // イベント発火はロック外で行う（デッドロック防止）
        if (fireUserId > 0)             UserIdDetected?.Invoke(this, fireUserId);
        if (firePlaceId > 0)            PlaceJoined?.Invoke(this, firePlaceId);
        if (!string.IsNullOrEmpty(fireIp)) ServerIpDetected?.Invoke(this, fireIp);
        if (fireLeave)                  GameLeft?.Invoke(this, EventArgs.Empty);
    }

    private void CheckProcessExit()
    {
        var running = IsRobloxRunning();
        bool shouldFireLeave;
        lock (_lock)
        {
            if (running) { _wasRunning = true; return; }
            shouldFireLeave = _wasRunning;
            if (_wasRunning) { _wasRunning = false; _lastPlaceId = 0; }
        }
        if (shouldFireLeave) GameLeft?.Invoke(this, EventArgs.Empty);
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
