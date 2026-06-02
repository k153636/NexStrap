using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using NexStrap.Core.Models;

namespace NexStrap.Core.Services;

public class AccountService
{
    private readonly string _filePath;
    private readonly List<RobloxAccount> _accounts;

    public IReadOnlyList<RobloxAccount> Accounts => _accounts;

    public AccountService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexStrap");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "accounts.json");
        _accounts = Load();
    }

    public void Add(RobloxAccount account, string plaintextCookie)
    {
        account.EncryptedCookie = Encrypt(plaintextCookie);
        _accounts.Add(account);
        Save();
    }

    public void Remove(Guid id)
    {
        var item = _accounts.FirstOrDefault(a => a.Id == id);
        if (item != null)
        {
            _accounts.Remove(item);
            Save();
        }
    }

    public void SetActive(Guid id)
    {
        foreach (var a in _accounts)
            a.IsActive = a.Id == id;
        Save();
    }

    public string? GetCookieByIndex(int index)
    {
        if (_accounts.Count == 0) return null;
        var account = _accounts[index % _accounts.Count];
        if (string.IsNullOrEmpty(account.EncryptedCookie)) return null;
        try { return Decrypt(account.EncryptedCookie); }
        catch { return null; }
    }

    public string? GetActiveCookie()
    {
        var active = _accounts.FirstOrDefault(a => a.IsActive);
        if (active == null || string.IsNullOrEmpty(active.EncryptedCookie)) return null;
        try { return Decrypt(active.EncryptedCookie); }
        catch { return null; }
    }

    private List<RobloxAccount> Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return [];
            var json = File.ReadAllText(_filePath);
            return JsonConvert.DeserializeObject<List<RobloxAccount>>(json) ?? [];
        }
        catch { return []; }
    }

    private void Save()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_accounts, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }

    private static string Encrypt(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string Decrypt(string base64)
    {
        var encrypted = Convert.FromBase64String(base64);
        var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }
}
