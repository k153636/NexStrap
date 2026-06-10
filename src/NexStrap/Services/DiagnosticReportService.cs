using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace NexStrap.Services;

public sealed class DiagnosticReportService(RobloxService roblox)
{
    private const int LogTailLines = 100;

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "debug.log");

    public string GenerateReport()
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== User Report ===");
        sb.AppendLine("Symptoms:");
        sb.AppendLine();
        sb.AppendLine("Steps to reproduce:");
        sb.AppendLine();
        sb.AppendLine("Expected behavior:");
        sb.AppendLine();
        sb.AppendLine("Actual behavior:");
        sb.AppendLine();
        sb.AppendLine("Screenshot/video:");
        sb.AppendLine();
        sb.AppendLine();

        sb.AppendLine("=== Diagnostic Information ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"NexStrap Version: {GetNexStrapVersion()}");
        sb.AppendLine($"OS Version: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine("Roblox Channel: LIVE");
        sb.AppendLine($"Version GUID: {roblox.CachedVersionGuid ?? "Unknown"}");
        sb.AppendLine($"Installed Version Folder: {GetInstalledVersionFolderName()}");
        sb.AppendLine($"Roblox Status: {roblox.Status}");
        sb.AppendLine();
        sb.AppendLine("Last Install Result:");
        sb.AppendLine($"  CDN Install: {FormatTriState(roblox.LastCdnInstallSuccess, "Success", "Failed")}");
        sb.AppendLine($"  Official Installer Fallback Used: {FormatYesNo(roblox.LastOfficialInstallerFallbackUsed)}");
        sb.AppendLine($"  Stock Copy Fallback Used: {FormatYesNo(roblox.LastStockCopyFallbackUsed)}");
        sb.AppendLine($"  RobloxPlayerBeta.exe Found: {FormatYesNo(roblox.LastRobloxPlayerBetaFound)}");
        sb.AppendLine($"  Last Error: {FormatLastError(roblox.LastInstallError)}");
        sb.AppendLine();
        sb.AppendLine("=== debug.log (last 100 lines, masked) ===");
        sb.Append(GetMaskedLogTail());

        return sb.ToString();
    }

    private static string GetNexStrapVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version?.ToString() ?? "Unknown";
    }

    private string GetInstalledVersionFolderName()
    {
        var path = roblox.RobloxVersionPath;
        return string.IsNullOrEmpty(path) ? "Unknown" : Path.GetFileName(path);
    }

    private const string UnknownNoAttempt = "Unknown (no install attempt in this session)";

    private static string FormatYesNo(bool? value) => value switch
    {
        true  => "Yes",
        false => "No",
        null  => UnknownNoAttempt,
    };

    private static string FormatTriState(bool? value, string trueText, string falseText) => value switch
    {
        true  => trueText,
        false => falseText,
        null  => UnknownNoAttempt,
    };

    private static string FormatLastError(string? error) =>
        string.IsNullOrEmpty(error) ? UnknownNoAttempt : DiagnosticLogMasker.Mask(error);

    private static string GetMaskedLogTail()
    {
        try
        {
            if (!File.Exists(LogFilePath)) return "(no log file)";

            var lines = File.ReadAllLines(LogFilePath);
            var tail  = lines.Skip(Math.Max(0, lines.Length - LogTailLines));
            return string.Join('\n', tail.Select(DiagnosticLogMasker.Mask));
        }
        catch (Exception ex)
        {
            return $"(failed to read log: {ex.Message})";
        }
    }
}
