namespace NexStrap.Core.Services;

public class QuickLoginService
{
    private const int ExpirySeconds = 180;

    private readonly record struct Entry(Guid AccountId, DateTime ExpiresAt);
    private readonly Dictionary<string, Entry> _codes = [];

    public string GenerateCode(Guid accountId)
    {
        // 同一アカウントの既存コードを削除
        var existing = _codes.FirstOrDefault(kv => kv.Value.AccountId == accountId);
        if (existing.Key != null) _codes.Remove(existing.Key);

        var code = Random.Shared.Next(100000, 999999).ToString();
        _codes[code] = new Entry(accountId, DateTime.UtcNow.AddSeconds(ExpirySeconds));
        return code;
    }

    // コードに紐づくアカウントIDを返す。期限切れ・存在しない場合はnull
    public Guid? Redeem(string code)
    {
        if (!_codes.TryGetValue(code, out var entry)) return null;
        _codes.Remove(code);
        if (DateTime.UtcNow > entry.ExpiresAt) return null;
        return entry.AccountId;
    }

    public TimeSpan? GetRemaining(string code)
    {
        if (!_codes.TryGetValue(code, out var entry)) return null;
        var remaining = entry.ExpiresAt - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : null;
    }
}
