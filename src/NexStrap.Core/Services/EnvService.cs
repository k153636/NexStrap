namespace NexStrap.Core.Services;

public class EnvService
{
    private readonly Dictionary<string, string> _vars = new();

    public EnvService()
    {
        // Single-file publish では AppContext.BaseDirectory が一時展開フォルダになるため
        // 実際の exe の隣も探す
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(exeDir, ".env"),                                           // exe の隣（配布時）
            Path.Combine(AppContext.BaseDirectory, ".env"),                          // 開発: bin フォルダ
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env"), // 開発: ソリューションルート
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env"),
        };

        foreach (var path in candidates)
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) continue;
            Load(full);
            break;
        }
    }

    private void Load(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq < 0) continue;

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim().Trim('"').Trim('\'');
            _vars[key] = val;
        }
    }

    public string? Get(string key)
    {
        _vars.TryGetValue(key, out var val);
        // 空文字やプレースホルダーは null 扱い
        if (string.IsNullOrWhiteSpace(val) || val.StartsWith("YOUR_")) return null;
        return val;
    }

    public string GetOrDefault(string key, string defaultValue = "") =>
        Get(key) ?? defaultValue;
}
