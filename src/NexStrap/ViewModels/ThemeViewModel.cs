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
    }

    partial void OnGlassThemeEnabledChanged(bool value)
        => _settingsService.Update(s => s.GlassThemeEnabled = value);

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
