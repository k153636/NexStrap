using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Services;
using NexStrap.Services;

namespace NexStrap.ViewModels;

public partial class DevViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly FastFlagService _fastFlags;
    private readonly ModService _mods;
    private readonly GameHistoryService _history;
    private readonly DiscordRichPresence _discord;
    private readonly RobloxService _roblox;

    [ObservableProperty] private string _memoryUsage = string.Empty;
    [ObservableProperty] private string _discordStatus = string.Empty;
    [ObservableProperty] private int _fastFlagCount;
    [ObservableProperty] private int _modCount;
    [ObservableProperty] private int _historyCount;
    [ObservableProperty] private int _favoriteCount;
    [ObservableProperty] private string _robloxPath = string.Empty;

    public string AppVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0.0";
    public string Author => "k153636";
    public string DataDir  => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexStrap");
    public string SettingsPath => Path.Combine(DataDir, "settings.json");
    public string HistoryPath  => Path.Combine(DataDir, "history.json");
    public long   CachedUserId => _settings.Settings.CachedRobloxUserId;

    public DevViewModel(
        SettingsService settings,
        FastFlagService fastFlags,
        ModService mods,
        GameHistoryService history,
        DiscordRichPresence discord,
        RobloxService roblox)
    {
        _settings = settings;
        _fastFlags = fastFlags;
        _mods     = mods;
        _history  = history;
        _discord  = discord;
        _roblox   = roblox;
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        var proc = Process.GetCurrentProcess();
        MemoryUsage   = $"{proc.WorkingSet64 / 1024 / 1024} MB";
        DiscordStatus = _discord.IsConnected ? "Connected ✓" : "Disconnected";
        FastFlagCount = _fastFlags.GetAll().Count;
        ModCount      = _mods.Mods.Count;
        HistoryCount  = _history.Entries.Count;
        FavoriteCount = _settings.Settings.FavoriteGameIds.Count;
        RobloxPath    = _roblox.RobloxVersionPath ?? "Not found";
        OnPropertyChanged(nameof(CachedUserId));
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try { Process.Start(new ProcessStartInfo { FileName = DataDir, UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private void OpenSettingsFile()
    {
        try { Process.Start(new ProcessStartInfo { FileName = SettingsPath, UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private void OpenHistoryFile()
    {
        try { Process.Start(new ProcessStartInfo { FileName = HistoryPath, UseShellExecute = true }); }
        catch { }
    }
}
