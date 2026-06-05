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

    /// <summary>
    /// 埋め込みリソースからインストールする（Studio 起動時の更新チェック用）。
    /// 既存ファイルと内容が同じなら上書きしない。
    /// </summary>
    public static bool EnsureInstalled()
    {
        try
        {
            var content = ReadEmbeddedPlugin();
            if (content == null) return false;

            Directory.CreateDirectory(PluginDir);

            if (File.Exists(PluginPath))
            {
                var existing = File.ReadAllText(PluginPath, Encoding.UTF8);
                if (existing == content) return true;
            }

            File.WriteAllText(PluginPath, content, Encoding.UTF8);
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
        try
        {
            progress?.Report(("Downloading Studio plugin...", 0, true));

            using var http    = new HttpClient();
            http.Timeout      = TimeSpan.FromSeconds(30);
            var content       = await http.GetStringAsync(DownloadUrl, ct);

            progress?.Report(("Installing Studio plugin...", 80, false));

            Directory.CreateDirectory(PluginDir);
            await File.WriteAllTextAsync(PluginPath, content, Encoding.UTF8, ct);

            progress?.Report(("Plugin installed.", 100, false));
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch
        {
            // ダウンロード失敗時は埋め込みリソースにフォールバック
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
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch { return null; }
    }
}
