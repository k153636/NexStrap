using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Models;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class ModsViewModel : ViewModelBase
{
    private readonly ModService _modService;

    [ObservableProperty] private ObservableCollection<Mod> _mods = new();
    [ObservableProperty] private Mod? _selectedMod;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ModsViewModel(ModService modService)
    {
        _modService = modService;
        modService.ModsChanged += (_, _) => RefreshMods();
        RefreshMods();
    }

    private void RefreshMods()
    {
        Mods = new ObservableCollection<Mod>(_modService.Mods);
    }

    public async Task ImportModAsync(IStorageFolder folder)
    {
        IsLoading = true;
        StatusMessage = "インポート中...";
        try
        {
            var mod = await _modService.ImportModAsync(folder.Path.LocalPath);
            StatusMessage = mod != null ? $"{mod.Name} をインポートしました" : "インポートに失敗しました";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ApplyModsAsync()
    {
        IsLoading = true;
        StatusMessage = "Mod を適用中...";
        await _modService.ApplyEnabledModsAsync();
        StatusMessage = "Mod を適用しました";
        IsLoading = false;
    }

    [RelayCommand]
    private void ToggleMod(Mod? mod)
    {
        if (mod == null) return;
        _modService.ToggleMod(mod.Id, !mod.IsEnabled);
        RefreshMods();
    }

    [RelayCommand]
    private void RemoveMod(Mod? mod)
    {
        if (mod == null) return;
        _modService.RemoveMod(mod.Id);
        StatusMessage = $"{mod.Name} を削除しました";
    }
}
