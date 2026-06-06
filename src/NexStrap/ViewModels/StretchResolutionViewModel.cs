using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Services;

namespace NexStrap.ViewModels;

public partial class StretchResolutionViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly RobloxService   _roblox;

    public IReadOnlyList<ResolutionPreset> Presets { get; } =
    [
        new("1280 × 960",  1280,  960, "Most popular stretch — Valorant / CS2"),
        new("1440 × 1080", 1440, 1080, "High-res 4:3 — Valorant / CS2"),
        new("1024 × 768",  1024,  768, "Classic FPS — Max visibility boost"),
        new("800 × 600",    800,  600, "Ultra low-res — Maximum FPS"),
    ];

    [ObservableProperty] private bool   _applyOnLaunch;
    [ObservableProperty] private bool   _warningVisible;
    [ObservableProperty] private bool   _isActive;
    [ObservableProperty] private string _statusText = "Inactive";
    [ObservableProperty] private ResolutionPreset? _selectedPreset;

    public StretchResolutionViewModel(SettingsService settings, RobloxService roblox)
    {
        _settings = settings;
        _roblox   = roblox;

        var s = settings.Settings;
        _applyOnLaunch  = s.StretchResolutionEnabled;
        _warningVisible = !s.StretchWarningDismissed;

        _selectedPreset = Presets.FirstOrDefault(
            p => p.Width == s.StretchResolutionWidth && p.Height == s.StretchResolutionHeight)
            ?? Presets[0];
    }

    partial void OnApplyOnLaunchChanged(bool value)
        => _settings.Update(s =>
        {
            s.StretchResolutionEnabled = value;
            s.StretchResolutionWidth   = SelectedPreset?.Width  ?? Presets[0].Width;
            s.StretchResolutionHeight  = SelectedPreset?.Height ?? Presets[0].Height;
        });

    [RelayCommand]
    private void DismissWarning()
    {
        WarningVisible = false;
        _settings.Update(s => s.StretchWarningDismissed = true);
    }

    [RelayCommand]
    private void SelectPreset(ResolutionPreset preset)
    {
        SelectedPreset = preset;
        _settings.Update(s =>
        {
            s.StretchResolutionWidth  = preset.Width;
            s.StretchResolutionHeight = preset.Height;
        });
    }

    [RelayCommand]
    private void ApplyNow()
    {
        var preset = SelectedPreset ?? Presets[0];
        var ok = _roblox.ApplyStretchResolution(preset.Width, preset.Height);
        IsActive   = ok;
        StatusText = ok ? $"Active — {preset.Width}×{preset.Height}" : "Failed to apply";
    }

    [RelayCommand]
    private void Restore()
    {
        _roblox.RestoreResolution();
        IsActive   = false;
        StatusText = "Inactive";
    }
}

public record ResolutionPreset(string Label, int Width, int Height, string Description);
