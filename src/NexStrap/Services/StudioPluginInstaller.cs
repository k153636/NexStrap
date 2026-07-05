using System.Net.Http;
using System.Reflection;
using System.Text;

namespace NexStrap.Services;

/// <summary>
/// NexStrapStudioRPC.lua を Roblox Studio の Plugins フォルダに自動インストールする。
/// </summary>
public static class StudioPluginInstaller
{
    private static readonly string PluginDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "Plugins");

    private const string FileName     = "NexStrapStudioRPC.lua";
    private const string ResourceName = "NexStrap.Plugins.NexStrapStudioRPC.lua";

    private const string DownloadUrl =
        "https://raw.githubusercontent.com/k153636/NexStrap/master/plugins/NexStrapStudioRPC.lua";
    private const int MaxPluginBytes = 512 * 1024;

    public static string PluginPath => Path.Combine(PluginDir, FileName);

    /// <summary>プラグインがインストール済みかどうか。</summary>
    public static bool IsInstalled => File.Exists(PluginPath);

    /// <summary>
    /// GitHub の最新版とインストール済みファイルを比較して更新が必要かどうかを返す。
    /// ネット接続がない場合や取得失敗時は false（更新不要）を返す。
    /// </summary>
    public static async Task<bool> IsUpdateAvailableAsync(CancellationToken ct = default)
    {
        var log = NexStrap.Services.Logger.Instance;
        try
        {
            var embedded = ReadEmbeddedPlugin();
            if (File.Exists(PluginPath) && embedded is not null)
            {
                var installed = await File.ReadAllTextAsync(PluginPath, new UTF8Encoding(false), ct);
                if (GetVersion(embedded) > GetVersion(installed))
                    return true;
            }

            DownloadSecurityVerifier.EnsureAllowedHttpsUrl(DownloadUrl, "raw.githubusercontent.com");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var remote = await http.GetStringAsync(DownloadUrl, ct);
            remote = remote.TrimStart('﻿'); // BOM 除去

            if (!File.Exists(PluginPath))
            {
                log.Info("StudioPlugin", "未インストール → 更新あり");
                return true;
            }

            var existing = await File.ReadAllTextAsync(PluginPath, new UTF8Encoding(false), ct);
            var desired = GetVersion(embedded) > GetVersion(remote) ? embedded! : remote;
            var needsUpdate = desired != existing;
            log.Info("StudioPlugin", needsUpdate ? "GitHub と差異あり → 更新あり" : "最新版がインストール済み");
            return needsUpdate;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            log.Warning("StudioPlugin", $"更新チェック失敗（オフライン？）: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// GitHub からプラグインをダウンロードしてインストールする。
    /// 進捗を <paramref name="progress"/> に報告する。
    /// </summary>
    public static async Task<bool> DownloadAndInstallAsync(
        IProgress<(string Message, double Percent, bool Indeterminate)>? progress = null,
        CancellationToken ct = default)
    {
        var log = NexStrap.Services.Logger.Instance;
        try
        {
            log.Info("StudioPlugin", $"Downloading from {DownloadUrl}");
            progress?.Report(("Downloading Studio plugin...", 0, true));
            DownloadSecurityVerifier.EnsureAllowedHttpsUrl(DownloadUrl, "raw.githubusercontent.com");

            using var http    = new HttpClient();
            http.Timeout      = TimeSpan.FromSeconds(30);
            using var resp    = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            if (resp.Content.Headers.ContentLength is long len && len > MaxPluginBytes)
                throw new InvalidOperationException("Studio plugin payload is unexpectedly large.");
            var content = await resp.Content.ReadAsStringAsync(ct);
            if (content.Length > MaxPluginBytes)
                throw new InvalidOperationException("Studio plugin payload is unexpectedly large.");
            var embedded = ReadEmbeddedPlugin();
            if (GetVersion(embedded) > GetVersion(content))
                content = embedded!;

            progress?.Report(("Installing Studio plugin...", 80, false));

            Directory.CreateDirectory(PluginDir);
            await File.WriteAllTextAsync(PluginPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);

            log.Info("StudioPlugin", $"Installed to {PluginPath}");
            progress?.Report(("Plugin installed.", 100, false));
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            // ダウンロード失敗時は埋め込みリソースにフォールバック
            log.Warning("StudioPlugin", $"ダウンロード失敗 ({ex.Message})、埋め込みリソースを使用");
            var embedded = ReadEmbeddedPlugin();
            if (embedded == null) return false;
            try
            {
                Directory.CreateDirectory(PluginDir);
                await File.WriteAllTextAsync(PluginPath, embedded, new UTF8Encoding(false), ct);
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>プラグインをアンインストールする。</summary>
    public static void Uninstall()
    {
        try { if (File.Exists(PluginPath)) File.Delete(PluginPath); } catch { }
    }

    private static string? ReadEmbeddedPlugin()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(ResourceName);
            if (stream == null) return null;
            using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var content = reader.ReadToEnd();
            // BOM が含まれている場合は除去（Lua は U+FEFF を認識しないため）
            return content.TrimStart('﻿');
        }
        catch { return null; }
    }

    private static Version GetVersion(string? content)
    {
        if (content is null) return new Version();
        const string marker = "local VERSION = \"";
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return new Version();
        start += marker.Length;
        var end = content.IndexOf('"', start);
        return end > start && Version.TryParse(content[start..end], out var version)
            ? version
            : new Version();
    }
}
