using Newtonsoft.Json;

namespace NexStrap.Core.Services;

public record QuickLoginData(
    long    UserId,
    string  Username,
    string  DisplayName,
    string? AvatarUrl,
    string  PlaintextCookie);

public class QuickLoginService
{
    private const int ExpirySeconds = 180;

    private static readonly string StorageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "quicklogin");

    // メモリキャッシュ：カウントダウンタイマー用（同一プロセス内）
    private readonly Dictionary<string, DateTime> _expiries = [];

    private readonly AccountService _accountService;

    public QuickLoginService(AccountService accountService)
    {
        _accountService = accountService;
        Directory.CreateDirectory(StorageDir);
        PurgeExpired();
    }

    // アカウント ID からコードを生成してファイルへ保存
    public string? GenerateCode(Guid accountId)
    {
        var account = _accountService.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account == null) return null;

        string? cookie;
        try { cookie = _accountService.GetCookieById(accountId); }
        catch { return null; }
        if (cookie == null) return null;

        // 同一アカウントの既存コードを削除
        PurgeForAccount(accountId);

        var code    = Random.Shared.Next(100000, 999999).ToString();
        var expires = DateTime.UtcNow.AddSeconds(ExpirySeconds);

        var payload = new StoredCode
        {
            AccountId       = accountId,
            UserId          = account.UserId,
            Username        = account.Username,
            DisplayName     = account.DisplayName,
            AvatarUrl       = account.AvatarUrl,
            EncryptedCookie = AccountService.Encrypt(cookie),
            ExpiresAt       = expires,
        };

        File.WriteAllText(
            Path.Combine(StorageDir, $"{code}.json"),
            JsonConvert.SerializeObject(payload));

        _expiries[code] = expires;
        return code;
    }

    // コードを消費してアカウントデータを返す。期限切れ・不正な場合は null
    public QuickLoginData? Redeem(string code)
    {
        var path = Path.Combine(StorageDir, $"{code}.json");
        if (!File.Exists(path)) return null;

        try
        {
            var payload = JsonConvert.DeserializeObject<StoredCode>(File.ReadAllText(path));
            File.Delete(path);
            _expiries.Remove(code);

            if (payload == null || DateTime.UtcNow > payload.ExpiresAt) return null;

            var plainCookie = AccountService.Decrypt(payload.EncryptedCookie);
            return new QuickLoginData(
                payload.UserId,
                payload.Username,
                payload.DisplayName,
                payload.AvatarUrl,
                plainCookie);
        }
        catch
        {
            try { File.Delete(path); } catch { }
            return null;
        }
    }

    public TimeSpan? GetRemaining(string code)
    {
        if (!_expiries.TryGetValue(code, out var expires)) return null;
        var remaining = expires - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    private void PurgeForAccount(Guid accountId)
    {
        foreach (var file in Directory.GetFiles(StorageDir, "*.json"))
        {
            try
            {
                var payload = JsonConvert.DeserializeObject<StoredCode>(File.ReadAllText(file));
                if (payload?.AccountId == accountId) File.Delete(file);
            }
            catch { }
        }
    }

    private void PurgeExpired()
    {
        foreach (var file in Directory.GetFiles(StorageDir, "*.json"))
        {
            try
            {
                var payload = JsonConvert.DeserializeObject<StoredCode>(File.ReadAllText(file));
                if (payload == null || DateTime.UtcNow > payload.ExpiresAt) File.Delete(file);
            }
            catch { try { File.Delete(file); } catch { } }
        }
    }

    private class StoredCode
    {
        public Guid     AccountId       { get; set; }
        public long     UserId          { get; set; }
        public string   Username        { get; set; } = string.Empty;
        public string   DisplayName     { get; set; } = string.Empty;
        public string?  AvatarUrl       { get; set; }
        public string   EncryptedCookie { get; set; } = string.Empty;
        public DateTime ExpiresAt       { get; set; }
    }
}
