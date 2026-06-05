using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class StretchResolutionViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly RobloxService   _roblox;

    // ── プリセット ──────────────────────────────────────────────────────────
    public IReadOnlyList<ResolutionPreset> Presets { get; } =
    [
        new("1280 × 960",  1280,  960, "Most popular stretch — Valorant / CS2"),
        new("1440 × 1080", 1440, 1080, "High-res 4:3 — Valorant / CS2"),
        new("1024 × 768",  1024,  768, "Classic FPS — Max visibility boost"),
        new("800 × 600",    800,  600, "Ultra low-res — Maximum FPS"),
    ];

    [ObservableProperty] private bool   _applyOnLaunch;
    [ObservableProperty] private bool   _warningVisible;
    [ObservableProperty] private int    _customWidth;
    [ObservableProperty] private int    _customHeight;
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
        _customWidth   = s.StretchResolutionWidth;
        _customHeight  = s.StretchResolutionHeight;

        // 保存済み解像度に一致するプリセットを選択状態にする
        _selectedPreset = Presets.FirstOrDefault(
            p => p.Width == _customWidth && p.Height == _customHeight);
    }

    partial void OnApplyOnLaunchChanged(bool value)
        => _settings.Update(s =>
        {
            s.StretchResolutionEnabled = value;
            s.StretchResolutionWidth   = CustomWidth;
            s.StretchResolutionHeight  = CustomHeight;
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
        SelectedPreset  = preset;
        CustomWidth     = preset.Width;
        CustomHeight    = preset.Height;
        _settings.Update(s =>
        {
            s.StretchResolutionWidth  = preset.Width;
            s.StretchResolutionHeight = preset.Height;
        });
    }

    [RelayCommand]
    private void ApplyNow()
    {
        var ok = _roblox.ApplyStretchResolution(CustomWidth, CustomHeight);
        IsActive   = ok;
        StatusText = ok ? $"Active — {CustomWidth}×{CustomHeight}" : "Failed to apply";
    }

    [RelayCommand]
    private void Restore()
    {
        _roblox.RestoreResolution();
        IsActive   = false;
        StatusText = "Inactive";
    }

    [RelayCommand]
    private void SaveCustom()
    {
        SelectedPreset = null;
        _settings.Update(s =>
        {
            s.StretchResolutionWidth  = CustomWidth;
            s.StretchResolutionHeight = CustomHeight;
        });
        StatusText = $"Custom saved: {CustomWidth}×{CustomHeight}";
    }
}

public record ResolutionPreset(string Label, int Width, int Height, string Description);
