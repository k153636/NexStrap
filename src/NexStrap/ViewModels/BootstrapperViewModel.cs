using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Services;

namespace NexStrap.ViewModels;

public partial class BootstrapperViewModel : ViewModelBase
{
    private readonly RobloxService _roblox;

    [ObservableProperty] private string _statusText      = "Preparing...";
    [ObservableProperty] private double _progressValue   = 0;
    [ObservableProperty] private bool   _isIndeterminate = true;
    [ObservableProperty] private bool   _isCancelVisible = false;
    [ObservableProperty] private string _detailText      = string.Empty;

    public bool HasDetail => !string.IsNullOrEmpty(DetailText);

    // Theme snapshot — read once; window is short-lived
    public string BackgroundImagePath   { get; }
    public double BackgroundBlurRadius  { get; }
    public double BackgroundImageOpacity { get; }
    public bool   GlassThemeEnabled     { get; }
    public bool   HasBackgroundImage    { get; }

    public event EventHandler? CloseRequested;

    public BootstrapperViewModel(RobloxService roblox, SettingsService settings)
    {
        _roblox = roblox;

        var s = settings.Settings;
        var imgPath            = !string.IsNullOrEmpty(s.BootstrapperImagePath)
                                     ? s.BootstrapperImagePath
                                     : s.BackgroundImagePath;
        BackgroundImagePath    = imgPath;
        BackgroundBlurRadius   = 0;
        BackgroundImageOpacity = 0.85;
        GlassThemeEnabled      = s.GlassThemeEnabled;
        HasBackgroundImage     = !string.IsNullOrEmpty(imgPath);

        _roblox.StatusChanged += (_, s) =>
        {
            if (s is RobloxStatus.Launching or RobloxStatus.Updating)
                Dispatcher.UIThread.InvokeAsync(() => IsCancelVisible = true);
        };

        _roblox.BootstrapperProgress += OnProgress;
        _roblox.StatusChanged        += OnStatusChanged;
    }

    public BootstrapperViewModel(StudioService studio, SettingsService settings)
    {
        _roblox = null!;

        var s = settings.Settings;
        var imgPath            = !string.IsNullOrEmpty(s.BootstrapperImagePath)
                                     ? s.BootstrapperImagePath
                                     : s.BackgroundImagePath;
        BackgroundImagePath    = imgPath;
        BackgroundBlurRadius   = 0;
        BackgroundImageOpacity = 0.85;
        GlassThemeEnabled      = s.GlassThemeEnabled;
        HasBackgroundImage     = !string.IsNullOrEmpty(imgPath);

        _cancelAction = studio.CancelInstall;

        studio.BootstrapperProgress += OnProgress;
        studio.StatusChanged        += OnStudioStatusChanged;

        _studioDetachAction = () =>
        {
            studio.BootstrapperProgress -= OnProgress;
            studio.StatusChanged        -= OnStudioStatusChanged;
        };
    }

    private Action? _cancelAction;
    private Action? _studioDetachAction;

    private void OnProgress(object? sender, BootstrapperProgress p)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            StatusText      = p.Message;
            ProgressValue   = p.Percent;
            IsIndeterminate = p.IsIndeterminate;
            DetailText      = p.Detail ?? string.Empty;
            OnPropertyChanged(nameof(HasDetail));
        });
    }

    private void OnStatusChanged(object? sender, RobloxStatus status)
    {
        if (status is RobloxStatus.Running or RobloxStatus.NotInstalled or RobloxStatus.Idle)
            Dispatcher.UIThread.InvokeAsync(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    private void OnStudioStatusChanged(object? sender, RobloxStatus status)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (status is RobloxStatus.Updating)
                IsCancelVisible = true;
            else if (status is RobloxStatus.Running or RobloxStatus.Idle)
                CloseRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    [RelayCommand]
    private void Cancel()
    {
        IsCancelVisible = false;
        StatusText = "Cancelling...";
        if (_cancelAction != null)
            _cancelAction();
        else
            _roblox.CancelInstall();
    }

    public void Detach()
    {
        if (_studioDetachAction != null)
        {
            _studioDetachAction();
            return;
        }
        if (_roblox == null) return; // 手動制御コンストラクタ使用時は _roblox が null
        _roblox.BootstrapperProgress -= OnProgress;
        _roblox.StatusChanged        -= OnStatusChanged;
    }

    /// <summary>プラグインダウンロード等、手動制御用コンストラクタ。</summary>
    public BootstrapperViewModel(SettingsService settings)
    {
        _roblox = null!;

        var s = settings.Settings;
        var imgPath            = !string.IsNullOrEmpty(s.BootstrapperImagePath)
                                     ? s.BootstrapperImagePath
                                     : s.BackgroundImagePath;
        BackgroundImagePath    = imgPath;
        BackgroundBlurRadius   = 0;
        BackgroundImageOpacity = 0.85;
        GlassThemeEnabled      = s.GlassThemeEnabled;
        HasBackgroundImage     = !string.IsNullOrEmpty(imgPath);
    }

    /// <summary>手動でウィンドウを閉じるよう要求する。</summary>
    public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>プログレスを手動で更新する。</summary>
    public void ReportProgress(string message, double percent, bool indeterminate = false)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            StatusText      = message;
            ProgressValue   = percent;
            IsIndeterminate = indeterminate;
        });
    }
}
