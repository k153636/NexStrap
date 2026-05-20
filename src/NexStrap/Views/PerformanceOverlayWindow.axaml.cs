using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using NexStrap.Core.Services;

namespace NexStrap.Views;

public partial class PerformanceOverlayWindow : Window
{
    private readonly PerformanceMonitorService _monitor;

    public double CpuPercent { get; private set; }
    public double MemoryPercent { get; private set; }
    public string CpuText { get; private set; } = "0%";
    public string MemoryText { get; private set; } = "0 MB";

    public PerformanceOverlayWindow(PerformanceMonitorService monitor)
    {
        InitializeComponent();
        _monitor = monitor;
        DataContext = this;

        // 画面右上に配置
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
            CpuPercent    = stats.CpuPercent;
            MemoryPercent = Math.Min(100, stats.MemoryMb / 80.0); // 8GB を 100% とする目安
            CpuText       = $"{stats.CpuPercent:F0}%";
            MemoryText    = $"{stats.MemoryMb}MB";
            DataContext   = null;
            DataContext   = this;
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitor.StatsUpdated -= OnStatsUpdated;
        base.OnClosed(e);
    }
}
