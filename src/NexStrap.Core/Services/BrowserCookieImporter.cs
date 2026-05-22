using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;

namespace NexStrap.Core.Services;

public enum BrowserType { Chrome, Edge }

public static class BrowserCookieImporter
{
    public static async Task<string?> TryImportAsync(BrowserType browser)
    {
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var (localStatePath, cookiesPath) = browser switch
        {
            BrowserType.Chrome => (
                Path.Combine(localApp, "Google", "Chrome", "User Data", "Local State"),
                Path.Combine(localApp, "Google", "Chrome", "User Data", "Default", "Network", "Cookies")),
            _ => (
                Path.Combine(localApp, "Microsoft", "Edge", "User Data", "Local State"),
                Path.Combine(localApp, "Microsoft", "Edge", "User Data", "Default", "Network", "Cookies"))
        };

        if (!File.Exists(localStatePath) || !File.Exists(cookiesPath))
            return null;

        string? tempFile = null;
        try
        {
            var key = await Task.Run(() => GetAesKey(localStatePath));
            if (key == null) return null;

            tempFile = Path.GetTempFileName();
            await Task.Run(() => File.Copy(cookiesPath, tempFile, overwrite: true));

            byte[]? encryptedValue = null;
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection($"Data Source={tempFile};Mode=ReadOnly");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT encrypted_value FROM cookies WHERE host_key LIKE '%roblox.com' AND name = '.ROBLOSECURITY' LIMIT 1";
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                    encryptedValue = (byte[])reader[0];
            });

            if (encryptedValue == null || encryptedValue.Length < 3) return null;

            if (encryptedValue[0] == (byte)'v' &&
                encryptedValue[1] == (byte)'1' &&
                encryptedValue[2] == (byte)'0')
            {
                return await Task.Run(() => DecryptAesGcm(key, encryptedValue));
            }

            return null;
        }
        catch { return null; }
        finally
        {
            if (tempFile != null)
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    private static byte[]? GetAesKey(string localStatePath)
    {
        try
        {
            var json = File.ReadAllText(localStatePath);
            var obj = JObject.Parse(json);
            var encryptedKeyB64 = obj["os_crypt"]?["encrypted_key"]?.Value<string>();
            if (encryptedKeyB64 == null) return null;

            var encryptedKeyWithPrefix = Convert.FromBase64String(encryptedKeyB64);
            var encryptedKey = encryptedKeyWithPrefix[5..]; // strip "DPAPI" prefix
            return ProtectedData.Unprotect(encryptedKey, null, DataProtectionScope.CurrentUser);
        }
        catch { return null; }
    }

    private static string? DecryptAesGcm(byte[] key, byte[] encryptedValue)
    {
        try
        {
            var nonce      = encryptedValue[3..15];
            var tag        = encryptedValue[^16..];
            var ciphertext = encryptedValue[15..^16];
            var plaintext  = new byte[ciphertext.Length];

            using var aesGcm = new AesGcm(key, 16);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch { return null; }
    }
}
