using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Models;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class FlagEntry : ObservableObject
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _value;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string _category = "Custom";
    [ObservableProperty] private string _description = string.Empty;

    public Func<FlagEntry, Task>? DeleteAction { get; set; }

    [RelayCommand]
    private Task Delete() => DeleteAction?.Invoke(this) ?? Task.CompletedTask;

    public FlagEntry(string name, string value)
    {
        _name = name;
        _value = value;
    }
}

public partial class FastFlagsViewModel : ViewModelBase
{
    private readonly FastFlagService _service;
    private readonly ProfileService _profileService;
    private List<FlagEntry> _allFlags = new();
    private CancellationTokenSource? _statusCts;
    private bool _suppressProfileLoad;

    [ObservableProperty] private ObservableCollection<FlagEntry> _flags = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = "All";
    [ObservableProperty] private FlagEntry? _selectedFlag;
    [ObservableProperty] private string _newFlagName = string.Empty;
    [ObservableProperty] private string _newFlagValue = string.Empty;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isStatusError;
    [ObservableProperty] private int _fpsTarget = 144;
    [ObservableProperty] private string _savePath = string.Empty;

    // プロファイル
    [ObservableProperty] private ObservableCollection<Profile> _profiles = new();
    [ObservableProperty] private Profile? _selectedProfile;
    [ObservableProperty] private string _newProfileName = string.Empty;
    [ObservableProperty] private bool _isPresetPanelOpen;
    [ObservableProperty] private bool _isPresetActive;

    public IReadOnlyList<PresetGroup> PresetGroups => FastFlagBundles.Groups;

    public List<string> Categories { get; } = new()
    {
        "All", "Performance", "Graphics", "Network", "UI", "Avatar", "Custom"
    };

    public FastFlagsViewModel(FastFlagService service, ProfileService profileService)
    {
        _service = service;
        _profileService = profileService;
        service.FlagsChanged += (_, _) => Dispatcher.UIThread.Post(LoadFlags);
        LoadFlags();
        SavePath = service.GetSavePath();
        RefreshProfiles();
    }

    private void RefreshProfiles()
    {
        Profiles = new ObservableCollection<Profile>(_profileService.Profiles);
    }

    partial void OnSelectedProfileChanged(Profile? value)
    {
        if (value == null || _suppressProfileLoad) return;
        _ = LoadProfileAsync(value);
    }

    private async Task LoadProfileAsync(Profile value)
    {
        await _service.HotReloadAsync(
            value.FastFlags.Where(f => f.IsEnabled)
                          .ToDictionary(f => f.Name, f => f.Value)
        );
        await Dispatcher.UIThread.InvokeAsync(LoadFlags);
        await ShowStatusAsync($"Loaded profile \"{value.Name}\"");
    }

    [RelayCommand]
    private async Task SaveAsProfileAsync()
    {
        var name = NewProfileName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            await ShowStatusAsync("Enter a profile name", isError: true);
            return;
        }
        var flags = _allFlags.Select(f => new FastFlag
        {
            Name = f.Name, Value = f.Value,
            Category = f.Category, Description = f.Description,
            IsEnabled = true, IsPreset = false
        }).ToList();

        var profile = _profileService.CreateProfile(name);
        profile.FastFlags = flags;
        _profileService.UpdateProfile(profile);

        _suppressProfileLoad = true;
        RefreshProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        _suppressProfileLoad = false;

        NewProfileName = string.Empty;
        await ShowStatusAsync($"Saved profile \"{name}\"");
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile == null) return;
        if (SelectedProfile.IsDefault)
        {
            await ShowStatusAsync("Cannot delete the default profile", isError: true);
            return;
        }
        var name = SelectedProfile.Name;
        _profileService.DeleteProfile(SelectedProfile.Id);
        RefreshProfiles();
        SelectedProfile = Profiles.FirstOrDefault();
        await ShowStatusAsync($"Deleted profile \"{name}\"");
    }

    private async Task ShowStatusAsync(string message, bool isError = false, int durationMs = 3000)
    {
        _statusCts?.Cancel();
        _statusCts = new CancellationTokenSource();
        var token = _statusCts.Token;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsStatusError = isError;
            StatusMessage = message;
        });

        try
        {
            await Task.Delay(durationMs, token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = string.Empty;
                IsStatusError = false;
            });
        }
        catch (TaskCanceledException) { }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    private void LoadFlags()
    {
        var rawFlags = _service.GetAll();
        _allFlags = rawFlags.Select(kvp =>
        {
            var (desc, cat) = FlagDescriptions.Lookup(kvp.Key);
            return new FlagEntry(kvp.Key, kvp.Value)
            {
                Description  = desc,
                Category     = cat,
                DeleteAction = RemoveFlagAsync
            };
        }).ToList();

        // プリセット情報で上書き（より詳細な場合）
        foreach (var preset in FastFlagPresets.All)
        {
            var entry = _allFlags.FirstOrDefault(f => f.Name == preset.Name);
            if (entry != null)
            {
                entry.Category    = preset.Category;
                entry.Description = preset.Description;
            }
        }

        ApplyFilter();
        RefreshPresetState();
    }

    private void RefreshPresetState()
    {
        var current = _service.GetAll();
        IsPresetActive = FastFlagBundles.AllFlags.All(f =>
            current.TryGetValue(f.Name, out var v) && v == f.Value);
    }

    private void ApplyFilter()
    {
        var filtered = _allFlags.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(f =>
                f.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                f.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        if (SelectedCategory != "All")
            filtered = filtered.Where(f => f.Category == SelectedCategory);

        var result = new ObservableCollection<FlagEntry>(filtered);
        if (Dispatcher.UIThread.CheckAccess())
            Flags = result;
        else
            Dispatcher.UIThread.Post(() => Flags = result);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrEmpty(_service.GetSavePath()))
        {
            await ShowStatusAsync("Roblox not found. Check your installation", isError: true);
            return;
        }

        IsSaving = true;
        foreach (var flag in _allFlags)
            _service.Set(flag.Name, flag.Value);
        await _service.SaveAsync();
        IsSaving = false;
        await ShowStatusAsync($"Saved ({_allFlags.Count} flags)");
    }

    [RelayCommand]
    private async Task HotReloadAsync()
    {
        if (string.IsNullOrEmpty(_service.GetSavePath()))
        {
            await ShowStatusAsync("Roblox not found", isError: true);
            return;
        }

        var dict = _allFlags.Where(f => f.IsEnabled)
            .ToDictionary(f => f.Name, f => f.Value);
        await _service.HotReloadAsync(dict);
        await ShowStatusAsync($"Hot reload complete — takes effect on next game join ({dict.Count} flags)");
    }

    [RelayCommand]
    private async Task AddFlagAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFlagName)) return;
        if (string.IsNullOrEmpty(_service.GetSavePath()))
        {
            await ShowStatusAsync("Roblox not found", isError: true);
            return;
        }
        var (desc, cat) = FlagDescriptions.Lookup(NewFlagName.Trim());
        var entry = new FlagEntry(NewFlagName.Trim(), NewFlagValue.Trim())
        {
            Description = desc,
            Category    = cat
        };
        _allFlags.Add(entry);
        _service.Set(entry.Name, entry.Value);
        NewFlagName = string.Empty;
        NewFlagValue = string.Empty;
        ApplyFilter();
        await _service.SaveAsync();
        await ShowStatusAsync($"Added and saved {entry.Name}");
    }

    [RelayCommand]
    private async Task RemoveFlagAsync(FlagEntry? flag)
    {
        if (flag == null) return;
        _allFlags.Remove(flag);
        _service.Remove(flag.Name);
        ApplyFilter();
        await _service.SaveAsync();
    }

    [RelayCommand]
    private void TogglePresetPanel() => IsPresetPanelOpen = !IsPresetPanelOpen;

    [RelayCommand]
    private async Task TogglePresetAsync()
    {
        if (IsPresetActive)
        {
            foreach (var f in FastFlagBundles.AllFlags)
                _service.Remove(f.Name);
        }
        else
        {
            foreach (var f in FastFlagBundles.AllFlags)
                _service.Set(f.Name, f.Value);
        }
        await _service.SaveAsync();
        IsPresetActive = !IsPresetActive;
        LoadFlags();
        await ShowStatusAsync(IsPresetActive
            ? $"Optimization preset applied ({FastFlagBundles.AllFlags.Count} flags)"
            : "Optimization preset removed");
    }

    [RelayCommand]
    private async Task ApplyPresetsAsync()
    {
        _service.ApplyPreset(FastFlagPresets.All);
        LoadFlags();
        await ShowStatusAsync("Preset applied");
    }

    [RelayCommand]
    private async Task SetFpsTargetAsync()
    {
        _service.Set("DFIntTaskSchedulerTargetFps", FpsTarget.ToString());
        var entry = _allFlags.FirstOrDefault(f => f.Name == "DFIntTaskSchedulerTargetFps");
        if (entry != null)
            entry.Value = FpsTarget.ToString();
        else
            _allFlags.Add(new FlagEntry("DFIntTaskSchedulerTargetFps", FpsTarget.ToString())
                { Category = "Performance", Description = "FPS limit" });
        ApplyFilter();
        await _service.SaveAsync();
    }
}
