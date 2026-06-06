using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using NexStrap.Models;

namespace NexStrap.ViewModels;

public partial class GameEntryViewModel : ViewModelBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly ConcurrentDictionary<string, Bitmap> IconCache = new();

    private static readonly Bitmap? Placeholder = LoadPlaceholder();
    private static Bitmap? LoadPlaceholder()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://NexStrap/Assets/nexstrap.png"));
            return new Bitmap(stream);
        }
        catch { return null; }
    }

    public GameHistoryEntry Entry { get; }

    public long     PlaceId         => Entry.PlaceId;
    public string   Name            => Entry.Name;
    public string?  IconUrl         => Entry.IconUrl;
    public DateTime PlayedAt        => Entry.PlayedAt;
    public int      DurationSeconds => Entry.DurationSeconds;

    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private bool _isFavorite;

    public int DisplayDurationSeconds { get; set; }

    public GameEntryViewModel(GameHistoryEntry entry)
    {
        Entry = entry;
        if (!string.IsNullOrEmpty(entry.IconUrl) && IconCache.TryGetValue(entry.IconUrl, out var cached))
            _icon = cached;
        else
        {
            _icon = Placeholder;
            if (!string.IsNullOrEmpty(entry.IconUrl))
                _ = LoadIconAsync(entry.IconUrl);
        }
    }

    private async Task LoadIconAsync(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);
            IconCache[url] = bmp;
            Icon = bmp;
        }
        catch { }
    }
}
