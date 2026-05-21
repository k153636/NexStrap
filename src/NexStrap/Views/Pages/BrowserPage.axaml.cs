using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NexStrap.ViewModels;

namespace NexStrap.Views.Pages;

public partial class BrowserPage : UserControl
{
    public BrowserPage()
    {
        InitializeComponent();

        GoButton.Click      += OnGoClicked;
        YoutubeButton.Click += (_, _) => OpenAndTrack("https://www.youtube.com");
        NicoButton.Click    += (_, _) => OpenAndTrack("https://www.nicovideo.jp");
        TwitterButton.Click += (_, _) => OpenAndTrack("https://x.com");
        DiscordButton.Click += (_, _) => OpenAndTrack("https://discord.com/app");
        SpotifyButton.Click += (_, _) => OpenAndTrack("https://open.spotify.com");
        RobloxButton.Click  += (_, _) => OpenAndTrack("https://www.roblox.com/games");

        UrlBar.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
                OnGoClicked(null, null!);
        };
    }

    private void OnGoClicked(object? sender, RoutedEventArgs e)
    {
        var url = UrlBar.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(url)) return;
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "https://" + url;
        OpenAndTrack(url);
    }

    private void OpenAndTrack(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        if (DataContext is BrowserViewModel vm)
            _ = vm.NavigateAsync(url);
    }
}
