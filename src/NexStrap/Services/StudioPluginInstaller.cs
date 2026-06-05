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

    public static string PluginPath => Path.Combine(PluginDir, FileName);

    /// <summary>プラグインがインストール済みかどうか。</summary>
    public static bool IsInstalled => File.Exists(PluginPath);

    /// <summary>
    /// プラグインを Plugins フォルダにインストールする。
    /// 既存ファイルと内容が同じなら上書きしない。
    /// </summary>
    /// <returns>インストール成功なら true。</returns>
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
                if (existing == content) return true; // 変更なし
            }

            File.WriteAllText(PluginPath, content, Encoding.UTF8);
            return true;
        }
        catch { return false; }
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
