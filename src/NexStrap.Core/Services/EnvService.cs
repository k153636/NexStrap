namespace NexStrap.Core.Services;

public class EnvService
{
    private readonly Dictionary<string, string> _vars = new();

    public EnvService()
    {
        // 実行ファイルの場所から .env を探す（開発時はソリューションルートも検索）
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, ".env"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env"),
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
