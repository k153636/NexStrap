using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NexStrap.Services;
using NexStrap.Views;

namespace NexStrap.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly DiscordRichPresence _discord;
    private readonly SettingsService _settings;
    private readonly PerformanceMonitorService _perfMonitor;
    private PerformanceOverlayWindow? _overlayWindow;
    private Lazy<FastFlagsViewModel> _fastFlagsVM = null!;
    private Lazy<ModsViewModel> _modsVM = null!;
    private Lazy<SettingsViewModel> _settingsVM = null!;
    private Lazy<DiscordViewModel> _discordVM = null!;
    private Lazy<StatsViewModel> _statsVM = null!;
    private Lazy<DevViewModel> _devVM = null!;
    private Lazy<AccountViewModel> _accountVM = null!;
    private Lazy<StretchResolutionViewModel> _stretchVM = null!;
    private Lazy<ShortcutsViewModel> _shortcutsVM = null!;

    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private bool _isDiscordConnected;
    [ObservableProperty] private bool _isDiscordAppIdMissing;
    [ObservableProperty] private bool _isOnSettingsPage;
    [ObservableProperty] private bool _isStartupLoading = true;
    [ObservableProperty] private string _startupStatusText = "Loading Home";

    public HomeViewModel HomeVM { get; }
    public ThemeViewModel ThemeVM { get; }
    public FastFlagsViewModel FastFlagsVM => _fastFlagsVM.Value;
    public ModsViewModel ModsVM => _modsVM.Value;
    public SettingsViewModel SettingsVM => _settingsVM.Value;
    public DiscordViewModel DiscordVM => _discordVM.Value;
    public StatsViewModel StatsVM => _statsVM.Value;
    public DevViewModel DevVM => _devVM.Value;
    public AccountViewModel AccountVM => _accountVM.Value;
    public StretchResolutionViewModel StretchVM => _stretchVM.Value;
    public ShortcutsViewModel ShortcutsVM => _shortcutsVM.Value;

    public MainWindowViewModel(
        IServiceProvider services,
        DiscordRichPresence discord,
        SettingsService settings,
        PerformanceMonitorService perfMonitor,
        HomeViewModel homeVM,
        ThemeViewModel themeVM)
    {
        _services = services;
        _discord = discord;
        _settings = settings;
        _perfMonitor = perfMonitor;

        HomeVM = homeVM;
        ThemeVM = themeVM;

        ResetLazyPages();
        _currentPage = homeVM;

        IsDiscordAppIdMissing = false;

        if (settings.Settings.DiscordRpcEnabled)
            discord.SetDiscordEnabled(true);

        discord.ConnectionChanged += (_, connected) =>
            Dispatcher.UIThread.InvokeAsync(() => IsDiscordConnected = connected);

        settings.SettingsChanged += (_, s) =>
        {
            discord.SetDiscordEnabled(s.DiscordRpcEnabled);
            UpdateOverlayVisibility(s.ShowPerformanceOverlay);
        };

        homeVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.IsRobloxRunning))
                UpdateOverlayVisibility(settings.Settings.ShowPerformanceOverlay && homeVM.IsRobloxRunning);
        };
    }

    private void ResetLazyPages()
    {
        _fastFlagsVM = CreateLazy<FastFlagsViewModel>();
        _modsVM = CreateLazy<ModsViewModel>();
        _settingsVM = CreateLazy<SettingsViewModel>();
        _discordVM = CreateLazy<DiscordViewModel>();
        _statsVM = CreateLazy<StatsViewModel>();
        _devVM = CreateLazy<DevViewModel>();
        _stretchVM = CreateLazy<StretchResolutionViewModel>();
        _shortcutsVM = CreateLazy<ShortcutsViewModel>();
        _accountVM = new Lazy<AccountViewModel>(() =>
        {
            var vm = _services.GetRequiredService<AccountViewModel>();
            vm.LaunchAsRequested += () => HomeVM.LaunchRobloxCommand.Execute(null);
            return vm;
        });
    }

    private Lazy<T> CreateLazy<T>() where T : notnull
        => new(() => _services.GetRequiredService<T>());

    public async Task BeginDeferredStartupAsync()
    {
        try
        {
            await Task.Delay(120);
            await WarmPageAsync("Loading Home", () => HomeVM);
            await WarmPageAsync("Loading Theme", () => ThemeVM);
            await WarmPageAsync("Loading Settings", () => _settingsVM.Value);
            await WarmPageAsync("Loading Fast Flags", () => _fastFlagsVM.Value);
            await WarmPageAsync("Loading Mods", () => _modsVM.Value);
            await WarmPageAsync("Loading Stretch Res", () => _stretchVM.Value);
            await WarmPageAsync("Loading Shortcuts", () => _shortcutsVM.Value);
            await WarmPageAsync("Loading Stats", () => _statsVM.Value);
            await WarmPageAsync("Loading Discord RPC", () => _discordVM.Value);
            await WarmPageAsync("Loading Account", () => _accountVM.Value);
            await WarmPageAsync("Loading Dev Tools", () => _devVM.Value);
            await Dispatcher.UIThread.InvokeAsync(() => StartupStatusText = "Ready");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsStartupLoading = false);
        }
    }

    private async Task WarmPageAsync(string status, Func<object> create)
    {
        await Dispatcher.UIThread.InvokeAsync(() => StartupStatusText = status);
        await Dispatcher.UIThread.InvokeAsync(create, DispatcherPriority.Background);
        await Task.Delay(80);
    }

    private void UpdateOverlayVisibility(bool show)
    {
        if (show)
        {
            if (_overlayWindow == null || !_overlayWindow.IsVisible)
            {
                _perfMonitor.Start();
                _overlayWindow = new PerformanceOverlayWindow(_perfMonitor);
                _overlayWindow.Show();
            }
        }
        else
        {
            _overlayWindow?.Close();
            _overlayWindow = null;
            _perfMonitor.Stop();
        }
    }

    [RelayCommand]
    private void NavigateTo(string page)
    {
        if (page == "Stats")
            StatsVM.Refresh();

        if (page == "Dev")
        {
            DevVM.Refresh();
            CurrentPage = DevVM;
            HomeVM.CurrentPageName = "Dev";
            _discord.SetDevPresence();
            return;
        }

        CurrentPage = page switch
        {
            "Home" => HomeVM,
            "FastFlags" => FastFlagsVM,
            "Mods" => ModsVM,
            "Theme" => ThemeVM,
            "Stats" => StatsVM,
            "Discord" => DiscordVM,
            "Settings" => SettingsVM,
            "Account" => AccountVM,
            "Stretch" => StretchVM,
            "Shortcuts" => ShortcutsVM,
            _ => HomeVM
        };
        IsOnSettingsPage = page == "Settings";

        HomeVM.CurrentPageName = page switch
        {
            "Discord"   => "Discord RPC",
            "Stretch"   => "Stretch Res",
            "Shortcuts" => "Shortcuts",
            _ => page
        };
        HomeVM.RefreshPresence();
    }

    [RelayCommand]
    private void Shutdown()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }

    [RelayCommand]
    private void RestartApp()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                { UseShellExecute = true });
        Shutdown();
    }
}
