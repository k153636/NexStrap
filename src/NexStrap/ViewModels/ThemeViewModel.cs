using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class ThemeViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;

    [ObservableProperty] private bool _glassThemeEnabled;
    [ObservableProperty] private string _backgroundImagePath = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private double _backgroundBlurRadius;
    [ObservableProperty] private double _backgroundImageOpacity;
    [ObservableProperty] private string _glassAccentColor = "#FFFFFF";

    public static IReadOnlyList<string> AccentColors { get; } =
    [
        "#FFFFFF", "#6EA8FF", "#B470FF", "#FF70A6",
        "#4ADE80", "#FFAA44", "#44DFFF", "#FF5555"
    ];

    public bool IsAccentWhite  => GlassAccentColor == "#FFFFFF";
    public bool IsAccentBlue   => GlassAccentColor == "#6EA8FF";
    public bool IsAccentPurple => GlassAccentColor == "#B470FF";
    public bool IsAccentPink   => GlassAccentColor == "#FF70A6";
    public bool IsAccentGreen  => GlassAccentColor == "#4ADE80";
    public bool IsAccentOrange => GlassAccentColor == "#FFAA44";
    public bool IsAccentCyan   => GlassAccentColor == "#44DFFF";
    public bool IsAccentRed    => GlassAccentColor == "#FF5555";

    public double BackgroundImageOpacityPercent
    {
        get => BackgroundImageOpacity * 100.0;
        set => BackgroundImageOpacity = value / 100.0;
    }

    public ThemeViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _glassThemeEnabled = settingsService.Settings.GlassThemeEnabled;
        _backgroundImagePath = settingsService.Settings.BackgroundImagePath;
        _backgroundBlurRadius = settingsService.Settings.BackgroundBlurRadius;
        _backgroundImageOpacity = settingsService.Settings.BackgroundImageOpacity;
        _glassAccentColor = settingsService.Settings.GlassAccentColor;
    }

    partial void OnGlassThemeEnabledChanged(bool value)
        => _settingsService.Update(s => s.GlassThemeEnabled = value);

    partial void OnGlassAccentColorChanged(string value)
    {
        _settingsService.Update(s => s.GlassAccentColor = value);
        OnPropertyChanged(nameof(IsAccentWhite));
        OnPropertyChanged(nameof(IsAccentBlue));
        OnPropertyChanged(nameof(IsAccentPurple));
        OnPropertyChanged(nameof(IsAccentPink));
        OnPropertyChanged(nameof(IsAccentGreen));
        OnPropertyChanged(nameof(IsAccentOrange));
        OnPropertyChanged(nameof(IsAccentCyan));
        OnPropertyChanged(nameof(IsAccentRed));
    }

    [RelayCommand]
    private void SetAccentColor(string color) => GlassAccentColor = color;

    partial void OnBackgroundImagePathChanged(string value)
        => _settingsService.Update(s => s.BackgroundImagePath = value);

    partial void OnBackgroundBlurRadiusChanged(double value)
        => _settingsService.Update(s => s.BackgroundBlurRadius = value);

    partial void OnBackgroundImageOpacityChanged(double value)
    {
        _settingsService.Update(s => s.BackgroundImageOpacity = value);
        OnPropertyChanged(nameof(BackgroundImageOpacityPercent));
    }

    public async Task PickBackgroundImageAsync(IStorageProvider storageProvider)
    {
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "背景画像を選択",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("画像ファイル")
                {
                    Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file == null) return;

        BackgroundImagePath = file.Path.LocalPath;
        StatusMessage = "背景画像を設定しました";
    }

    [RelayCommand]
    private void ClearBackgroundImage()
    {
        BackgroundImagePath = string.Empty;
        StatusMessage = "背景画像を削除しました";
    }
}
