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

    // Roblox ログに現れる placeId のパターン（複数形式に対応）
    private static readonly Regex[] PlaceIdPatterns =
    [
        new(@"placeId[=:](\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"place_id[=:](\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"place id[=: ]+(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"PlaceId\s*=\s*(\d+)", RegexOptions.Compiled),
        new(@"placeid:(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly string[] LeaveKeywords =
    [
        "Disconnect was called",
        "GameDisconnect",
        "game left",
        "reportGameDisconnect",
        "Game disconnect",
        "Disconnecting"
    ];

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "logs");

    public void Start()
    {
        if (!Directory.Exists(LogDir)) return;

        // 既存の最新ログを監視開始
        var latest = GetLatestLogFile();
        if (latest != null) StartWatchingFile(latest);

        // 新しいログファイル（Roblox起動時に作成）を検知
        _dirWatcher = new FileSystemWatcher(LogDir)
        {
            Filter = "*",
            EnableRaisingEvents = true
        };
        _dirWatcher.Created += (_, e) =>
        {
            if (e.FullPath.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                StartWatchingFile(e.FullPath);
        };

        // ログファイルをポーリング（1秒ごと）
        _pollTimer = new Timer(_ => PollLogFile(), null, 1000, 1000);

        // Robloxプロセス監視（退出フォールバック用、5秒ごと）
        _processTimer = new Timer(_ => CheckProcessExit(), null, 5000, 5000);
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        _dirWatcher?.Dispose();
        _processTimer?.Dispose();
        _pollTimer = null;
        _dirWatcher = null;
        _processTimer = null;
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
        // ゲーム参加検知
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

        // ゲーム退出検知
        if (_lastPlaceId != 0)
        {
            foreach (var keyword in LeaveKeywords)
            {
                if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    _lastPlaceId = 0;
                    GameLeft?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }
        }
    }

    // Robloxプロセスが終了していたら退出イベントを発火（フォールバック）
    private void CheckProcessExit()
    {
        if (_lastPlaceId == 0) return;

        try
        {
            var running = Process.GetProcessesByName("RobloxPlayerBeta")
                .Any(p => !p.HasExited);
            if (!running)
            {
                _lastPlaceId = 0;
                GameLeft?.Invoke(this, EventArgs.Empty);
            }
        }
        catch { }
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
