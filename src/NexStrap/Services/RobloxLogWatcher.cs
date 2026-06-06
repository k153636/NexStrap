using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NexStrap.Services;

// ── イベント引数 ──────────────────────────────────────────────────────────────
public sealed record PlaceJoinedArgs(uint Pid, int Slot, long PlaceId, long UniverseId);
public sealed record GameLeftArgs(uint Pid, int Slot);
public sealed record UserIdDetectedArgs(uint Pid, int Slot, long UserId);
public sealed record ServerIpDetectedArgs(uint Pid, int Slot, string Ip);
public sealed record InstanceActivity(
    uint Pid,
    int Slot,
    long PlaceId,
    long UniverseId,
    long UserId,
    string? ServerIp,
    DateTime TimeJoined,
    string LogPath);
public sealed record ActivityChangedArgs(InstanceActivity Activity);

public class RobloxLogWatcher : IDisposable
{
    // ── per-instance state ─────────────────────────────────────────────────
    private sealed class InstanceState
    {
        public string LogPath        { get; init; } = "";
        public uint   Pid            { get; init; }
        public int    Slot           { get; init; }
        public long   Position       { get; set; }
        public long   LastPlaceId    { get; set; }
        public string DetectedIp     { get; set; } = "";
        public long   DetectedUserId { get; set; }
        public bool   WasRunning     { get; set; }
        public InstanceActivity? Activity { get; set; }
    }

    private Timer? _pollTimer;
    private Timer? _scanTimer;
    private Timer? _processTimer;
    private bool   _isBackgroundMode;
    private bool   _isPlayingMode;
    private readonly object _lock = new();
    private readonly Func<bool> _isRobloxRunningFunc;
    private int _nextSlot;

    // PID → インスタンス状態
    private readonly Dictionary<uint, InstanceState> _instances = new();

    public event EventHandler<PlaceJoinedArgs>?     PlaceJoined;
    public event EventHandler<GameLeftArgs>?         GameLeft;
    public event EventHandler<UserIdDetectedArgs>?   UserIdDetected;
    public event EventHandler<ServerIpDetectedArgs>? ServerIpDetected;
    public event EventHandler<ActivityChangedArgs>?  ActivityChanged;
    public event EventHandler?                       StudioPlaySoloStarted;
    public event EventHandler?                       StudioPlaySoloStopped;

    // Studio は DiscordRichPresence._studioTimer で追跡するため常に false
    public bool IsWatchingStudioLog => false;

    // ── 正規表現 ──────────────────────────────────────────────────────────

    private static readonly Regex[] PlaceIdPatterns =
    [
        new(@"placeid:(\d+)",             RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"placeId[=:](\d+)",          RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bplace\s+(\d{7,})\b",      RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"""placeId""\s*:\s*(\d+)",   RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly Regex UserIdPattern =
        new(@"\buserid:(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UniverseIdPattern =
        new(@"\buniverseid\s*[:=]\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UdmuxPattern =
        new(@"UDMUX Address = (\d+\.\d+\.\d+\.\d+)", RegexOptions.Compiled);

    private static readonly string[] LeaveKeywords =
    [
        "Disconnect was called",
        "GameDisconnect",
        "game left",
        "reportGameDisconnect",
        "Game disconnect",
        "leaving game",
        "leaveUGCGameInternal",
        "returnToLuaApp",
        "Client:Disconnect",
        "Sending disconnect",
        "Disconnected from server",
        "Disconnection Notification",
        "Connection lost",
        "connection timeout",
        "Connection closed",
    ];

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "logs");
    private const double MaxProcessLogStartDeltaSeconds = 120;

    // ── コンストラクタ ────────────────────────────────────────────────────

    public RobloxLogWatcher(Func<bool> isRobloxRunningFunc)
    {
        _isRobloxRunningFunc = isRobloxRunningFunc ?? (() => false);
    }

    // PID→slot の安定ルックアップ（フォーカスタイマー用）
    public bool TryGetSlotForPid(uint pid, out int slot)
    {
        lock (_lock)
        {
            if (_instances.TryGetValue(pid, out var state)) { slot = state.Slot; return true; }
        }
        slot = -1;
        return false;
    }

    public bool TryGetActivityForSlot(int slot, out InstanceActivity activity)
    {
        lock (_lock)
        {
            var found = _instances.Values.FirstOrDefault(s => s.Slot == slot)?.Activity;
            if (found != null) { activity = found; return true; }
        }
        activity = null!;
        return false;
    }

    // ── 起動 ──────────────────────────────────────────────────────────────

    public void Start()
    {
        if (!Directory.Exists(LogDir))
        {
            Logger.Instance.Warning("LogWatcher", $"Roblox log directory not found: {LogDir}");
            return;
        }

        var procs   = GetAllPlayerProcesses().ToList();
        var matched = MatchProcessesToLogFiles(procs);
        Logger.Instance.Info("LogWatcher", $"Start: processes={procs.Count}, matched={matched.Count}");
        _nextSlot = matched.Count;
        foreach (var (pid, (logPath, slot)) in matched)
        {
            var state = new InstanceState { LogPath = logPath, Pid = pid, Slot = slot };
            lock (_lock) { _instances[pid] = state; }
            Logger.Instance.Info("LogWatcher", $"Attach initial: pid={pid}, slot={slot}, log={Path.GetFileName(logPath)}");
            ScanInstanceForInitialState(state);
        }

        _pollTimer    = new Timer(_ => PollAllInstances(),    null, 250,   250);
        _scanTimer    = new Timer(_ => CheckForNewInstances(), null, 500,   500);
        _processTimer = new Timer(_ => CheckAllProcessExits(), null, 5000, 5000);
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        _scanTimer?.Dispose();
        _processTimer?.Dispose();
        _pollTimer = _scanTimer = _processTimer = null;
    }

    public void SetBackgroundMode(bool background, bool playing)
    {
        _isBackgroundMode = background;
        _isPlayingMode    = playing;

        if (background && playing)
        {
            _pollTimer?.Change(3_000, 3_000);
            _scanTimer?.Change(3_000, 3_000);
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

    // ── 全インスタンスポーリング ─────────────────────────────────────────

    private void PollAllInstances()
    {
        List<InstanceState> snapshot;
        lock (_lock) { snapshot = _instances.Values.ToList(); }

        foreach (var state in snapshot)
        {
            try   { PollInstance(state); }
            catch { }
        }
    }

    private void PollInstance(InstanceState state)
    {
        if (!File.Exists(state.LogPath))
        {
            Logger.Instance.Warning("LogWatcher", $"Missing log: pid={state.Pid}, slot={state.Slot}, log={Path.GetFileName(state.LogPath)}");
            return;
        }
        long pos;
        lock (_lock) { pos = state.Position; }

        byte[] buf;
        int bytesRead;
        using (var fs = new FileStream(state.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            if (fs.Length <= pos) return;
            fs.Seek(pos, SeekOrigin.Begin);
            var available = (int)(fs.Length - pos);
            buf = new byte[available];
            bytesRead = fs.Read(buf, 0, available);
        }
        if (bytesRead == 0) return;

        // StreamReader の先読みバッファによる partial line 消失を防ぐため
        // 生バイトを直接処理し、\n で終わる完全な行のみ処理する
        var content = System.Text.Encoding.UTF8.GetString(buf, 0, bytesRead);
        int lineStart = 0;
        long newPos = pos;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] != '\n') continue;
            var line = content.Substring(lineStart, i - lineStart).TrimEnd('\r');
            ProcessLineForInstance(line, state);
            newPos = pos + System.Text.Encoding.UTF8.GetByteCount(content, 0, i + 1);
            lineStart = i + 1;
        }
        if (newPos > pos)
            lock (_lock) { state.Position = newPos; }
    }

    private void ProcessLineForInstance(string line, InstanceState state)
    {
        long   fireUserId     = 0;
        long   firePlaceId    = 0;
        long   fireUniverseId = 0;
        bool   fireLeave      = false;
        string fireIp         = string.Empty;
        InstanceActivity? activityChanged = null;

        lock (_lock)
        {
            if (state.DetectedUserId == 0)
            {
                var um = UserIdPattern.Match(line);
                if (um.Success && long.TryParse(um.Groups[1].Value, out var uid) && uid > 0)
                {
                    state.DetectedUserId = uid;
                    fireUserId = uid;
                    if (state.Activity != null)
                    {
                        state.Activity = state.Activity with { UserId = uid };
                        activityChanged = state.Activity;
                    }
                }
            }

            foreach (var pattern in PlaceIdPatterns)
            {
                var m = pattern.Match(line);
                if (!m.Success) continue;
                if (!long.TryParse(m.Groups[1].Value, out var placeId)) continue;
                if (placeId <= 0) continue;
                if (placeId == state.LastPlaceId)
                {
                    var duplicateUniverseId = MatchUniverseId(line);
                    if (duplicateUniverseId > 0 && state.Activity is { UniverseId: 0 } activity)
                    {
                        state.Activity = activity with { UniverseId = duplicateUniverseId };
                        activityChanged = state.Activity;
                    }
                    continue;
                }
                state.LastPlaceId = placeId;
                firePlaceId = placeId;
                fireUniverseId = MatchUniverseId(line);
                break;
            }

            var rm = UdmuxPattern.Match(line);
            if (rm.Success && rm.Groups[1].Value != state.DetectedIp)
            {
                state.DetectedIp = rm.Groups[1].Value;
                fireIp = state.DetectedIp;
            }

            if (firePlaceId > 0)
            {
                state.WasRunning = true;
                state.DetectedIp = string.Empty;
                state.Activity = new InstanceActivity(
                    state.Pid,
                    state.Slot,
                    firePlaceId,
                    fireUniverseId,
                    state.DetectedUserId,
                    null,
                    DateTime.UtcNow,
                    state.LogPath);
                activityChanged = state.Activity;
            }
            else if (state.LastPlaceId != 0)
            {
                foreach (var kw in LeaveKeywords)
                {
                    if (!line.Contains(kw, StringComparison.OrdinalIgnoreCase)) continue;
                    state.LastPlaceId = 0;
                    state.DetectedIp  = string.Empty;
                    state.WasRunning  = false;
                    state.Activity    = null;
                    fireLeave = true;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(fireIp) && state.Activity != null)
            {
                state.Activity = state.Activity with { ServerIp = fireIp };
                activityChanged = state.Activity;
            }
        }

        if (fireUserId > 0)
            UserIdDetected?.Invoke(this, new UserIdDetectedArgs(state.Pid, state.Slot, fireUserId));
        if (activityChanged != null)
            ActivityChanged?.Invoke(this, new ActivityChangedArgs(activityChanged));
        if (firePlaceId > 0)
        {
            Logger.Instance.Info("LogWatcher", $"Place detected: pid={state.Pid}, slot={state.Slot}, placeId={firePlaceId}, universeId={fireUniverseId}, log={Path.GetFileName(state.LogPath)}");
            PlaceJoined?.Invoke(this, new PlaceJoinedArgs(state.Pid, state.Slot, firePlaceId, fireUniverseId));
        }
        if (!string.IsNullOrEmpty(fireIp))
        {
            Logger.Instance.Info("LogWatcher", $"Server IP detected: pid={state.Pid}, slot={state.Slot}, ip={fireIp}");
            ServerIpDetected?.Invoke(this, new ServerIpDetectedArgs(state.Pid, state.Slot, fireIp));
        }
        if (fireLeave)
        {
            Logger.Instance.Info("LogWatcher", $"Leave detected: pid={state.Pid}, slot={state.Slot}, log={Path.GetFileName(state.LogPath)}");
            GameLeft?.Invoke(this, new GameLeftArgs(state.Pid, state.Slot));
        }
    }

    // ── 新インスタンス検出（スキャンタイマー） ────────────────────────────

    private void CheckForNewInstances()
    {
        var procs = GetAllPlayerProcesses().ToList();

        // 既に監視中のログファイルを取得（重複マッチ防止）
        HashSet<string> usedLogs;
        lock (_lock)
        {
            usedLogs = new HashSet<string>(
                _instances.Values.Select(s => s.LogPath), StringComparer.OrdinalIgnoreCase);
        }

        foreach (var proc in procs)
        {
            var pid = (uint)proc.Id;
            bool exists;
            lock (_lock) { exists = _instances.ContainsKey(pid); }
            if (exists) continue;

            var logFile = FindLogFileForProcess(proc, usedLogs);
            if (logFile == null)
            {
                Logger.Instance.Warning("LogWatcher", $"No log match yet: pid={pid}, start={SafeStartTime(proc):HH:mm:ss.fff}");
                continue;
            }

            var slot  = Interlocked.Increment(ref _nextSlot) - 1;
            var state = new InstanceState { LogPath = logFile, Pid = pid, Slot = slot };
            lock (_lock)
            {
                if (_instances.ContainsKey(pid)) continue;
                _instances[pid] = state;
                usedLogs.Add(logFile);
            }
            Logger.Instance.Info("LogWatcher", $"Attach new: pid={pid}, slot={slot}, log={Path.GetFileName(logFile)}, delta={GetCreationDeltaSeconds(proc, logFile):F1}s");
            ScanInstanceForInitialState(state);
        }
    }

    // ── プロセス終了検出（プロセスタイマー） ─────────────────────────────

    private void CheckAllProcessExits()
    {
        var dead = new List<InstanceState>();
        lock (_lock)
        {
            foreach (var (pid, state) in _instances)
            {
                bool alive;
                try { var p = Process.GetProcessById((int)pid); alive = !p.HasExited; }
                catch { alive = false; }
                if (!alive) dead.Add(state);
            }
        }

        // イベント発火を先に行う（発火中も TryGetSlotForPid が正しく機能するように）
        foreach (var state in dead)
        {
            if (state.WasRunning)
                GameLeft?.Invoke(this, new GameLeftArgs(state.Pid, state.Slot));
        }

        lock (_lock)
        {
            foreach (var state in dead) _instances.Remove(state.Pid);
        }
    }

    // ── 起動時初期スキャン ────────────────────────────────────────────────

    private void ScanInstanceForInitialState(InstanceState state)
    {
        try
        {
            var (placeId, userId, universeId, ip) = ScanForLastPlaceIdAndUser(state.LogPath);
            InstanceActivity? activityChanged = null;
            if (userId > 0)
            {
                lock (_lock) { state.DetectedUserId = userId; }
                UserIdDetected?.Invoke(this, new UserIdDetectedArgs(state.Pid, state.Slot, userId));
            }
            if (placeId > 0)
            {
                lock (_lock)
                {
                    state.LastPlaceId = placeId;
                    state.WasRunning = true;
                    state.Activity = new InstanceActivity(
                        state.Pid,
                        state.Slot,
                        placeId,
                        universeId,
                        state.DetectedUserId,
                        string.IsNullOrEmpty(ip) ? null : ip,
                        DateTime.UtcNow,
                        state.LogPath);
                    activityChanged = state.Activity;
                }
                Logger.Instance.Info("LogWatcher", $"Initial place detected: pid={state.Pid}, slot={state.Slot}, placeId={placeId}, universeId={universeId}, log={Path.GetFileName(state.LogPath)}");
                if (activityChanged != null)
                    ActivityChanged?.Invoke(this, new ActivityChangedArgs(activityChanged));
                PlaceJoined?.Invoke(this, new PlaceJoinedArgs(state.Pid, state.Slot, placeId, universeId));
            }
            if (!string.IsNullOrEmpty(ip))
            {
                lock (_lock) { state.DetectedIp = ip; }
                Logger.Instance.Info("LogWatcher", $"Initial server IP detected: pid={state.Pid}, slot={state.Slot}, ip={ip}");
                ServerIpDetected?.Invoke(this, new ServerIpDetectedArgs(state.Pid, state.Slot, ip));
            }
            lock (_lock) { state.Position = GetFileLength(state.LogPath); }
            Logger.Instance.Info("LogWatcher", $"Initial scan complete: pid={state.Pid}, slot={state.Slot}, placeId={placeId}, ip={(string.IsNullOrEmpty(ip) ? "none" : ip)}, position={state.Position}");
        }
        catch (Exception ex) { Logger.Instance.Exception("LogWatcher", ex); }
    }

    // ── ログファイル全体スキャン（最後のplaceId/userId/ip取得） ───────────

    private static (long placeId, long userId, long universeId, string ip) ScanForLastPlaceIdAndUser(string path)
    {
        long foundPlace = 0, foundUser = 0, foundUniverse = 0;
        string foundIp = string.Empty;
        try
        {
            using var fs     = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
                if (matchedPlace)
                {
                    foundUniverse = MatchUniverseId(line);
                    foundIp = string.Empty;
                }
                else if (foundPlace != 0)
                {
                    foreach (var kw in LeaveKeywords)
                    {
                        if (!line.Contains(kw, StringComparison.OrdinalIgnoreCase)) continue;
                        foundPlace = 0;
                        foundUniverse = 0;
                        foundIp = string.Empty;
                        break;
                    }
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

    private static long MatchUniverseId(string line)
    {
        var um = UniverseIdPattern.Match(line);
        return um.Success && long.TryParse(um.Groups[1].Value, out var uid) && uid > 0 ? uid : 0;
    }

    // ── プロセス→ログファイル マッチング ─────────────────────────────────

    // 起動時: 複数プロセスをまとめてマッチング（貪欲マッチ、時間制限なし）
    private static Dictionary<uint, (string LogPath, int Slot)> MatchProcessesToLogFiles(
        IEnumerable<Process> procs)
    {
        var result   = new Dictionary<uint, (string, int)>();
        var procList = procs.ToList();
        if (procList.Count == 0 || !Directory.Exists(LogDir)) return result;

        var logFiles = Directory.GetFiles(LogDir, "*_Player_*_last.log")
                                .Where(f => !f.Contains("_Studio_", StringComparison.OrdinalIgnoreCase))
                                .ToList();
        if (logFiles.Count == 0) return result;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int slot = 0;
        foreach (var proc in procList.OrderBy(p => { try { return p.StartTime; } catch { return DateTime.MaxValue; } }))
        {
            try
            {
                var startTime = proc.StartTime;
                var candidates = logFiles
                    .Where(f => !used.Contains(f))
                    .Select(f => (Path: f, Delta: Math.Abs((File.GetCreationTime(f) - startTime).TotalSeconds)))
                    .Where(x => x.Delta <= MaxProcessLogStartDeltaSeconds)
                    .ToList();
                if (candidates.Count > 0)
                {
                    var best = candidates.MinBy(x => x.Delta);
                    result[(uint)proc.Id] = (best.Path, slot++);
                    used.Add(best.Path);
                }
            }
            catch { }
        }
        return result;
    }

    // リアルタイム: 単一プロセスのログファイルを見つける（使用中ファイルを除外）
    private static string? FindLogFileForProcess(Process proc, HashSet<string> usedLogs)
    {
        if (!Directory.Exists(LogDir)) return null;
        try
        {
            var startTime = proc.StartTime;
            string? best  = null;
            double bestDelta = double.MaxValue;
            foreach (var f in Directory.GetFiles(LogDir, "*_Player_*_last.log"))
            {
                if (f.Contains("_Studio_", StringComparison.OrdinalIgnoreCase)) continue;
                if (usedLogs.Contains(f)) continue;
                var delta = Math.Abs((File.GetCreationTime(f) - startTime).TotalSeconds);
                if (delta < bestDelta) { bestDelta = delta; best = f; }
            }
            return bestDelta <= MaxProcessLogStartDeltaSeconds ? best : null;
        }
        catch { return null; }
    }

    private static DateTime SafeStartTime(Process proc)
    {
        try { return proc.StartTime; }
        catch { return DateTime.MinValue; }
    }

    private static double GetCreationDeltaSeconds(Process proc, string logFile)
    {
        try { return Math.Abs((File.GetCreationTime(logFile) - proc.StartTime).TotalSeconds); }
        catch { return double.NaN; }
    }

    // ── ユーティリティ ────────────────────────────────────────────────────

    private static IEnumerable<Process> GetAllPlayerProcesses() =>
        Process.GetProcessesByName("RobloxPlayerBeta")
               .Concat(Process.GetProcessesByName("RobloxPlayer"))
               .Where(p => { try { return !p.HasExited; } catch { return false; } });

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

    public void Dispose() => Stop();
}
