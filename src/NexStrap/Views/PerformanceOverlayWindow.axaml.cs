using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NexStrap.Services;

namespace NexStrap.Views;

// DataContext = this だと INotifyPropertyChanged が効かないため専用 VM を使う
internal sealed partial class OverlayViewModel : ObservableObject
{
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _memoryPercent;
    [ObservableProperty] private string _cpuText    = "0%";
    [ObservableProperty] private string _memoryText = "0 MB";
}

public partial class PerformanceOverlayWindow : Window
{
    private readonly PerformanceMonitorService _monitor;
    private readonly OverlayViewModel _vm = new();

    public PerformanceOverlayWindow() : this(null!) { }

    public PerformanceOverlayWindow(PerformanceMonitorService monitor)
    {
        InitializeComponent();
        _monitor    = monitor;
        DataContext = _vm;

        var screen = Screens.Primary;
        if (screen != null)
        {
            Position = new PixelPoint(
                screen.WorkingArea.Right - 170,
                screen.WorkingArea.Y + 12);
        }

        _monitor.StatsUpdated += OnStatsUpdated;
    }

    private void OnStatsUpdated(object? sender, PerformanceStats stats)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _vm.CpuPercent    = stats.CpuPercent;
            _vm.MemoryPercent = Math.Min(100, stats.MemoryMb / 80.0);
            _vm.CpuText       = $"{stats.CpuPercent:F0}%";
            _vm.MemoryText    = $"{stats.MemoryMb}MB";
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitor.StatsUpdated -= OnStatsUpdated;
        base.OnClosed(e);
    }
}
