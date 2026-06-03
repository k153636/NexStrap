using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace NexStrap.ViewModels.Installer;

public enum InstallerStep { Welcome, Install, Complete }

public partial class InstallerViewModel : ViewModelBase
{
    [ObservableProperty] private InstallerStep _step = InstallerStep.Welcome;
    [ObservableProperty] private string  _installPath = DefaultInstallPath;
    [ObservableProperty] private bool    _desktopShortcut    = true;
    [ObservableProperty] private bool    _startMenuShortcut  = true;
    [ObservableProperty] private bool    _isInstalling       = false;
    [ObservableProperty] private string  _statusMessage      = string.Empty;
    [ObservableProperty] private bool    _pathError          = false;
    [ObservableProperty] private string  _pathErrorMessage   = string.Empty;

    public IStorageProvider? StorageProvider { get; set; }
    public Action? CloseAction { get; set; }

    public static string DefaultInstallPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "NexStrap.exe");

    public static string AppVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            return v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v1.1";
        }
    }

    partial void OnInstallPathChanged(string value) => ValidatePath(value);

    private bool ValidatePath(string path)
    {
        PathError = false;
        PathErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            PathError = true; PathErrorMessage = "Please enter an install path."; return false;
        }
        var dir = Path.GetDirectoryName(path) ?? path;
        if (dir.Length <= 3 && dir.EndsWith(":\\"))
        {
            PathError = true; PathErrorMessage = "Cannot install to a drive root."; return false;
        }
        if (path.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
        {
            PathError = true; PathErrorMessage = "Cannot install to a temp directory."; return false;
        }
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFiles86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (path.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(programFiles86, StringComparison.OrdinalIgnoreCase))
        {
            PathError = true; PathErrorMessage = "Cannot install to Program Files (requires admin)."; return false;
        }
        return true;
    }

    [RelayCommand]
    private void Next() => Step = InstallerStep.Install;

    [RelayCommand]
    private void Back() => Step = InstallerStep.Welcome;

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (StorageProvider == null) return;
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Install Folder",
            AllowMultiple = false
        });
        if (folder.Count > 0)
            InstallPath = Path.Combine(folder[0].Path.LocalPath, "NexStrap.exe");
    }

    [RelayCommand]
    private void ResetPath() => InstallPath = DefaultInstallPath;

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (!ValidatePath(InstallPath)) return;

        IsInstalling = true;
        StatusMessage = "Creating directory...";

        try
        {
            var dir = Path.GetDirectoryName(InstallPath)!;
            Directory.CreateDirectory(dir);

            // 自分自身をインストール先にコピー
            StatusMessage = "Copying files...";
            var currentExe = Environment.ProcessPath!;
            if (!string.Equals(currentExe, InstallPath, StringComparison.OrdinalIgnoreCase))
                File.Copy(currentExe, InstallPath, overwrite: true);

            // レジストリ登録（コントロールパネルのアンインストール一覧）
            StatusMessage = "Registering application...";
            RegisterUninstall(InstallPath);

            // プロトコルハンドラを新しいパスで更新
            RegisterProtocol(InstallPath);

            // ショートカット作成
            StatusMessage = "Creating shortcuts...";
            if (DesktopShortcut)
                CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NexStrap.lnk"),
                    InstallPath);
            if (StartMenuShortcut)
            {
                var smDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                Directory.CreateDirectory(smDir);
                CreateShortcut(Path.Combine(smDir, "NexStrap.lnk"), InstallPath);
            }

            await Task.Delay(300);
            StatusMessage = "Done!";
            Step = InstallerStep.Complete;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsInstalling = false; }
    }

    [RelayCommand]
    private void LaunchInstalled()
    {
        try
        {
            // Environment.Exit でインストーラープロセスを完全終了してから起動。
            // ウィンドウを閉じるだけでは Mutex が解放されず、起動した EXE が Mutex 取得に失敗する。
            Process.Start(new ProcessStartInfo(InstallPath) { UseShellExecute = true });
        }
        catch { }
        Environment.Exit(0);
    }

    [RelayCommand]
    private void CloseInstaller() => CloseAction?.Invoke();

    // ─── helpers ────────────────────────────────────────────────────────────

    private static void RegisterUninstall(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NexStrap");
        key.SetValue("DisplayName",         "NexStrap");
        key.SetValue("DisplayVersion",      AppVersion.TrimStart('v'));
        key.SetValue("DisplayIcon",         exePath);
        key.SetValue("InstallLocation",     Path.GetDirectoryName(exePath)!);
        key.SetValue("Publisher",           "k153636");
        key.SetValue("UninstallString",     $"\"{exePath}\" --uninstall");
        key.SetValue("HelpLink",            "https://github.com/k153636/NexStrap");
        key.SetValue("URLUpdateInfo",       "https://github.com/k153636/NexStrap/releases");
        key.SetValue("NoModify",            1, RegistryValueKind.DWord);
    }

    private static void RegisterProtocol(string exePath)
    {
        foreach (var scheme in new[] { "roblox", "roblox-player" })
        {
            using var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}");
            root.SetValue("", $"URL:{scheme} Protocol");
            root.SetValue("URL Protocol", "");
            using var icon = root.CreateSubKey("DefaultIcon");
            icon.SetValue("", exePath);
            using var cmd = root.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exePath}\" \"%1\"");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

    private static void CreateShortcut(string lnkPath, string targetPath)
    {
        try
        {
            // WScript.Shell COM を使用してショートカット作成
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            var shortcut = shell.CreateShortcut(lnkPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Description = "NexStrap — Roblox Launcher";
            shortcut.Save();
        }
        catch { }
    }

    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NexStrap");
        return key != null;
    }
}
