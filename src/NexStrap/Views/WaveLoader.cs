using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace NexStrap.Views;

/// <summary>
/// Animated wave loader — bars travel from both edges inward toward the center.
/// </summary>
public class WaveLoader : Control
{
    private DispatcherTimer? _timer;
    private double _phase;

    // Bar geometry
    private const int    BarCount   = 20;
    private const double BarWidth   = 3.5;
    private const double MaxBarH    = 30.0;
    private const double MinBarH    = 3.0;

    // Wave timing
    private const double PhaseSpeed = 0.07;   // radians per frame (~60 fps)
    private const double PhaseStep  = 0.52;   // phase lag per bar from edge toward center

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            OnTick);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _phase += PhaseSpeed;
        if (_phase > Math.PI * 20) _phase -= Math.PI * 20; // prevent double overflow
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var gap     = (w - BarCount * BarWidth) / (BarCount - 1);
        var centerY = h / 2.0;
        var halfN   = BarCount / 2.0;

        for (int i = 0; i < BarCount; i++)
        {
            // 0 = outermost edge bar, (halfN - 1) = innermost (center)
            var distFromEdge = Math.Min(i, BarCount - 1 - i);

            // Outer bars lead — the wave crest arrives at the edge first and
            // propagates inward, so center bars have a larger phase offset (= later peak).
            var phaseOffset = distFromEdge * PhaseStep;
            var sine        = Math.Sin(_phase - phaseOffset);
            var t           = (sine + 1.0) / 2.0; // 0 → 1

            var barH = MinBarH + t * (MaxBarH - MinBarH);
            var x    = i * (BarWidth + gap);
            var y    = centerY - barH / 2.0;

            // Subtle brightness gradient: center bars slightly brighter than edges
            var norm  = distFromEdge / (halfN - 1.0);          // 0 = edge, 1 = center
            var alpha = (byte)(160 + norm * 95);               // 160 … 255
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));

            context.FillRectangle(brush,
                new Rect(x, y, BarWidth, barH),
                (float)(BarWidth / 2));   // rounded caps
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width)
            ? BarCount * (BarWidth + 10)
            : availableSize.Width;
        return new Size(width, MaxBarH + 10);
    }
}
