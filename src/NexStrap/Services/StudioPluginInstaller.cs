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

    public static string PluginPath => Path.Combine(PluginDir, FileName);

    /// <summary>プラグインがインストール済みかどうか。</summary>
    public static bool IsInstalled => File.Exists(PluginPath);

    /// <summary>埋め込みリソースとインストール済みファイルが一致しているか。</summary>
    public static bool IsUpToDate()
    {
        if (!File.Exists(PluginPath)) return false;
        var content = ReadEmbeddedPlugin();
        if (content == null) return false;
        var newBytes      = new UTF8Encoding(false).GetBytes(content);
        var existingBytes = File.ReadAllBytes(PluginPath);
        return existingBytes.SequenceEqual(newBytes);
    }

    /// <summary>
    /// 埋め込みリソースからインストール／上書きする。
    /// </summary>
    public static bool EnsureInstalled()
    {
        try
        {
            var content = ReadEmbeddedPlugin();
            if (content == null) return false;
            Directory.CreateDirectory(PluginDir);
            File.WriteAllText(PluginPath, content, new UTF8Encoding(false));
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// GitHub からプラグインをダウンロードしてインストールする。
    /// 進捗を <paramref name="progress"/> に報告する。
    /// </summary>
    public static async Task<bool> DownloadAndInstallAsync(
        IProgress<(string Message, double Percent, bool Indeterminate)>? progress = null,
        CancellationToken ct = default)
    {
        var log = NexStrap.Core.Services.Logger.Instance;
        try
        {
            log.Info("StudioPlugin", $"Downloading from {DownloadUrl}");
            progress?.Report(("Downloading Studio plugin...", 0, true));

            using var http    = new HttpClient();
            http.Timeout      = TimeSpan.FromSeconds(30);
            var content       = await http.GetStringAsync(DownloadUrl, ct);

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
            log.Warning("StudioPlugin", $"Download failed ({ex.Message}), falling back to embedded resource");
            return EnsureInstalled();
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
}
