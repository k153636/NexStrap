using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using NexStrap.Models;
using NexStrap.Services;

namespace NexStrap.ViewModels;

public partial class DiscordPartyPresetsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly DiscordRichPresence _discord;

    [ObservableProperty] private bool _partyPresetsEnabled;
    [ObservableProperty] private bool _isDirty;

    public ObservableCollection<DiscordPartyPresetItemViewModel> PartyPresets { get; } = [];

    public DiscordPartyPresetsViewModel(SettingsService settingsService, DiscordRichPresence discord)
    {
        _settingsService = settingsService;
        _discord = discord;

        var s = settingsService.Settings;
        _partyPresetsEnabled = s.DiscordPartyPresetsEnabled;

        foreach (var preset in s.DiscordPartyPresets)
            PartyPresets.Add(new DiscordPartyPresetItemViewModel(preset, MarkDirty));
    }

    partial void OnPartyPresetsEnabledChanged(bool value)
    {
        IsDirty = true;
    }

    [RelayCommand]
    private void AddPartyPreset()
    {
        PartyPresets.Add(new DiscordPartyPresetItemViewModel(new DiscordPartyPreset
        {
            Label = $"Preset {PartyPresets.Count + 1}"
        }, MarkDirty));
        IsDirty = true;
    }

    [RelayCommand]
    private void RemovePartyPreset(DiscordPartyPresetItemViewModel? preset)
    {
        if (preset == null) return;
        PartyPresets.Remove(preset);
        IsDirty = true;
    }

    public bool HasPendingChanges => IsDirty;

    public void SavePendingChanges()
    {
        if (!IsDirty) return;

        var presets = PartyPresets.Select(p => p.ToModel()).ToList();
        _settingsService.Update(s =>
        {
            s.DiscordPartyPresetsEnabled = PartyPresetsEnabled;
            s.DiscordPartyPresets = presets;
        });
        IsDirty = false;
        _discord.EnqueueRefresh();
    }

    private void MarkDirty() => IsDirty = true;
}
