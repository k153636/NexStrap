using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using NexStrap.Core.Models;

namespace NexStrap.ViewModels;

public partial class GameEntryViewModel : ViewModelBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

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
        if (!string.IsNullOrEmpty(entry.IconUrl))
            _ = LoadIconAsync(entry.IconUrl);
    }

    private async Task LoadIconAsync(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            Icon = new Bitmap(ms);
        }
        catch { }
    }
}
