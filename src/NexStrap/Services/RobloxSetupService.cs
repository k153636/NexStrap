using System.Diagnostics;
using Microsoft.Win32;

namespace NexStrap.Services;

public sealed class RobloxSetupService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private const int BufferSize = 65536;

    static RobloxSetupService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxStudio/WinInet");
    }

    public bool IsVcRedistInstalled()
    {
        var paths = new[]
        {
            @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64",
            @"SOFTWARE\WoW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X64",
            @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
        };
        foreach (var path in paths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key?.GetValue("Installed") is int v && v == 1) return true;
            }
            catch { }
        }

        try
        {
            foreach (var uninstallPath in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WoW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            })
            {
                using var key = Registry.LocalMachine.OpenSubKey(uninstallPath);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var entry = key.OpenSubKey(sub);
                    var name = entry?.GetValue("DisplayName") as string;
                    if (name != null && name.Contains("Microsoft Visual C++") &&
                        name.Contains("2015") || name != null && name.Contains("Redistributable") &&
                        name?.Contains("14.") == true)
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    internal async Task CheckAndInstallVcRedistAsync(RobloxProgressReporter reportProgress)
    {
        if (IsVcRedistInstalled()) return;

        RobloxService.Log("VC++ 2015-2022 x64 not found, downloading...");
        reportProgress("Downloading vc_redist.x64.exe", 0);

        var tempExe = Path.Combine(Path.GetTempPath(), "vc_redist.x64.exe");
        try
        {
            using var resp = await Http.GetAsync(
                "https://aka.ms/vs/17/release/vc_redist.x64.exe",
                HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            var total     = resp.Content.Headers.ContentLength ?? 0;
            var startTime = DateTime.UtcNow;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(tempExe);

            var buf  = new byte[BufferSize];
            long got = 0;
            int  n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                got += n;
                var pct     = total > 0 ? got / (double)total * 100.0 : 0;
                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                reportProgress($"Downloading vc_redist.x64.exe ({got / 1024:N0} KB)", pct,
                    detail: FormatSpeed(elapsed > 0.1 ? got / elapsed : 0));
            }
        }
        catch (Exception ex) { RobloxService.Log($"Failed to download VC++ redist: {ex.Message}"); return; }

        try
        {
            DownloadSecurityVerifier.VerifySignedExecutable(tempExe, "CN=Microsoft Corporation");
        }
        catch (Exception ex)
        {
            RobloxService.Log($"VC++ redist signature verification failed: {ex.Message}");
            try { File.Delete(tempExe); } catch { }
            return;
        }

        reportProgress("Installing vc_redist.x64.exe", 100, indeterminate: true);
        try
        {
            var proc = Process.Start(new ProcessStartInfo(tempExe)
            {
                Arguments       = "/install /quiet /norestart",
                UseShellExecute = true
            });
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                RobloxService.Log($"VC++ redist install exited with code {proc.ExitCode}");
            }
        }
        catch (Exception ex) { RobloxService.Log($"Failed to install VC++ redist: {ex.Message}"); }
        finally { try { File.Delete(tempExe); } catch { } }
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1_048_576) return $"{bytesPerSec / 1_048_576:F1} MB/s";
        if (bytesPerSec >= 1024)      return $"{bytesPerSec / 1024:F0} KB/s";
        return $"{(int)bytesPerSec} B/s";
    }
}
