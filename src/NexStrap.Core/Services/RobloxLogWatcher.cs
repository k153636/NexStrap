using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NexStrap.Core.Services;

public class RobloxLogWatcher : IDisposable
{
    private Timer? _pollTimer;
    private Timer? _scanTimer;
    private Timer? _processTimer;
    private string? _watchedFile;
    private uint    _watchedFilePid;
    private long _filePosition;
    private long _lastPlaceId;
    private bool _wasRunning;
    private bool _isBackgroundMode;
    private bool _isPlayingMode;
    private readonly object _lock = new();
    private readonly Func<bool> _isRobloxRunningFunc;

    public uint CurrentEventSourcePid { get; private set; }

    public event EventHandler<(long placeId, long universeId)>? PlaceJoined;
    public event EventHandler<long>?   UserIdDetected;
    public event EventHandler?         GameLeft;
    public event EventHandler<string>? ServerIpDetected;
    public event EventHandler?         StudioPlaySoloStarted;
    public event EventHandler?         StudioPlaySoloStopped;

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

    // GameJoinLoadTime 行に含まれる universeid:XXXXXXXXXX
    private static readonly Regex UniverseIdPattern =
        new(@"\buniverseid:(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        "leaving game",
        "returnToLuaApp",
        "connection timeout",
        "Connection closed",
        // "Disconnecting" removed — too broad, fires on game JOIN logs too
    ];

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "logs");

    public RobloxLogWatcher(Func<bool> isRobloxRunningFunc)
    {
        _isRobloxRunningFunc = isRobloxRunningFunc ?? (() => false);
    }

    public void Start()
    {
        if (!Directory.Exists(LogDir)) return;

        var latest = GetLatestLogFile();
        if (latest != null)
        {
            // NexStrap経由でない外部起動のRobloxも検出するため IsRobloxRunning() をフォールバックとして使用
            if (_isRobloxRunningFunc() || IsRobloxRunning())
            {
                bool isStudio = Path.GetFileName(latest).Contains("_Studio_", StringComparison.OrdinalIgnoreCase);
                if (!isStudio)
                {
                    _watchedFilePid = FindOwnerPidForPlayerLog(latest);
                    CurrentEventSourcePid = _watchedFilePid;
                }
                var (placeId, userId, universeId, ip) = ScanForLastPlaceIdAndUser(latest);
                if (userId > 0) { _detectedUserId = userId; UserIdDetected?.Invoke(this, userId); }
                if (placeId > 0) { _lastPlaceId = placeId; _wasRunning = true; PlaceJoined?.Invoke(this, (placeId, universeId)); }
                if (!string.IsNullOrEmpty(ip)) { _detectedIp = ip; ServerIpDetected?.Invoke(this, ip); }
                CurrentEventSourcePid = 0;
            }
            StartWatchingFile(latest, fromEnd: true);
        }

        // ログ行ポーリング（250msごと）
        _pollTimer = new Timer(_ => PollLogFile(), null, 250, 250);

        // 新ログファイル検知（FileSystemWatcherの代わりに定期スキャン、500msごと）
        _scanTimer = new Timer(_ => CheckForNewLogFile(), null, 500, 500);

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

    public void SetBackgroundMode(bool background, bool playing)
    {
        _isBackgroundMode = background;
        _isPlayingMode = playing;

        if (background && playing)
        {
            _pollTimer?.Change(3_000, 3_000);
            if (_watchedFile == null)
                _scanTimer?.Change(30_000, 30_000);
            else
                _scanTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _processTimer?.Change(20_000, 20_000);
        }
        else if (background)
        {
            _pollTimer?.Change(5_000, 5_000);
            _scanTimer?.Change(15_000, 15_000);
            _processTimer?.Change(20_000, 20_000);
        }
        else
        {
            _pollTimer?.Change(250, 250);
            _scanTimer?.Change(500, 500);
            _processTimer?.Change(5_000, 5_000);
        }
    }

    // 現在監視中のファイルより新しいログファイルがあれば切り替える
    private void CheckForNewLogFile()
    {
        if (_isBackgroundMode && _isPlayingMode && _watchedFile != null)
            return;

        var latest = GetLatestLogFile();
        if (latest == null) return;
        bool switched;
        lock (_lock)
        {
            if (latest == _watchedFile) return;
            StartWatchingFileUnsafe(latest, fromEnd: false);
            switched = true;
        }
        if (switched)
        {
            bool isStudio = Path.GetFileName(latest).Contains("_Studio_", StringComparison.OrdinalIgnoreCase);
            _watchedFilePid = isStudio ? 0 : FindOwnerPidForPlayerLog(latest);
            PollLogFile();
        }
    }

    private void StartWatchingFile(string path, bool fromEnd)
    {
        lock (_lock) { StartWatchingFileUnsafe(path, fromEnd); }
    }

    public int CurrentSlotId { get; private set; }
    public event EventHandler<int>? InstanceSlotChanged;

    public bool IsWatchingStudioLog
    {
        get
        {
            string? file;
            lock (_lock) { file = _watchedFile; }
            return file != null &&
                   Path.GetFileName(file).Contains("_Studio_", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void StartWatchingFileUnsafe(string path, bool fromEnd)
    {
        if (_watchedFile != null && path != _watchedFile)
        {
            CurrentSlotId++;
            InstanceSlotChanged?.Invoke(this, CurrentSlotId);
        }
        _watchedFile    = path;
        _watchedFilePid = 0; // resolved lazily in PollLogFile or after this call
        _lastPlaceId    = 0;
        _detectedIp     = string.Empty;
        _detectedUserId = 0;
        _filePosition   = fromEnd ? GetFileLength(path) : 0;
    }

    private (long placeId, long userId, long universeId, string ip) ScanForLastPlaceIdAndUser(string path)
    {
        long foundPlace = 0, foundUser = 0, foundUniverse = 0;
        string foundIp = string.Empty;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                bool matchedPlace = false;
                foreach (var pattern in PlaceIdPatterns)
                {
                    var m = pattern.Match(line);
                    if (m.Success && long.TryParse(m.Groups[1].Value, out var id) && id > 0)
                    { foundPlace = id; matchedPlace = true; break; }
                }
                // universeid: は placeid: と同じ行（GameJoinLoadTime）にある
                if (matchedPlace)
                {
                    var um = UniverseIdPattern.Match(line);
                    foundUniverse = um.Success && long.TryParse(um.Groups[1].Value, out var uid) && uid > 0
                        ? uid : 0;
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
        return (foundPlace, foundUser, foundUniverse, foundIp);
    }

    private void PollLogFile()
    {
        string? file;
        long position;
        lock (_lock) { file = _watchedFile; position = _filePosition; }

        if (file == null || !File.Exists(file)) return;

        // PIDが未解決なら今解決する（新ファイルに切り替わった直後など）
        if (_watchedFilePid == 0)
        {
            bool isStudio = Path.GetFileName(file).Contains("_Studio_", StringComparison.OrdinalIgnoreCase);
            if (!isStudio) _watchedFilePid = FindOwnerPidForPlayerLog(file);
        }
        CurrentEventSourcePid = _watchedFilePid;

        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length <= position) { CurrentEventSourcePid = 0; return; }

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
        finally { CurrentEventSourcePid = 0; }
    }

    private void ProcessLine(string line)
    {
        long   fireUserId    = 0;
        long   firePlaceId   = 0;
        long   fireUniverseId = 0;
        bool   fireLeave     = false;
        string fireIp        = string.Empty;

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
                // universeid: は placeid: と同じ行（GameJoinLoadTime）にある
                var uum = UniverseIdPattern.Match(line);
                if (uum.Success && long.TryParse(uum.Groups[1].Value, out var universeId) && universeId > 0)
                    fireUniverseId = universeId;
                break;
            }

            var rm = UdmuxPattern.Match(line);
            if (rm.Success && rm.Groups[1].Value != _detectedIp)
            {
                _detectedIp = rm.Groups[1].Value;
                fireIp = _detectedIp;
            }

            if (firePlaceId > 0)
            {
                _wasRunning = true;
                _detectedIp = string.Empty; // 新しいゲームセッションでは必ず ServerIpDetected を発火させる
            }
            else if (_lastPlaceId != 0)
            {
                foreach (var keyword in LeaveKeywords)
                {
                    if (!line.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                    _lastPlaceId = 0;
                    _detectedIp  = string.Empty;
                    _wasRunning  = false;
                    fireLeave = true;
                    break;
                }
            }
        }

        // イベント発火はロック外で行う（デッドロック防止）
        if (fireUserId > 0)             UserIdDetected?.Invoke(this, fireUserId);
        if (firePlaceId > 0)            PlaceJoined?.Invoke(this, (firePlaceId, fireUniverseId));
        if (!string.IsNullOrEmpty(fireIp)) ServerIpDetected?.Invoke(this, fireIp);
        if (fireLeave)                  GameLeft?.Invoke(this, EventArgs.Empty);

        // Studio テストプレイ検知（State: PlaySolo / StopPlaySolo）
        if (IsWatchingStudioLog)
        {
            if (line.Contains("[telemetryLog] State: PlaySolo", StringComparison.Ordinal) &&
                !line.Contains("Stop", StringComparison.OrdinalIgnoreCase))
                StudioPlaySoloStarted?.Invoke(this, EventArgs.Empty);
            else if (line.Contains("State: StopPlaySolo", StringComparison.OrdinalIgnoreCase))
                StudioPlaySoloStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CheckProcessExit()
    {
        // Studio testplay runs inside Studio — no RobloxPlayerBeta process exists.
        // Leave detection is handled by log keywords (StopPlaySolo/Connection closed).
        if (IsWatchingStudioLog) return;

        var running = _isRobloxRunningFunc() || IsRobloxRunning();
        bool shouldFireLeave;
        lock (_lock)
        {
            if (running)
            {
                // ゲームプレイ中のときだけフラグを立てる。
                // メニュー画面で _lastPlaceId==0 のときは立てない → 二重発火防止
                if (_lastPlaceId != 0) _wasRunning = true;
                return;
            }
            shouldFireLeave = _wasRunning;
            if (_wasRunning) { _wasRunning = false; _lastPlaceId = 0; }
        }
        if (shouldFireLeave)
        {
            CurrentEventSourcePid = _watchedFilePid;
            GameLeft?.Invoke(this, EventArgs.Empty);
            CurrentEventSourcePid = 0;
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

    // ログファイルの作成時刻とプロセス起動時刻を照合してオーナーPIDを特定する（120秒以内）
    private static uint FindOwnerPidForPlayerLog(string logFilePath)
    {
        try
        {
            var created = File.GetCreationTime(logFilePath);
            Process? best = null;
            double bestDelta = 120.0;
            foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta")
                                     .Concat(Process.GetProcessesByName("RobloxPlayer"))
                                     .Where(p => !p.HasExited))
            {
                try
                {
                    var delta = Math.Abs((p.StartTime - created).TotalSeconds);
                    if (delta < bestDelta) { bestDelta = delta; best = p; }
                }
                catch { }
            }
            return best != null ? (uint)best.Id : 0;
        }
        catch { return 0; }
    }

    // プロセスの起動時刻に最も近いPlayerログファイルを見つける（120秒以内）
    private static string? FindLogFileForProcess(Process proc)
    {
        if (!Directory.Exists(LogDir)) return null;
        try
        {
            var startTime = proc.StartTime;
            string? best = null;
            double bestDelta = 120.0;
            foreach (var f in Directory.GetFiles(LogDir, "*_Player_*_last.log"))
            {
                if (f.Contains("_Studio_", StringComparison.OrdinalIgnoreCase)) continue;
                var delta = Math.Abs((File.GetCreationTime(f) - startTime).TotalSeconds);
                if (delta < bestDelta) { bestDelta = delta; best = f; }
            }
            return best;
        }
        catch { return null; }
    }

    // 現在監視中のファイル以外の全Playerインスタンスをスキャンし、起動時イベントを発火する
    public void ScanAllPlayerInstances()
    {
        string? watchedFile;
        lock (_lock) { watchedFile = _watchedFile; }

        var procs = Process.GetProcessesByName("RobloxPlayerBeta")
                           .Concat(Process.GetProcessesByName("RobloxPlayer"))
                           .Where(p => !p.HasExited)
                           .ToList();

        foreach (var proc in procs)
        {
            var logFile = FindLogFileForProcess(proc);
            if (logFile == null || logFile == watchedFile) continue;

            CurrentEventSourcePid = (uint)proc.Id;
            try
            {
                var (placeId, userId, universeId, ip) = ScanForLastPlaceIdAndUser(logFile);
                if (userId > 0)                    UserIdDetected?.Invoke(this, userId);
                if (placeId > 0)                   PlaceJoined?.Invoke(this, (placeId, universeId));
                if (!string.IsNullOrEmpty(ip))     ServerIpDetected?.Invoke(this, ip);
            }
            catch { }
            finally { CurrentEventSourcePid = 0; }
        }
    }

    public void Dispose() => Stop();
}
