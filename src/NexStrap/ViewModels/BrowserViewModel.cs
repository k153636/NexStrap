using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using NexStrap.Services;

namespace NexStrap.ViewModels;

public partial class BrowserViewModel : ViewModelBase
{
    private readonly RobloxApiService _robloxApi;

    public string? UserAvatarUrl { get; set; }

    [ObservableProperty] private string _currentSiteLabel = string.Empty;

    private static readonly Regex RobloxGamePattern =
        new(@"roblox\.com/games/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public BrowserViewModel(RobloxApiService robloxApi)
    {
        _robloxApi = robloxApi;
    }

    public async Task NavigateAsync(string url)
    {
        var match = RobloxGamePattern.Match(url);
        if (match.Success && long.TryParse(match.Groups[1].Value, out var placeId))
        {
            try
            {
                var (name, _, _) = await _robloxApi.GetGameInfoAsync(placeId);
                CurrentSiteLabel = name;
            }
            catch
            {
                CurrentSiteLabel = "roblox.com";
            }
        }
        else
        {
            CurrentSiteLabel = ExtractDomain(url);
        }
    }

    private static string ExtractDomain(string url)
    {
        try { return new Uri(url).Host.Replace("www.", ""); }
        catch { return "Unknown site"; }
    }
}
