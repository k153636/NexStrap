using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Models;
using NexStrap.Services;

namespace NexStrap.ViewModels;

public partial class LaunchWindowViewModel : ViewModelBase
{
    private readonly RobloxService _roblox;
    private readonly FastFlagService _fastFlags;
    private readonly ModService _mods;
    private readonly StudioService _studio;
    private readonly StudioFastFlagService _studioFastFlags;
    private readonly DiscordRichPresence _discord;
    private readonly SettingsService _settings;
    private readonly AccountService _accounts;
    private readonly RobloxApiService _robloxApi;

    [ObservableProperty] private string _statusText = "Choose how to start";
    [ObservableProperty] private bool _isLaunchingRoblox;
    [ObservableProperty] private bool _isLaunchingStudio;

    public string VersionText
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version;
            return v != null ? $"Ver. {v.Major}.{v.Minor}.{v.Build}" : "Ver. 1.1";
        }
    }

    public event Action<string?>? OpenMainWindowRequested;

    public LaunchWindowViewModel(
        RobloxService roblox,
        FastFlagService fastFlags,
        ModService mods,
        StudioService studio,
        StudioFastFlagService studioFastFlags,
        DiscordRichPresence discord,
        SettingsService settings,
        AccountService accounts,
        RobloxApiService robloxApi)
    {
        _roblox = roblox;
        _fastFlags = fastFlags;
        _mods = mods;
        _studio = studio;
        _studioFastFlags = studioFastFlags;
        _discord = discord;
        _settings = settings;
        _accounts = accounts;
        _robloxApi = robloxApi;

        StartLauncherConnections();
        _discord.SetTemporaryDetails("Launcher");
    }

    public void SetTemporaryDetails(string? details) => _discord.SetTemporaryDetails(details);

    [RelayCommand]
    private void LaunchApp() => OpenMainWindowRequested?.Invoke(null);

    [RelayCommand]
    private void OpenAbout() => OpenUrl("https://github.com/k153636/NexStrap#readme");

    [RelayCommand]
    private void OpenGitHub() => OpenUrl("https://github.com/k153636/NexStrap");

    [RelayCommand]
    private void OpenDiscord() => OpenUrl("https://discord.gg/PPrKt97jRn");

    [RelayCommand]
    private void OpenSettings() => OpenMainWindowRequested?.Invoke("Settings");

    [RelayCommand]
    private async Task LaunchStudioAsync()
    {
        if (IsLaunchingStudio) return;

        IsLaunchingStudio = true;
        StartLauncherConnections();
        _discord.SetTemporaryDetails("Preparing Studio");
        StatusText = "Preparing Studio...";
        try
        {
            if (await StudioPluginInstaller.IsUpdateAvailableAsync())
                await StudioPluginInstaller.DownloadAndInstallAsync();

            await _studioFastFlags.SaveAsync();

            var launched = await _studio.LaunchAsync();
            StatusText = launched ? "Studio launched" : "Studio launch failed";
        }
        catch (Exception ex)
        {
            StatusText = $"Studio launch failed: {ex.Message}";
        }
        finally
        {
            IsLaunchingStudio = false;
        }
    }

    [RelayCommand]
    private async Task LaunchRobloxAsync()
    {
        if (IsLaunchingRoblox) return;

        IsLaunchingRoblox = true;
        StartLauncherConnections();
        _discord.SetTemporaryDetails("Preparing Roblox");
        StatusText = "Preparing Roblox...";

        try
        {
            var s = _settings.Settings;

            _fastFlags.ApplyPerformanceSettings(s);
            await _fastFlags.SaveAsync();
            await _mods.ApplyEnabledModsAsync();

            string? launchArgs = null;
            var cookie = _accounts.GetActiveCookie();
            if (cookie != null)
            {
                var ticket = await _robloxApi.GetAuthTicketAsync(cookie);
                if (ticket != null)
                    launchArgs = $"--launchMode app --authenticationTicket {ticket} --authenticationUrl https://auth.roblox.com";
            }

            var opts = new LaunchOptions(
                MultiInstance: s.MultiInstanceEnabled,
                SuppressCrashHandler: s.SuppressCrashHandler,
                CpuCoreLimit: s.CpuAffinityEnabled ? s.CpuCoreLimit : 0,
                MemoryOptimization: s.MemoryOptimizationEnabled,
                CleanupOldVersions: s.CleanupOldVersions,
                CookieToInject: cookie,
                StretchResolution: s.StretchResolutionEnabled,
                StretchWidth: s.StretchResolutionWidth,
                StretchHeight: s.StretchResolutionHeight);

            StatusText = "Launching Roblox...";
            var launched = await _roblox.LaunchAsync(launchArgs, autoUpdate: s.AutoUpdateRoblox, options: opts);
            StatusText = launched ? "Roblox launched" : "Launch failed";
        }
        catch (Exception ex)
        {
            StatusText = $"Launch failed: {ex.Message}";
        }
        finally
        {
            IsLaunchingRoblox = false;
        }
    }

    private void StartLauncherConnections()
    {
        if (_settings.Settings.DiscordRpcEnabled)
            _discord.SetDiscordEnabled(true);
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }
}
