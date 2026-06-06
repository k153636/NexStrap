using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace NexStrap.Services;

public class UpdateService
{
    private const string GithubApiUrl = "https://api.github.com/repos/k153636/NexStrap/releases/latest";
    private const string AssetName    = "NexStrap.exe";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("NexStrap-Updater/1.0");
    }

    // Returns (tag, download URL) if a newer release exists, null otherwise
    public async Task<(string Version, string DownloadUrl)?> CheckForUpdateAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(GithubApiUrl);
            using var doc  = JsonDocument.Parse(json);
            var root       = doc.RootElement;

            var tag        = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
            if (!Version.TryParse(tag, out var latest)) return null;

            // PE ファイルのバージョンリソースから直接読む（最も信頼性が高い）。
            // Assembly.GetExecutingAssembly() は NexStrap.dll を指すため
            // NexStrap.exe とは別バージョンになる場合があり使用しない。
            Version? current = null;
            var exePath = Environment.ProcessPath;
            if (exePath != null && File.Exists(exePath))
            {
                var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrEmpty(fvi.FileVersion))
                    Version.TryParse(fvi.FileVersion, out current);
            }

            // フォールバック: エントリアセンブリの InformationalVersion
            if (current == null)
            {
                var infoVer = Assembly.GetEntryAssembly()
                    ?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;
                var vStr = infoVer?.Split('+')[0].Split('-')[0];
                if (Version.TryParse(vStr, out var v2)) current = v2;
            }

            // 最終フォールバック: AssemblyName.Version
            current ??= Assembly.GetEntryAssembly()?.GetName().Version;

            RobloxService.Log($"UpdateCheck: current={current} latest={latest}");
            if (current != null && latest <= current) return null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != AssetName) continue;
                var url = asset.GetProperty("browser_download_url").GetString();
                if (url != null) return (tag, url);
            }
        }
        catch { }
        return null;
    }

    // Downloads the new exe, writes an updater script, launches it, then exits the app
    public async Task DownloadAndApplyAsync(string downloadUrl, Action<BootstrapperProgress> report)
    {
        report(new BootstrapperProgress("Downloading NexStrap update...", 0, false));

        var currentExe = Environment.ProcessPath;
        if (currentExe == null) return;

        var tempExe = Path.Combine(Path.GetTempPath(), "NexStrap_update.exe");

        try
        {
            using var resp = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            var total     = resp.Content.Headers.ContentLength ?? 0;
            var buf       = new byte[65536];
            long received = 0;
            var started   = DateTime.UtcNow;

            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(tempExe);

            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                received += n;

                var pct     = total > 0 ? received / (double)total * 100.0 : 0;
                var elapsed = (DateTime.UtcNow - started).TotalSeconds;
                var speed   = elapsed > 0.1 ? FormatSpeed(received / elapsed) : "";
                report(new BootstrapperProgress("Downloading NexStrap update...", pct, false, speed));
            }
        }
        catch { return; }

        report(new BootstrapperProgress("Applying update...", 100, true));

        // Write a cmd script that waits for this process to exit, replaces the exe, and restarts
        var scriptPath = Path.Combine(Path.GetTempPath(), "nexstrap_update.cmd");
        await File.WriteAllTextAsync(scriptPath,
            $"""
            @echo off
            timeout /t 2 /nobreak >nul
            move /y "{tempExe}" "{currentExe}"
            start "" "{currentExe}"
            del "%~f0"
            """);

        Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments      = $"/c \"{scriptPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });

        Environment.Exit(0);
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1_048_576) return $"{bytesPerSec / 1_048_576:F1} MB/s";
        if (bytesPerSec >= 1024)      return $"{bytesPerSec / 1024:F0} KB/s";
        return $"{(int)bytesPerSec} B/s";
    }
}
