using CommunityToolkit.Mvvm.ComponentModel;
using NexStrap.Services;
using NexStrap.Services;

namespace NexStrap.ViewModels;

public partial class DiscordViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly DiscordRichPresence _discord;
    private readonly RobloxApiService _robloxApi;
    private readonly AccountService _accountService;

    [ObservableProperty] private bool _discordRpcEnabled;
    [ObservableProperty] private bool _discordAppIdConfigured;
    [ObservableProperty] private bool _showRobloxUsername;
    [ObservableProperty] private bool _useDisplayNameFormat;
    [ObservableProperty] private bool _showCreator;
    [ObservableProperty] private bool _showJoinButton;
    [ObservableProperty] private bool _showLauncherPresence;
    [ObservableProperty] private bool _showLauncherDetails;
    [ObservableProperty] private bool _showServerRegion;
    [ObservableProperty] private bool _showFlagCount;
    [ObservableProperty] private bool _placeNameEnglish;

    public DiscordViewModel(SettingsService settingsService, DiscordRichPresence discord, RobloxApiService robloxApi, AccountService accountService)
    {
        _settingsService = settingsService;
        _discord = discord;
        _robloxApi = robloxApi;
        _accountService = accountService;

        var s = settingsService.Settings;
        _discordRpcEnabled      = s.DiscordRpcEnabled;
        _discordAppIdConfigured = true;
        _showRobloxUsername     = s.DiscordShowRobloxUsername;
        _useDisplayNameFormat   = s.DiscordUseDisplayNameFormat;
        _showCreator            = s.DiscordShowCreator;
        _showJoinButton         = s.DiscordShowJoinButton;
        _showLauncherPresence   = s.DiscordShowLauncherPresence;
        _showLauncherDetails    = s.DiscordShowLauncherDetails;
        _showServerRegion       = s.DiscordShowServerRegion;
        _showFlagCount          = s.DiscordShowFlagCount;
        _placeNameEnglish       = s.DiscordPlaceNameEnglish;
    }

    partial void OnDiscordRpcEnabledChanged(bool value)
    {
        _settingsService.Update(s => s.DiscordRpcEnabled = value);
        // SetDiscordEnabled は MainWindowViewModel の SettingsChanged で処理される
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

    partial void OnShowServerRegionChanged(bool value)
        => _settingsService.Update(s => s.DiscordShowServerRegion = value);

    partial void OnShowFlagCountChanged(bool value)
        => _settingsService.Update(s => s.DiscordShowFlagCount = value);

    partial void OnPlaceNameEnglishChanged(bool value)
        => _settingsService.Update(s => s.DiscordPlaceNameEnglish = value);

    private async Task ApplyUserLabelAsync()
    {
        if (!ShowRobloxUsername)
        {
            _discord.SetUserLabel(null);
            return;
        }

        // アクティブアカウント → キャッシュIDの順で参照
        var activeAccount = _accountService.Accounts.FirstOrDefault(a => a.IsActive)
                         ?? _accountService.Accounts.FirstOrDefault();
        var userId = activeAccount?.UserId > 0
            ? activeAccount.UserId
            : _settingsService.Settings.CachedRobloxUserId;
        if (userId <= 0) return;

        var info = await _robloxApi.GetUserInfoAsync(userId);
        if (info is not { } u) return;

        var label = UseDisplayNameFormat
            ? $"{u.displayName} (@{u.username})"
            : $"@{u.username}";
        _discord.SetUserLabel(label);
    }
}
