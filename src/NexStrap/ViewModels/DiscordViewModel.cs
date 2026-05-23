using CommunityToolkit.Mvvm.ComponentModel;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class DiscordViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly DiscordRpcService _discord;
    private readonly RobloxApiService _robloxApi;

    [ObservableProperty] private bool _discordRpcEnabled;
    [ObservableProperty] private bool _discordAppIdConfigured;
    [ObservableProperty] private bool _showRobloxUsername;
    [ObservableProperty] private bool _useDisplayNameFormat;
    [ObservableProperty] private bool _showCreator;
    [ObservableProperty] private bool _showJoinButton;
    [ObservableProperty] private bool _showLauncherPresence;
    [ObservableProperty] private bool _showLauncherDetails;

    public DiscordViewModel(SettingsService settingsService, DiscordRpcService discord, RobloxApiService robloxApi)
    {
        _settingsService = settingsService;
        _discord = discord;
        _robloxApi = robloxApi;

        var s = settingsService.Settings;
        _discordRpcEnabled      = s.DiscordRpcEnabled;
        _discordAppIdConfigured = true;
        _showRobloxUsername     = s.DiscordShowRobloxUsername;
        _useDisplayNameFormat   = s.DiscordUseDisplayNameFormat;
        _showCreator            = s.DiscordShowCreator;
        _showJoinButton         = s.DiscordShowJoinButton;
        _showLauncherPresence   = s.DiscordShowLauncherPresence;
        _showLauncherDetails    = s.DiscordShowLauncherDetails;
    }

    partial void OnDiscordRpcEnabledChanged(bool value)
    {
        _settingsService.Update(s => s.DiscordRpcEnabled = value);
        if (value)
            _discord.Initialize(AppConstants.DiscordAppId);
        else
            _discord.ClearPresence();
    }

    partial void OnShowRobloxUsernameChanged(bool value)
    {
        _settingsService.Update(s => s.DiscordShowRobloxUsername = value);
        _ = ApplyUserLabelAsync();
    }

    partial void OnUseDisplayNameFormatChanged(bool value)
    {
        _settingsService.Update(s => s.DiscordUseDisplayNameFormat = value);
        _ = ApplyUserLabelAsync();
    }

    partial void OnShowCreatorChanged(bool value)
        => _settingsService.Update(s => s.DiscordShowCreator = value);

    partial void OnShowJoinButtonChanged(bool value)
        => _settingsService.Update(s => s.DiscordShowJoinButton = value);

    partial void OnShowLauncherPresenceChanged(bool value)
        => _settingsService.Update(s => s.DiscordShowLauncherPresence = value);

    partial void OnShowLauncherDetailsChanged(bool value)
        => _settingsService.Update(s => s.DiscordShowLauncherDetails = value);

    private async Task ApplyUserLabelAsync()
    {
        if (!ShowRobloxUsername)
        {
            _discord.SetUserLabel(null);
            return;
        }

        var userId = _settingsService.Settings.CachedRobloxUserId;
        if (userId <= 0) return;

        var info = await _robloxApi.GetUserInfoAsync(userId);
        if (info is not { } u) return;

        var label = UseDisplayNameFormat
            ? $"{u.displayName} (@{u.username})"
            : $"@{u.username}";
        _discord.SetUserLabel(label);
    }
}
