using System.Collections.ObjectModel;
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
    private List<FlagEntry> _allFlags = new();
    private CancellationTokenSource? _statusCts;

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

    public List<string> Categories { get; } = new()
    {
        "すべて", "パフォーマンス", "グラフィックス", "ネットワーク", "UI", "アバター", "Custom"
    };

    public FastFlagsViewModel(FastFlagService service)
    {
        _service = service;
        service.FlagsChanged += (_, _) => LoadFlags();
        LoadFlags();
        SavePath = service.GetSavePath();
    }

    private async Task ShowStatusAsync(string message, bool isError = false, int durationMs = 3000)
    {
        _statusCts?.Cancel();
        _statusCts = new CancellationTokenSource();
        var token = _statusCts.Token;

        IsStatusError = isError;
        StatusMessage = message;

        try
        {
            await Task.Delay(durationMs, token);
            StatusMessage = string.Empty;
            IsStatusError = false;
        }
        catch (TaskCanceledException) { }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    private void LoadFlags()
    {
        var rawFlags = _service.GetAll();
        _allFlags = rawFlags.Select(kvp => new FlagEntry(kvp.Key, kvp.Value)).ToList();

        foreach (var preset in FastFlagPresets.All)
        {
            var entry = _allFlags.FirstOrDefault(f => f.Name == preset.Name);
            if (entry != null)
            {
                entry.Category = preset.Category;
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

        Flags = new ObservableCollection<FlagEntry>(filtered);
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
    private void AddFlag()
    {
        if (string.IsNullOrWhiteSpace(NewFlagName)) return;
        var entry = new FlagEntry(NewFlagName.Trim(), NewFlagValue.Trim());
        _allFlags.Add(entry);
        _service.Set(entry.Name, entry.Value);
        NewFlagName = string.Empty;
        NewFlagValue = string.Empty;
        ApplyFilter();
    }

    [RelayCommand]
    private void RemoveFlag(FlagEntry? flag)
    {
        if (flag == null) return;
        _allFlags.Remove(flag);
        _service.Remove(flag.Name);
        ApplyFilter();
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
