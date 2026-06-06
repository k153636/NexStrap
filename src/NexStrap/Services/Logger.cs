using System.Collections.Concurrent;
using System.Text;

namespace NexStrap.Services;

/// <summary>
/// ファイルベースのロガー。非ブロッキングキューでバックグラウンド書き込みを行う。
/// %LOCALAPPDATA%\NexStrap\Logs\ に日付別ファイルで保存する。
/// </summary>
public sealed class Logger : IDisposable
{
    public enum Level { Info, Warning, Error }

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "Logs");

    private readonly BlockingCollection<string> _queue = new(4096);
    private readonly Thread                     _worker;
    private readonly string                     _filePath;
    private          StreamWriter?              _writer;
    private          bool                       _disposed;

    public static Logger Instance { get; } = new();

    private Logger()
    {
        var date  = DateTime.Now.ToString("yyyy-MM-dd");
        _filePath = Path.Combine(LogDir, $"nexstrap-{date}.log");

        _worker = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name         = "LoggerWorker"
        };
        _worker.Start();

        CleanOldLogs();
    }

    // ── 公開 API ─────────────────────────────────────────────────────────

    public void Info   (string category, string message) => Enqueue(Level.Info,    category, message);
    public void Warning(string category, string message) => Enqueue(Level.Warning, category, message);
    public void Error  (string category, string message) => Enqueue(Level.Error,   category, message);

    public void Exception(string category, Exception ex)
        => Enqueue(Level.Error, category, $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    // ── 内部 ──────────────────────────────────────────────────────────────

    private void Enqueue(Level level, string category, string message)
    {
        if (_disposed) return;
        var line = Format(level, category, message);
        try { _queue.TryAdd(line, millisecondsTimeout: 0); } catch { }
    }

    private static string Format(Level level, string category, string message)
    {
        var ts  = DateTime.Now.ToString("HH:mm:ss.fff");
        var lv  = level switch
        {
            Level.Info    => "INF",
            Level.Warning => "WRN",
            Level.Error   => "ERR",
            _             => "   "
        };
        return $"[{ts}] [{lv}] [{category}] {message}";
    }

    private void WriteLoop()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            _writer = new StreamWriter(_filePath, append: true, Encoding.UTF8) { AutoFlush = false };

            foreach (var line in _queue.GetConsumingEnumerable())
            {
                _writer.WriteLine(line);
                if (_queue.Count == 0) _writer.Flush();
            }
        }
        catch { }
        finally
        {
            try { _writer?.Flush(); _writer?.Dispose(); } catch { }
        }
    }

    private static void CleanOldLogs(int keepDays = 7)
    {
        try
        {
            if (!Directory.Exists(LogDir)) return;
            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var file in Directory.GetFiles(LogDir, "nexstrap-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        _worker.Join(millisecondsTimeout: 2000);
        _queue.Dispose();
    }
}
