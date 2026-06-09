using System.Security.Cryptography;

namespace NexStrap.Services;

public sealed class RobloxCookieSessionService
{
    private static readonly string RobloxCookiesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "LocalStorage", "RobloxCookies.dat");

    private static readonly string AppStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "LocalStorage", "appStorage.json");

    public void ClearRobloxCookies()
    {
        try { File.Delete(RobloxCookiesPath); } catch { }
    }

    public void ClearAppStorageSession()
    {
        if (!File.Exists(AppStoragePath)) return;
        try
        {
            var json = File.ReadAllText(AppStoragePath);
            var obj  = Newtonsoft.Json.Linq.JObject.Parse(json);
            // 繧ｻ繝・す繝ｧ繝ｳ髢｢騾｣繝輔ぅ繝ｼ繝ｫ繝峨ｒ遨ｺ縺ｫ縺吶ｋ
            obj["CredentialValue"] = "";
            obj["AccountBlob"]     = "";
            if (obj.ContainsKey("WebLogin")) obj["WebLogin"] = Newtonsoft.Json.Linq.JValue.CreateNull();
            File.WriteAllText(AppStoragePath, obj.ToString(Newtonsoft.Json.Formatting.None));
            RobloxService.Log("AppStorage session cleared for multi-account launch");
        }
        catch (Exception ex) { RobloxService.Log($"ClearAppStorageSession failed: {ex.Message}"); }
    }

    public bool InjectAccountCookie(string robloSecurityCookie, string? targetPath = null)
    {
        var cookiesFilePath = targetPath ?? RobloxCookiesPath;
        try
        {
            string cookiesJson;
            if (File.Exists(cookiesFilePath))
            {
                cookiesJson = File.ReadAllText(cookiesFilePath);
                var obj     = System.Text.Json.JsonDocument.Parse(cookiesJson).RootElement;
                if (!obj.TryGetProperty("CookiesData", out var cookiesDataElem)) goto write_fresh;
                var encB64  = cookiesDataElem.GetString();
                if (encB64 == null) goto write_fresh;

                var encrypted = Convert.FromBase64String(encB64);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                var text      = System.Text.Encoding.UTF8.GetString(decrypted);

                // Netscape cookie 蠖｢蠑・ 繝輔ぅ繝ｼ繝ｫ繝峨・繧ｿ繝門玄蛻・ｊ縲・逡ｪ逶ｮ縺・name縲・逡ｪ逶ｮ縺・value
                var lines  = text.Split('\n').ToList();
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    var parts = lines[i].Split('\t');
                    if (parts.Length >= 7 && parts[5] == ".ROBLOSECURITY")
                    {
                        parts[6]  = robloSecurityCookie;
                        lines[i]  = string.Join("\t", parts);
                        found     = true;
                        break;
                    }
                }

                if (!found)
                {
                    // 繧ｨ繝ｳ繝医Μ縺後↑縺代ｌ縺ｰ霑ｽ蜉
                    lines.Add($"#HttpOnly_.roblox.com\tTRUE\t/\tTRUE\t0\t.ROBLOSECURITY\t{robloSecurityCookie}");
                }

                var newText      = string.Join("\n", lines);
                var newBytes     = System.Text.Encoding.UTF8.GetBytes(newText);
                var newEncrypted = ProtectedData.Protect(newBytes, null, DataProtectionScope.CurrentUser);
                var newJson      = $"{{\"CookiesVersion\":\"1\",\"CookiesData\":\"{Convert.ToBase64String(newEncrypted)}\"}}";
                File.WriteAllText(cookiesFilePath, newJson);
                return true;
            }

            write_fresh:
            {
                var lines    = new[] { $"#HttpOnly_.roblox.com\tTRUE\t/\tTRUE\t0\t.ROBLOSECURITY\t{robloSecurityCookie}" };
                var bytes    = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines));
                var enc      = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                var json     = $"{{\"CookiesVersion\":\"1\",\"CookiesData\":\"{Convert.ToBase64String(enc)}\"}}";
                Directory.CreateDirectory(Path.GetDirectoryName(cookiesFilePath)!);
                File.WriteAllText(cookiesFilePath, json);
                return true;
            }
        }
        catch (Exception ex)
        {
            RobloxService.Log($"InjectAccountCookie failed: {ex.Message}");
            return false;
        }
    }
}