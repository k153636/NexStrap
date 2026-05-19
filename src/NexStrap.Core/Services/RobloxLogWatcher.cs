using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NexStrap.Core.Services;

public class RobloxLogWatcher : IDisposable
{
    private FileSystemWatcher? _dirWatcher;
    private Timer? _pollTimer;
    private Timer? _processTimer;
    private string? _watchedFile;
    private long _filePosition;
    private long _lastPlaceId;

    public event EventHandler<long>? PlaceJoined;
    public event EventHandler? GameLeft;

    private static readonly Regex[] PlaceIdPatterns =
    [
        new(@"placeId[=:](\d+)",          RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"place_id[=:](\d+)",         RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"place id[=: ]+(\d+)",       RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"PlaceId\s*=\s*(\d+)",       RegexOptions.Compiled),
        new(@"placeid:(\d+)",             RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"""placeId""\s*:\s*(\d+)",   RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly string[] LeaveKeywords =
    [
        "Disconnect was called",
        "GameDisconnect",
        "game left",
        "reportGameDisconnect",
        "Game disconnect",
        "Disconnecting",
        "leaving game"
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
                // 既に起動中 → ログ全体から最新placeIdを取得してRPCを即反映
                var placeId = ScanForLastPlaceId(latest);
                if (placeId > 0)
                {
                    _lastPlaceId = placeId;
                    PlaceJoined?.Invoke(this, placeId);
                }
            }

            // 今後の新規ログ行は末尾から監視（過去分はスキップ）
            StartWatchingFile(latest, fromEnd: true);
        }

        // Roblox起動時に作成される新ログファイルを検知
        _dirWatcher = new FileSystemWatcher(LogDir)
        {
            Filter = "*",
            EnableRaisingEvents = true
        };
        _dirWatcher.Created += (_, e) =>
        {
            if (e.FullPath.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                StartWatchingFile(e.FullPath, fromEnd: false);
        };

        _pollTimer  = new Timer(_ => PollLogFile(),      null, 1000, 1000);
        _processTimer = new Timer(_ => CheckProcessExit(), null, 5000, 5000);
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        _dirWatcher?.Dispose();
        _processTimer?.Dispose();
        _pollTimer    = null;
        _dirWatcher   = null;
        _processTimer = null;
    }

    private void StartWatchingFile(string path, bool fromEnd = false)
    {
        _watchedFile  = path;
        _lastPlaceId  = 0;
        _filePosition = fromEnd ? GetFileLength(path) : 0;
    }

    // ログ全体を読み、最後に現れたplaceIdを返す
    private static long ScanForLastPlaceId(string path)
    {
        long found = 0;
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
                    {
                        found = id;
                        break;
                    }
                }
            }
        }
        catch { }
        return found;
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
        foreach (var pattern in PlaceIdPatterns)
        {
            var match = pattern.Match(line);
            if (!match.Success) continue;
            if (!long.TryParse(match.Groups[1].Value, out var placeId)) continue;
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

    // Robloxプロセス終了を検知（ログが退出を記録しなかった場合のフォールバック）
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
        try { return Process.GetProcessesByName("RobloxPlayerBeta").Any(p => !p.HasExited); }
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
