using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NexStrap.Services;

// ── イベント引数 ──────────────────────────────────────────────────────────────
public sealed record PlaceJoinedArgs(uint Pid, int Slot, long PlaceId, long UniverseId);
public sealed record GameLeftArgs(uint Pid, int Slot);
public sealed record UserIdDetectedArgs(uint Pid, int Slot, long UserId);
public sealed record ServerIpDetectedArgs(uint Pid, int Slot, string Ip);

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
        new(@"\buniverseid:(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
        "returnToLuaApp",
        "connection timeout",
        "Connection closed",
    ];

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "logs");

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

    // ── 起動 ──────────────────────────────────────────────────────────────

    public void Start()
    {
        if (!Directory.Exists(LogDir)) return;

        var procs   = GetAllPlayerProcesses().ToList();
        var matched = MatchProcessesToLogFiles(procs);
        _nextSlot = matched.Count;
        foreach (var (pid, (logPath, slot)) in matched)
        {
            var state = new InstanceState { LogPath = logPath, Pid = pid, Slot = slot };
            lock (_lock) { _instances[pid] = state; }
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
        if (!File.Exists(state.LogPath)) return;
        using var fs = new FileStream(state.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long pos;
        lock (_lock) { pos = state.Position; }
        if (fs.Length <= pos) return;
        fs.Seek(pos, SeekOrigin.Begin);
        using var reader = new StreamReader(fs);
        string? line;
        while ((line = reader.ReadLine()) != null)
            ProcessLineForInstance(line, state);
        lock (_lock) { state.Position = fs.Position; }
    }

    private void ProcessLineForInstance(string line, InstanceState state)
    {
        long   fireUserId     = 0;
        long   firePlaceId    = 0;
        long   fireUniverseId = 0;
        bool   fireLeave      = false;
        string fireIp         = string.Empty;

        lock (_lock)
        {
            if (state.DetectedUserId == 0)
            {
                var um = UserIdPattern.Match(line);
                if (um.Success && long.TryParse(um.Groups[1].Value, out var uid) && uid > 0)
                {
                    state.DetectedUserId = uid;
                    fireUserId = uid;
                }
            }

            foreach (var pattern in PlaceIdPatterns)
            {
                var m = pattern.Match(line);
                if (!m.Success) continue;
                if (!long.TryParse(m.Groups[1].Value, out var placeId)) continue;
                if (placeId <= 0 || placeId == state.LastPlaceId) continue;
                state.LastPlaceId = placeId;
                firePlaceId = placeId;
                var uum = UniverseIdPattern.Match(line);
                if (uum.Success && long.TryParse(uum.Groups[1].Value, out var uid2) && uid2 > 0)
                    fireUniverseId = uid2;
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
            }
            else if (state.LastPlaceId != 0)
            {
                foreach (var kw in LeaveKeywords)
                {
                    if (!line.Contains(kw, StringComparison.OrdinalIgnoreCase)) continue;
                    state.LastPlaceId = 0;
                    state.DetectedIp  = string.Empty;
                    state.WasRunning  = false;
                    fireLeave = true;
                    break;
                }
            }
        }

        if (fireUserId > 0)
            UserIdDetected?.Invoke(this, new UserIdDetectedArgs(state.Pid, state.Slot, fireUserId));
        if (firePlaceId > 0)
            PlaceJoined?.Invoke(this, new PlaceJoinedArgs(state.Pid, state.Slot, firePlaceId, fireUniverseId));
        if (!string.IsNullOrEmpty(fireIp))
            ServerIpDetected?.Invoke(this, new ServerIpDetectedArgs(state.Pid, state.Slot, fireIp));
        if (fireLeave)
            GameLeft?.Invoke(this, new GameLeftArgs(state.Pid, state.Slot));
    }

    // ── 新インスタンス検出（スキャンタイマー） ────────────────────────────

    private void CheckForNewInstances()
    {
        if (_isBackgroundMode && _isPlayingMode) return;

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
            if (logFile == null) continue;

            var slot  = Interlocked.Increment(ref _nextSlot) - 1;
            var state = new InstanceState { LogPath = logFile, Pid = pid, Slot = slot };
            lock (_lock)
            {
                if (_instances.ContainsKey(pid)) continue;
                _instances[pid] = state;
                usedLogs.Add(logFile);
            }
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
            if (userId > 0)
            {
                lock (_lock) { state.DetectedUserId = userId; }
                UserIdDetected?.Invoke(this, new UserIdDetectedArgs(state.Pid, state.Slot, userId));
            }
            if (placeId > 0)
            {
                lock (_lock) { state.LastPlaceId = placeId; state.WasRunning = true; }
                PlaceJoined?.Invoke(this, new PlaceJoinedArgs(state.Pid, state.Slot, placeId, universeId));
            }
            if (!string.IsNullOrEmpty(ip))
            {
                lock (_lock) { state.DetectedIp = ip; }
                ServerIpDetected?.Invoke(this, new ServerIpDetectedArgs(state.Pid, state.Slot, ip));
            }
            lock (_lock) { state.Position = GetFileLength(state.LogPath); }
        }
        catch { }
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
                    var um = UniverseIdPattern.Match(line);
                    foundUniverse = um.Success && long.TryParse(um.Groups[1].Value, out var uid) && uid > 0 ? uid : 0;
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
                var best = logFiles
                    .Where(f => !used.Contains(f))
                    .MinBy(f => Math.Abs((File.GetCreationTime(f) - startTime).TotalSeconds));
                if (best != null)
                {
                    result[(uint)proc.Id] = (best, slot++);
                    used.Add(best);
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
            return best;
        }
        catch { return null; }
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
