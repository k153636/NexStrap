using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NexStrap.Modules.Infrastructure.DependencyInjection;
using NexStrap.Modules.Infrastructure.Startup;
using NexStrap.Modules.Roblox.Protocol;
using NexStrap.Services;

namespace NexStrap;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private bool _isBackground;
    private bool _isPlaying;
    internal bool IsExiting { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();
        }
        catch (Exception ex)
        {
            RobloxService.Log($"DI initialization failed: {ex}");
            var msg = $"NexStrap failed to initialize:\n\n{ex.Message}\n\nCheck %LOCALAPPDATA%\\NexStrap\\crash.log";
            NativeMessageBox(msg, "NexStrap Error");
            Environment.Exit(1);
            return;
        }

        try { JumpListService.Initialize(); } catch { }

        RobloxService.RegisterProtocolHandler();

        var args = Environment.GetCommandLineArgs();
        RobloxService.Log($"Args: {string.Join(" | ", args)}");

        var protocolLaunchHandler = Services.GetRequiredService<RobloxProtocolLaunchHandler>();

        if (protocolLaunchHandler.TryGetRobloxUrl(args, out var robloxUrl))
        {
            Task.Run(() => protocolLaunchHandler.HandleRobloxUrlLaunchAsync(robloxUrl)).GetAwaiter().GetResult();
            Environment.Exit(0);
            return;
        }

        if (protocolLaunchHandler.TryGetJumpLaunch(args, out var placeId))
        {
            Task.Run(() => protocolLaunchHandler.HandleJumpLaunchAsync(placeId)).GetAwaiter().GetResult();
            Environment.Exit(0);
            return;
        }

        if (!NexStrap.ViewModels.Installer.InstallerViewModel.IsInstalled())
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopForInstaller)
            {
                var installerWindow = new NexStrap.Views.Installer.InstallerWindow();
                desktopForInstaller.MainWindow = installerWindow;
                desktopForInstaller.ShutdownMode = ShutdownMode.OnMainWindowClose;
                installerWindow.Show();
                installerWindow.Activate();
            }

            base.OnFrameworkInitializationCompleted();
            return;
        }

        var startupCoordinator = Services.GetRequiredService<StartupCoordinator>();
        startupCoordinator.Initialize();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = startupCoordinator.RunStartupSequenceAsync(desktop)
                .ContinueWith(t =>
                {
                    if (!t.IsFaulted) return;

                    var ex = t.Exception?.InnerException ?? t.Exception;
                    Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var msg = $"NexStrap failed to start:\n\n{ex?.Message}\n\n" +
                                  $"Check %LOCALAPPDATA%\\NexStrap\\crash.log for details.";
                        var box = new Window
                        {
                            Title = "NexStrap - Startup Error",
                            Width = 480,
                            Height = 200,
                            CanResize = false,
                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                            Content = new TextBlock
                            {
                                Text = msg,
                                Margin = new Thickness(20),
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            }
                        };
                        box.Show();
                        RobloxService.Log($"Startup failed: {ex}");
                        await Task.Delay(8000);
                        box.Close();
                        desktop.Shutdown(1);
                    });
                });
        }

        base.OnFrameworkInitializationCompleted();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private static void NativeMessageBox(string text, string caption) => MessageBox(IntPtr.Zero, text, caption, 0x10);

    private void OnTrayIconClicked(object? sender, EventArgs e) => ShowMainWindow();
    private void OnTrayShowClicked(object? sender, EventArgs e) => ShowMainWindow();
    private void OnTrayExitClicked(object? sender, EventArgs e)
    {
        IsExiting = true;
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private void ShowMainWindow()
    {
        var window = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window == null) return;

        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
        SetBackgroundMode(false);
    }

    internal void SetBackgroundMode(bool background)
    {
        _isBackground = background;
        ApplyServiceModes();
    }

    internal void SetPlayingMode(bool playing)
    {
        _isPlaying = playing;
        ApplyServiceModes();
    }

    private void ApplyServiceModes()
    {
        Services.GetRequiredService<RobloxLogWatcher>().SetBackgroundMode(_isBackground, _isPlaying);
        Services.GetRequiredService<FriendNotificationService>().SetBackgroundMode(_isBackground, _isPlaying);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddNexStrapServices();
    }
}
