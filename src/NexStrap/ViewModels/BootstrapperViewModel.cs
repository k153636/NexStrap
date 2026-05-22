using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class BootstrapperViewModel : ViewModelBase
{
    private readonly RobloxService _roblox;

    [ObservableProperty] private string  _statusText       = "Preparing...";
    [ObservableProperty] private double  _progressValue    = 0;
    [ObservableProperty] private bool    _isIndeterminate  = true;
    [ObservableProperty] private bool    _isCancelVisible  = true;

    public event EventHandler? CloseRequested;

    public BootstrapperViewModel(RobloxService roblox)
    {
        _roblox = roblox;
        _roblox.BootstrapperProgress += OnProgress;
        _roblox.StatusChanged        += OnStatusChanged;
    }

    private void OnProgress(object? sender, BootstrapperProgress p)
    {
        StatusText      = p.Message;
        ProgressValue   = p.Percent;
        IsIndeterminate = p.IsIndeterminate;
    }

    private void OnStatusChanged(object? sender, RobloxStatus status)
    {
        if (status is RobloxStatus.Running or RobloxStatus.NotInstalled or RobloxStatus.Idle)
            CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        IsCancelVisible = false;
        StatusText = "Cancelling...";
        _roblox.CancelInstall();
    }

    public void Detach()
    {
        _roblox.BootstrapperProgress -= OnProgress;
        _roblox.StatusChanged        -= OnStatusChanged;
    }
}
