using Avalonia;
using Avalonia.Fonts.Inter;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.Win32;
using Microsoft.Win32;

namespace NexStrap;

class Program
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexStrap", "crash.log");

    [STAThread]
    public static void Main(string[] args)
    {
        // DirectX アダプター初期化より前に設定する必要がある
        RequestHighPerformanceGpu();

        using var mutex = new Mutex(true, "NexStrap_SingleInstance", out bool createdNew);
        if (!createdNew) return;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void RequestHighPerformanceGpu()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            // Windows Settings > グラフィック > アプリの設定 > 高パフォーマンス と同等
            // GpuPreference=2 は DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\DirectX\UserGpuPreferences");
            key.SetValue(exePath, "GpuPreference=2;", RegistryValueKind.String);
        }
        catch { }
    }

    private static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex}\n\n";
            File.AppendAllText(LogPath, msg);
        }
        catch { }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                // ANGLE EGL → Direct3D 11 (GPU), WGL → OpenGL (GPU), Software fallback
                RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Wgl, Win32RenderingMode.Software],
            })
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = 256 * 1024 * 1024, // 256 MB GPU キャッシュ
            })
            .WithInterFont()
            .LogToTrace();
}
