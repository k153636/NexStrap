using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Models;
using NexStrap.Services;

namespace NexStrap.ViewModels;

public partial class ShortcutsViewModel : ViewModelBase
{
    private readonly SettingsService     _settings;
    private readonly GlobalHotKeyService _hotKey;
    private readonly RobloxService       _roblox;

    [ObservableProperty] private string _stretchHotKeyDisplay = "Not set";
    [ObservableProperty] private bool   _isRecording;

    public ShortcutsViewModel(SettingsService settings, GlobalHotKeyService hotKey, RobloxService roblox)
    {
        _settings = settings;
        _hotKey   = hotKey;
        _roblox   = roblox;

        var saved = HotKeyBinding.Parse(settings.Settings.StretchHotKey);
        StretchHotKeyDisplay = saved.IsEmpty ? "Not set" : saved.ToString();
        RegisterStretch(saved);
    }

    [RelayCommand]
    private void StartRecording() => IsRecording = true;

    [RelayCommand]
    private void ClearHotKey()
    {
        _hotKey.Unregister("StretchToggle");
        _settings.Update(s => s.StretchHotKey = "");
        StretchHotKeyDisplay = "Not set";
        IsRecording = false;
    }

    // Called from ShortcutsPage.axaml.cs when a key is pressed during recording
    public void CommitRecording(KeyModifiers mods, Key key)
    {
        IsRecording = false;
        var binding = HotKeyBinding.FromAvaloniaKey(mods, key);
        if (binding.IsEmpty) return;

        RegisterStretch(binding);
        _settings.Update(s => s.StretchHotKey = binding.ToString());
        StretchHotKeyDisplay = binding.ToString();
    }

    private void RegisterStretch(HotKeyBinding binding)
    {
        if (binding.IsEmpty) return;
        _hotKey.Register("StretchToggle", binding, () =>
        {
            if (_roblox.IsStretchActive)
                _roblox.RestoreResolution();
            else
            {
                var s = _settings.Settings;
                _roblox.ApplyStretchResolution(s.StretchResolutionWidth, s.StretchResolutionHeight);
            }
        });
    }
}
