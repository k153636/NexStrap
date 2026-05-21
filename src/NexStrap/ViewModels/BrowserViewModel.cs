using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class BrowserViewModel : ViewModelBase
{
    private readonly DiscordRpcService _discord;
    private readonly RobloxApiService _robloxApi;

    public string? UserAvatarUrl { get; set; }

    [ObservableProperty] private string _currentSiteLabel = string.Empty;

    private static readonly Regex RobloxGamePattern =
        new(@"roblox\.com/games/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public BrowserViewModel(DiscordRpcService discord, RobloxApiService robloxApi)
    {
        _discord = discord;
        _robloxApi = robloxApi;
    }

    public async Task NavigateAsync(string url)
    {
        var match = RobloxGamePattern.Match(url);
        if (match.Success && long.TryParse(match.Groups[1].Value, out var placeId))
        {
            try
            {
                var (name, iconUrl, _) = await _robloxApi.GetGameInfoAsync(placeId);
                CurrentSiteLabel = name;
                _discord.SetBrowsingGamePresence(name, iconUrl, UserAvatarUrl);
            }
            catch
            {
                CurrentSiteLabel = "roblox.com";
                _discord.SetBrowsingPresence("roblox.com", UserAvatarUrl);
            }
        }
        else
        {
            var domain = ExtractDomain(url);
            CurrentSiteLabel = domain;
            _discord.SetBrowsingPresence(domain, UserAvatarUrl);
        }
    }

    private static string ExtractDomain(string url)
    {
        try { return new Uri(url).Host.Replace("www.", ""); }
        catch { return "不明なサイト"; }
    }
}
