using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace NexStrap.Helpers;

public static class AnimationHelper
{
    private static readonly SplineEasing AppleEase = new(0.2, 0.8, 0.2, 1.0);
    private const double Duration     = 520;
    private const double StaggerStep  = 0.072;
    private const double MaxDelay     = 0.50;
    private const double TranslateY   = 26;

    // ── Burst counter for AnimateIn stagger ───────────────────────────────
    private static int      _burstCount;
    private static DateTime _burstStart = DateTime.MinValue;
    private const  int      BurstWindowMs = 400;

    private static int NextBurstIndex()
    {
        var now = DateTime.UtcNow;
        if ((now - _burstStart).TotalMilliseconds > BurstWindowMs)
        {
            _burstCount = 0;
            _burstStart = now;
        }
        return _burstCount++;
    }

    // ── StaggerChildren ────────────────────────────────────────────────────
    public static readonly AttachedProperty<bool> StaggerChildrenProperty =
        AvaloniaProperty.RegisterAttached<Panel, bool>(
            "StaggerChildren", typeof(AnimationHelper));

    public static bool GetStaggerChildren(Panel e) => e.GetValue(StaggerChildrenProperty);
    public static void SetStaggerChildren(Panel e, bool v) => e.SetValue(StaggerChildrenProperty, v);

    // ── AnimateIn ──────────────────────────────────────────────────────────
    public static readonly AttachedProperty<bool> AnimateInProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "AnimateIn", typeof(AnimationHelper));

    public static bool GetAnimateIn(Control e) => e.GetValue(AnimateInProperty);
    public static void SetAnimateIn(Control e, bool v) => e.SetValue(AnimateInProperty, v);

    static AnimationHelper()
    {
        StaggerChildrenProperty.Changed.AddClassHandler<Panel>(OnStaggerChildrenChanged);
        AnimateInProperty.Changed.AddClassHandler<Control>(OnAnimateInChanged);
    }

    private static void OnStaggerChildrenChanged(Panel panel, AvaloniaPropertyChangedEventArgs e)
    {
        if (!e.GetNewValue<bool>()) return;
        panel.Loaded += (_, _) =>
        {
            for (int i = 0; i < panel.Children.Count; i++)
                RunAnimation(panel.Children[i], i * StaggerStep);
        };
    }

    private static void OnAnimateInChanged(Control ctrl, AvaloniaPropertyChangedEventArgs e)
    {
        if (!e.GetNewValue<bool>()) return;
        ctrl.Loaded += (_, _) =>
        {
            var idx = NextBurstIndex();
            RunAnimation(ctrl, Math.Min(idx * StaggerStep, MaxDelay));
        };
    }

    // ── Core animation (DispatcherTimer-driven, 60 fps) ───────────────────
    public static void RunAnimation(Control ctrl, double delaySeconds)
    {
        ctrl.Opacity = 0;
        var translate = new TranslateTransform(0, TranslateY);
        ctrl.RenderTransform = translate;

        var delayEnd  = DateTime.UtcNow.AddSeconds(delaySeconds);
        var animStart = DateTime.MinValue;

        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            if (now < delayEnd) return;

            if (animStart == DateTime.MinValue)
                animStart = now;

            var rawT  = Math.Clamp((now - animStart).TotalMilliseconds / Duration, 0.0, 1.0);
            var eased = AppleEase.Ease(rawT);

            ctrl.Opacity  = eased;
            translate.Y   = TranslateY * (1.0 - eased);

            if (rawT >= 1.0)
            {
                timer.Stop();
                ctrl.Opacity         = 1;
                translate.Y          = 0;
                ctrl.RenderTransform = null;
            }
        };
        timer.Start();
    }
}
