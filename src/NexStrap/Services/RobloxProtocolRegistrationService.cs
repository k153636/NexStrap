using Microsoft.Win32;

namespace NexStrap.Services;

public static class RobloxProtocolRegistrationService
{
    private static readonly string VersionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "Versions");

    public static void RegisterProtocolHandler()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        var command = $"\"{exe}\" \"%1\"";
        var versionFolder = Path.GetFileName(
            Directory.Exists(VersionsDir)
                ? Directory.GetDirectories(VersionsDir).FirstOrDefault() ?? string.Empty
                : string.Empty);

        foreach (var scheme in new[] { "roblox", "roblox-player" })
        {
            try
            {
                using var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}");
                root.SetValue("", $"URL:{scheme} Protocol");
                root.SetValue("URL Protocol", "");

                using var icon = root.CreateSubKey("DefaultIcon");
                icon.SetValue("", exe);

                using var cmd = root.CreateSubKey(@"shell\open\command");
                cmd.SetValue("", command);
                if (!string.IsNullOrEmpty(versionFolder))
                    cmd.SetValue("version", versionFolder);
            }
            catch (Exception ex) { RobloxService.Log($"RegisterProtocolHandler({scheme}): {ex.Message}"); }
        }
    }
}
