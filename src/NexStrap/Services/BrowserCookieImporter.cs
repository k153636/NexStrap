using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;

namespace NexStrap.Services;

public enum BrowserType { Chrome, Edge }

public sealed record BrowserCookieImportResult(
    IReadOnlyList<string> Cookies,
    bool HasUnsupportedEncryption,
    bool HasLockedProfile);

public static class BrowserCookieImporter
{
    public static async Task<BrowserCookieImportResult> TryImportAsync(BrowserType browser)
    {
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var userDataPath = browser switch
        {
            BrowserType.Chrome => Path.Combine(localApp, "Google", "Chrome", "User Data"),
            _ => Path.Combine(localApp, "Microsoft", "Edge", "User Data")
        };
        var localStatePath = Path.Combine(userDataPath, "Local State");

        if (!File.Exists(localStatePath) || !Directory.Exists(userDataPath))
            return new BrowserCookieImportResult([], false, false);

        var key = await Task.Run(() => GetAesKey(localStatePath));
        var cookies = new HashSet<string>(StringComparer.Ordinal);
        var hasUnsupportedEncryption = false;
        var hasLockedProfile = false;

        var profilePaths = Directory.EnumerateDirectories(userDataPath)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal);
            })
            .OrderBy(path => Path.GetFileName(path) == "Default" ? 0 : 1)
            .ThenBy(Path.GetFileName);

        foreach (var profilePath in profilePaths)
        {
            var cookiesPath = Path.Combine(profilePath, "Network", "Cookies");
            if (!File.Exists(cookiesPath)) continue;

            string? tempFile = null;
            tempFile = Path.GetTempFileName();
            try
            {
                await Task.Run(() => File.Copy(cookiesPath, tempFile, overwrite: true));
                await Task.Run(() =>
                {
                    using var conn = new SqliteConnection($"Data Source={tempFile};Mode=ReadOnly");
                    conn.Open();
                    using var versionCmd = conn.CreateCommand();
                    versionCmd.CommandText = "SELECT value FROM meta WHERE key = 'version'";
                    var dbVersion = Convert.ToInt32(versionCmd.ExecuteScalar() ?? 0);

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText =
                        "SELECT host_key, value, encrypted_value FROM cookies " +
                        "WHERE host_key LIKE '%roblox.com' AND name = '.ROBLOSECURITY' " +
                        "ORDER BY last_access_utc DESC";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var hostKey = reader.GetString(0);
                        var value = reader.IsDBNull(1) ? null : reader.GetString(1);
                        if (!string.IsNullOrEmpty(value))
                        {
                            cookies.Add(value);
                            continue;
                        }

                        if (reader.IsDBNull(2)) continue;
                        var encryptedValue = (byte[])reader[2];
                        if (encryptedValue.Length < 3) continue;

                        var prefix = Encoding.ASCII.GetString(encryptedValue, 0, 3);
                        if (prefix == "v20")
                        {
                            hasUnsupportedEncryption = true;
                            continue;
                        }

                        if (prefix != "v10" || key == null) continue;
                        var decrypted = DecryptAesGcm(
                            key,
                            encryptedValue,
                            hostKey,
                            dbVersion >= 24);
                        if (!string.IsNullOrEmpty(decrypted))
                            cookies.Add(decrypted);
                    }
                });
            }
            catch
            {
                // A running browser can lock its active profile. Continue with
                // other profiles and let the coordinator retry when appropriate.
                hasLockedProfile = true;
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        return new BrowserCookieImportResult(
            cookies.ToList(),
            hasUnsupportedEncryption,
            hasLockedProfile);
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

    private static string? DecryptAesGcm(
        byte[] key,
        byte[] encryptedValue,
        string hostKey,
        bool hasHostHash)
    {
        try
        {
            var nonce      = encryptedValue[3..15];
            var tag        = encryptedValue[^16..];
            var ciphertext = encryptedValue[15..^16];
            var plaintext  = new byte[ciphertext.Length];

            using var aesGcm = new AesGcm(key, 16);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

            var valueOffset = 0;
            if (hasHostHash)
            {
                const int hashLength = 32;
                if (plaintext.Length < hashLength) return null;
                var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(hostKey));
                if (!CryptographicOperations.FixedTimeEquals(
                        expectedHash,
                        plaintext.AsSpan(0, hashLength)))
                    return null;
                valueOffset = hashLength;
            }

            return Encoding.UTF8.GetString(plaintext, valueOffset, plaintext.Length - valueOffset);
        }
        catch { return null; }
    }
}
