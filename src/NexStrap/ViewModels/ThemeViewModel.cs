using Avalonia;
using Avalonia.Media;
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
    [ObservableProperty] private string _bootstrapperImagePath = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private double _backgroundBlurRadius;
    [ObservableProperty] private double _backgroundImageOpacity;
    [ObservableProperty] private string _glassAccentColor = "#FFFFFF";
    [ObservableProperty] private Color _glassAccentColorValue = Colors.White;
    [ObservableProperty] private double _glassOpacity = 1.0;
    [ObservableProperty] private double _backgroundVignetteIntensity;
    [ObservableProperty] private double _backgroundVignetteRange;
    [ObservableProperty] private string _backgroundVignetteColor = "#000000";
    [ObservableProperty] private Color _backgroundVignetteColorValue = Colors.Black;

    private bool _syncingColor;
    private bool _syncingVignetteColor;

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

    public double BackgroundVignettePercent
    {
        get => BackgroundVignetteIntensity * 100.0;
        set => BackgroundVignetteIntensity = value / 100.0;
    }

    public double BackgroundVignetteRangePercent
    {
        get => BackgroundVignetteRange * 100.0;
        set => BackgroundVignetteRange = value / 100.0;
    }

    public LinearGradientBrush VignetteTopBrush { get; private set; } = new();
    public LinearGradientBrush VignetteBottomBrush { get; private set; } = new();

    private void RebuildVignetteBrushes()
    {
        var opaque      = BackgroundVignetteColorValue;
        var transparent = Color.FromArgb(0, opaque.R, opaque.G, opaque.B);
        var range       = BackgroundVignetteRange;

        VignetteTopBrush = new LinearGradientBrush
        {
            StartPoint    = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint      = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(opaque, 0), new GradientStop(transparent, range)]
        };
        VignetteBottomBrush = new LinearGradientBrush
        {
            StartPoint    = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            EndPoint      = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            GradientStops = [new GradientStop(opaque, 0), new GradientStop(transparent, range)]
        };

        OnPropertyChanged(nameof(VignetteTopBrush));
        OnPropertyChanged(nameof(VignetteBottomBrush));
    }

    public double GlassOpacityPercent
    {
        get => GlassOpacity / 0.75 * 100.0;
        set => GlassOpacity = value / 100.0 * 0.75;
    }

    public bool HasBackgroundImage    => !string.IsNullOrEmpty(BackgroundImagePath);
    public bool HasBootstrapperImage  => !string.IsNullOrEmpty(BootstrapperImagePath);


    public ThemeViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _glassThemeEnabled       = settingsService.Settings.GlassThemeEnabled;
        _backgroundImagePath     = settingsService.Settings.BackgroundImagePath;
        _bootstrapperImagePath   = settingsService.Settings.BootstrapperImagePath;
        _backgroundBlurRadius    = settingsService.Settings.BackgroundBlurRadius;
        _backgroundImageOpacity  = settingsService.Settings.BackgroundImageOpacity;
        _glassAccentColor             = settingsService.Settings.GlassAccentColor;
        _glassOpacity                 = Math.Clamp(settingsService.Settings.GlassOpacity, 0.0, 0.75);
        _backgroundVignetteIntensity  = settingsService.Settings.BackgroundVignetteIntensity;
        _backgroundVignetteRange      = settingsService.Settings.BackgroundVignetteRange;
        _backgroundVignetteColor      = settingsService.Settings.BackgroundVignetteColor;
        try { _glassAccentColorValue        = Color.Parse(_glassAccentColor); }       catch { }
        try { _backgroundVignetteColorValue = Color.Parse(_backgroundVignetteColor); } catch { }
        RebuildVignetteBrushes();

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

        if (!_syncingColor)
        {
            _syncingColor = true;
            try { GlassAccentColorValue = Color.Parse(value); } catch { }
            _syncingColor = false;
        }
    }

    partial void OnGlassAccentColorValueChanged(Color value)
    {
        if (!_syncingColor)
        {
            _syncingColor = true;
            GlassAccentColor = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            _syncingColor = false;
        }
    }

    [RelayCommand]
    private void SetAccentColor(string color) => GlassAccentColor = color;

    partial void OnBackgroundImagePathChanged(string value)
    {
        _settingsService.Update(s => s.BackgroundImagePath = value);
        OnPropertyChanged(nameof(HasBackgroundImage));
    }

    partial void OnBootstrapperImagePathChanged(string value)
    {
        _settingsService.Update(s => s.BootstrapperImagePath = value);
        OnPropertyChanged(nameof(HasBootstrapperImage));
    }

    partial void OnBackgroundBlurRadiusChanged(double value)
        => _settingsService.Update(s => s.BackgroundBlurRadius = value);

    partial void OnBackgroundImageOpacityChanged(double value)
    {
        _settingsService.Update(s => s.BackgroundImageOpacity = value);
        OnPropertyChanged(nameof(BackgroundImageOpacityPercent));
    }

    partial void OnGlassOpacityChanged(double value)
    {
        _settingsService.Update(s => s.GlassOpacity = value);
        OnPropertyChanged(nameof(GlassOpacityPercent));
    }

    partial void OnBackgroundVignetteIntensityChanged(double value)
    {
        _settingsService.Update(s => s.BackgroundVignetteIntensity = value);
        OnPropertyChanged(nameof(BackgroundVignettePercent));
    }

    partial void OnBackgroundVignetteRangeChanged(double value)
    {
        _settingsService.Update(s => s.BackgroundVignetteRange = value);
        OnPropertyChanged(nameof(BackgroundVignetteRangePercent));
        RebuildVignetteBrushes();
    }

    partial void OnBackgroundVignetteColorChanged(string value)
    {
        _settingsService.Update(s => s.BackgroundVignetteColor = value);
        if (!_syncingVignetteColor)
        {
            _syncingVignetteColor = true;
            try { BackgroundVignetteColorValue = Color.Parse(value); } catch { }
            _syncingVignetteColor = false;
        }
    }

    partial void OnBackgroundVignetteColorValueChanged(Color value)
    {
        RebuildVignetteBrushes();
        if (!_syncingVignetteColor)
        {
            _syncingVignetteColor = true;
            BackgroundVignetteColor = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            _syncingVignetteColor = false;
        }
    }

    public async Task PickBackgroundImageAsync(IStorageProvider storageProvider)
    {
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Background Image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image Files")
                {
                    Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file == null) return;

        BackgroundImagePath = file.Path.LocalPath;
        StatusMessage = "Background image set";
    }

    [RelayCommand]
    private void ClearBackgroundImage()
    {
        BackgroundImagePath = string.Empty;
        StatusMessage = "Background image removed";
    }

    public async Task PickBootstrapperImageAsync(IStorageProvider storageProvider)
    {
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Loading Screen Image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image Files")
                {
                    Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file == null) return;
        BootstrapperImagePath = file.Path.LocalPath;
    }

    [RelayCommand]
    private void ClearBootstrapperImage()
    {
        BootstrapperImagePath = string.Empty;
    }

}
