using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NexStrap.Services;
using NexStrap.ViewModels;
using NexStrap.Views;

namespace NexStrap.Modules.Infrastructure.Startup;

public sealed class StartupCoordinator(
    IServiceProvider services,
    RobloxService roblox,
    StudioService studio,
    SettingsService settings,
    UpdateService updateService,
    FriendNotificationService friendNotifications,
    GlobalHotKeyService hotKeys)
{
    private BootstrapperWindow? _robloxBootstrapperWindow;
    private BootstrapperViewModel? _robloxBootstrapperViewModel;
    private BootstrapperWindow? _studioBootstrapperWindow;
    private BootstrapperViewModel? _studioBootstrapperViewModel;
    private LaunchWindow? _launchWindow;
    private LaunchWindowViewModel? _launchWindowViewModel;
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _mainViewModel;
    private bool _initialized;

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        friendNotifications.FriendCameOnline += (_, e) =>
            NotificationService.ShowFriendOnline(e.DisplayName);

        RegisterRobloxBootstrapperWindow();
        RegisterStudioBootstrapperWindow();
    }

    public async Task RunStartupSequenceAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var justUpdated = Environment.GetCommandLineArgs().Contains("--updated");
        var update = justUpdated ? null : await updateService.CheckForUpdateAsync();
        if (update != null)
        {
            await ShowUpdateBootstrapperAndApplyAsync(update.Value.DownloadUrl);
            return;
        }

        await ShowLaunchWindowAsync(desktop);

        _ = RunDeferredStartupChecksAsync();

        desktop.Exit += (_, _) => Logger.Instance.Dispose();
    }

    private async Task ShowUpdateBootstrapperAndApplyAsync(string downloadUrl)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var vm = new BootstrapperViewModel(roblox, settings);
            var win = new BootstrapperWindow(vm);
            win.Show();
        });

        await updateService.DownloadAndApplyAsync(
            downloadUrl,
            p => roblox.BroadcastProgress(p));
    }

    private async Task RunDeferredStartupChecksAsync()
    {
        try
        {
            if (!roblox.NeedsSetup()) return;

            BootstrapperWindow? win = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var vm = new BootstrapperViewModel(roblox, settings);
                win = new BootstrapperWindow(vm);
                win.Show();
            });

            await roblox.RunSetupAsync();
            await Dispatcher.UIThread.InvokeAsync(() => win?.Close());
        }
        catch (Exception ex)
        {
            RobloxService.Log($"Deferred startup checks failed: {ex.Message}");
        }
    }

    private async Task ShowLaunchWindowAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _launchWindowViewModel = services.GetRequiredService<LaunchWindowViewModel>();
            _launchWindowViewModel.OpenMainWindowRequested += initialPage =>
                Dispatcher.UIThread.InvokeAsync(() => OpenMainWindow(desktop, initialPage));

            _launchWindow = new LaunchWindow
            {
                DataContext = _launchWindowViewModel
            };

            desktop.MainWindow = _launchWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            _launchWindow.Show();
            _launchWindow.Activate();
        });

        RobloxService.Log("Launch window shown");
    }

    private void OpenMainWindow(IClassicDesktopStyleApplicationLifetime desktop, string? initialPage)
    {
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            if (!string.IsNullOrWhiteSpace(initialPage))
                _mainViewModel?.NavigateToCommand.Execute(initialPage);
            return;
        }

        _mainViewModel = services.GetRequiredService<MainWindowViewModel>();
        _mainWindow = new MainWindow
        {
            DataContext = _mainViewModel
        };

        desktop.MainWindow = _mainWindow;
        _mainWindow.Show();
        _mainWindow.Activate();
        hotKeys.Install();

        if (_mainViewModel != null)
        {
            if (!string.IsNullOrWhiteSpace(initialPage))
                _mainViewModel.NavigateToCommand.Execute(initialPage);
            _ = _mainViewModel.BeginDeferredStartupAsync();
        }

        _launchWindowViewModel?.SetTemporaryDetails(null);
        _launchWindow?.Hide();

        RobloxService.Log("Main window shown");
    }

    private void RegisterRobloxBootstrapperWindow()
    {
        roblox.StatusChanged += (_, status) =>
        {
            void HandleStatus()
            {
                if (status is RobloxStatus.Updating or RobloxStatus.Launching && _robloxBootstrapperWindow == null)
                {
                    _robloxBootstrapperViewModel = new BootstrapperViewModel(roblox, settings);
                    _robloxBootstrapperWindow = new BootstrapperWindow(_robloxBootstrapperViewModel);
                    _robloxBootstrapperWindow.Closed += (_, _) =>
                    {
                        _robloxBootstrapperWindow = null;
                        _robloxBootstrapperViewModel = null;
                    };
                    _robloxBootstrapperWindow.Show();
                }
                else if (status is RobloxStatus.Running or RobloxStatus.Idle or RobloxStatus.NotInstalled)
                {
                    _robloxBootstrapperWindow?.Close();
                }
            }

            if (Dispatcher.UIThread.CheckAccess())
                HandleStatus();
            else
                Dispatcher.UIThread.InvokeAsync(HandleStatus);
        };
    }

    private void RegisterStudioBootstrapperWindow()
    {
        studio.StatusChanged += (_, status) =>
        {
            void HandleStudioStatus()
            {
                if (status is RobloxStatus.Updating or RobloxStatus.Launching && _studioBootstrapperWindow == null)
                {
                    _studioBootstrapperViewModel = new BootstrapperViewModel(studio, settings);
                    _studioBootstrapperWindow = new BootstrapperWindow(_studioBootstrapperViewModel);
                    _studioBootstrapperWindow.Closed += (_, _) =>
                    {
                        _studioBootstrapperWindow = null;
                        _studioBootstrapperViewModel = null;
                    };
                    _studioBootstrapperWindow.Show();
                }
                else if (status is RobloxStatus.Running or RobloxStatus.Idle)
                {
                    _studioBootstrapperWindow?.Close();
                }
            }

            if (Dispatcher.UIThread.CheckAccess())
                HandleStudioStatus();
            else
                Dispatcher.UIThread.InvokeAsync(HandleStudioStatus);
        };
    }
}
