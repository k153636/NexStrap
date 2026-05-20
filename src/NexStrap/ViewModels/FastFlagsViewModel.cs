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
    [ObservableProperty] private string _selectedCategory = "すべて";
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

    public List<string> Categories { get; } = new()
    {
        "すべて", "パフォーマンス", "グラフィックス", "ネットワーク", "UI", "アバター", "カスタム"
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
        _profileService.LoadProfiles();
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
        await ShowStatusAsync($"プロファイル「{value.Name}」を読み込みました");
    }

    [RelayCommand]
    private async Task SaveAsProfileAsync()
    {
        var name = NewProfileName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            await ShowStatusAsync("プロファイル名を入力してください", isError: true);
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
        await ShowStatusAsync($"プロファイル「{name}」を保存しました");
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile == null) return;
        if (SelectedProfile.IsDefault)
        {
            await ShowStatusAsync("デフォルトプロファイルは削除できません", isError: true);
            return;
        }
        var name = SelectedProfile.Name;
        _profileService.DeleteProfile(SelectedProfile.Id);
        RefreshProfiles();
        SelectedProfile = Profiles.FirstOrDefault();
        await ShowStatusAsync($"プロファイル「{name}」を削除しました");
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
                Description = desc,
                Category    = cat
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
    }

    private void ApplyFilter()
    {
        var filtered = _allFlags.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(f =>
                f.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                f.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        if (SelectedCategory != "すべて")
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
            await ShowStatusAsync("Roblox が見つかりません。インストールを確認してください", isError: true);
            return;
        }

        IsSaving = true;
        foreach (var flag in _allFlags)
            _service.Set(flag.Name, flag.Value);
        await _service.SaveAsync();
        IsSaving = false;
        await ShowStatusAsync($"保存しました ({_allFlags.Count} flags)");
    }

    [RelayCommand]
    private async Task HotReloadAsync()
    {
        if (string.IsNullOrEmpty(_service.GetSavePath()))
        {
            await ShowStatusAsync("Roblox が見つかりません", isError: true);
            return;
        }

        var dict = _allFlags.Where(f => f.IsEnabled)
            .ToDictionary(f => f.Name, f => f.Value);
        await _service.HotReloadAsync(dict);
        await ShowStatusAsync($"ホットリロード完了 — 次のゲーム参加時に反映されます ({dict.Count} flags)");
    }

    [RelayCommand]
    private async Task AddFlagAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFlagName)) return;
        if (string.IsNullOrEmpty(_service.GetSavePath()))
        {
            await ShowStatusAsync("Roblox が見つかりません", isError: true);
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
        await ShowStatusAsync($"{entry.Name} を追加・保存しました");
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
    private async Task ApplyPresetsAsync()
    {
        _service.ApplyPreset(FastFlagPresets.All);
        LoadFlags();
        await ShowStatusAsync("プリセットを適用しました");
    }

    [RelayCommand]
    private void SetFpsTarget()
    {
        _service.Set("DFIntTaskSchedulerTargetFps", FpsTarget.ToString());
        var entry = _allFlags.FirstOrDefault(f => f.Name == "DFIntTaskSchedulerTargetFps");
        if (entry != null) entry.Value = FpsTarget.ToString();
        else
        {
            var newEntry = new FlagEntry("DFIntTaskSchedulerTargetFps", FpsTarget.ToString())
                { Category = "パフォーマンス", Description = "FPS 上限" };
            _allFlags.Add(newEntry);
        }
        ApplyFilter();
    }
}
