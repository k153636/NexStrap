using CommunityToolkit.Mvvm.ComponentModel;
using NexStrap.Models;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace NexStrap.ViewModels;

public sealed partial class DiscordPartyPresetItemViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _enabled;
    private string _label = "New Preset";
    private string _placeIdText = string.Empty;
    private long _placeId;
    private int _currentSize;
    private int _maxSize;

    public DiscordPartyPresetItemViewModel(DiscordPartyPreset preset, Action changed)
    {
        _changed = changed;
        _enabled = preset.Enabled;
        _label = string.IsNullOrWhiteSpace(preset.Label) ? "New Preset" : preset.Label;
        _placeId = Math.Max(0, preset.PlaceId);
        _placeIdText = _placeId > 0 ? _placeId.ToString() : string.Empty;
        _currentSize = Math.Max(1, preset.CurrentSize);
        _maxSize = Math.Max(_currentSize, preset.MaxSize);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetTracked(ref _enabled, value);
    }

    public string Label
    {
        get => _label;
        set => SetTracked(ref _label, string.IsNullOrWhiteSpace(value) ? "New Preset" : value.Trim());
    }

    public string PlaceIdText
    {
        get => _placeIdText;
        set
        {
            var text = value?.Trim() ?? string.Empty;
            if (!SetProperty(ref _placeIdText, text)) return;

            _placeId = ParsePlaceId(text);
            _changed();
        }
    }

    public int CurrentSize
    {
        get => _currentSize;
        set
        {
            value = Math.Max(1, value);
            if (!SetProperty(ref _currentSize, value)) return;

            if (_maxSize < _currentSize)
            {
                _maxSize = _currentSize;
                OnPropertyChanged(nameof(MaxSize));
            }

            _changed();
        }
    }

    public int MaxSize
    {
        get => _maxSize;
        set
        {
            value = Math.Max(1, value);
            if (value < _currentSize) value = _currentSize;
            SetTracked(ref _maxSize, value);
        }
    }

    public DiscordPartyPreset ToModel() => new()
    {
        Enabled = Enabled,
        Label = Label,
        PlaceId = _placeId,
        CurrentSize = CurrentSize,
        MaxSize = MaxSize
    };

    private void SetTracked<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName)) return;
        _changed();
    }

    private static long ParsePlaceId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0;

        if (long.TryParse(input, out var direct) && direct > 0)
            return direct;

        var gameMatch = GameUrlRegex().Match(input);
        if (gameMatch.Success && long.TryParse(gameMatch.Groups[1].Value, out var gameId) && gameId > 0)
            return gameId;

        var placeMatch = PlaceIdQueryRegex().Match(input);
        if (placeMatch.Success && long.TryParse(placeMatch.Groups[1].Value, out var placeId) && placeId > 0)
            return placeId;

        return 0;
    }

    [GeneratedRegex(@"(?:^|/)games/(\d+)(?:/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex GameUrlRegex();

    [GeneratedRegex(@"(?:\?|&)(?:placeid|placeId)=(\d+)(?:&|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceIdQueryRegex();
}
