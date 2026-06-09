using System.Diagnostics;

namespace NexStrap.Services;

public static class RobloxProcessService
{
    public static Process? TryStartProcess(string playerPath, string? launchArgs, string? isolatedDataDir = null)
    {
        var psi = new ProcessStartInfo(playerPath)
        {
            WorkingDirectory = Path.GetDirectoryName(playerPath)!,
            Arguments        = launchArgs ?? string.Empty
        };

        if (isolatedDataDir != null)
        {
            psi.UseShellExecute = false;
            foreach (System.Collections.DictionaryEntry kv in System.Environment.GetEnvironmentVariables())
                psi.Environment[(string)kv.Key] = (string?)kv.Value ?? "";
            psi.Environment["LOCALAPPDATA"] = isolatedDataDir;
        }
        else
        {
            psi.UseShellExecute = true;
        }

        return Process.Start(psi);
    }

    public static async Task<bool> WaitForMainWindowAsync(Process proc, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (proc.HasExited) return false;
                proc.Refresh();
                if (proc.MainWindowHandle != IntPtr.Zero)
                    return true;
            }
            catch
            {
                return false;
            }

            await Task.Delay(500);
        }

        return false;
    }
}
