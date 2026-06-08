using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace NexStrap.Views;

public partial class SplashWindow : Window
{
    public bool IsTestMode { get; set; }

    private bool _playing;
    private RotateTransform _rotate = null!;
    private CancellationTokenSource _cts = new();

    public SplashWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _rotate = new RotateTransform(0);
        LogoImage.RenderTransform = _rotate;
        if (IsTestMode) TestControls.IsVisible = true;
        _ = PlayAsync();
    }

    // Call from App when initialization is complete
    public void Complete() => _cts.Cancel();

    public async Task PlayAsync()
    {
        if (_playing) return;
        _playing = true;
        _cts = new CancellationTokenSource();

        // Reset
        SplashContent.Opacity = 0;
        _rotate.Angle = 0;
        Opacity = 1;

        await Task.Delay(40);

        // Phase 1: fade in (280ms)
        await OpacityAnim(SplashContent, 0d, 1d, 280, new CubicEaseOut()).RunAsync(SplashContent);
        SplashContent.Opacity = 1;

        // Phase 2: spin until Complete() is called
        await SpinLoopAsync(_cts.Token);

        _playing = false;
        if (IsTestMode) return;

        // Phase 3: fade out
        await OpacityAnim(this, 1d, 0d, 260, new CubicEaseIn()).RunAsync(this);
        Close();
    }

    private async Task SpinLoopAsync(CancellationToken ct)
    {
        // DispatcherTimer-driven: directly sets _rotate.Angle each frame.
        // Avalonia's Animation.RunAsync on Transform targets doesn't reliably
        // propagate animated values back to the visual, so we drive manually.
        const double tSpin = 1100.0;
        const int    tHold = 400;

        while (!ct.IsCancellationRequested)
        {
            var tcs = new TaskCompletionSource<bool>();
            var sw  = System.Diagnostics.Stopwatch.StartNew();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                if (ct.IsCancellationRequested) { timer.Stop(); tcs.TrySetResult(true);  return; }
                double elapsed = sw.ElapsedMilliseconds;
                if (elapsed >= tSpin)           { timer.Stop(); tcs.TrySetResult(false); return; }
                double t     = elapsed / tSpin;
                double eased = SplineEase(t, 0.2, 0.8, 0.4, 1.0);
                _rotate.Angle = eased * 360.0;
            };
            timer.Start();

            if (await tcs.Task) break; // cancelled
            _rotate.Angle = 0; // normalize — 360° == 0° visually

            try { await Task.Delay(tHold, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static double SplineEase(double t, double x1, double y1, double x2, double y2)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        double s = t;
        for (int i = 0; i < 8; i++)
        {
            double dx = Bz(s, x1, x2) - t;
            if (Math.Abs(dx) < 1e-6) break;
            double d = BzD(s, x1, x2);
            if (Math.Abs(d)  < 1e-6) break;
            s -= dx / d;
        }
        return Bz(s, y1, y2);
    }
    private static double Bz (double t, double c1, double c2)
        => 3*c1*t*(1-t)*(1-t) + 3*c2*t*t*(1-t) + t*t*t;
    private static double BzD(double t, double c1, double c2)
        => 3*c1*(1-t)*(1-2*t) + 3*c2*t*(2-3*t) + 3*t*t;

    private static Animation OpacityAnim(
        Animatable _, double from, double to, int ms, Easing easing) => new()
    {
        Duration = TimeSpan.FromMilliseconds(ms),
        Easing   = easing,
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, from) } },
            new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, to)   } },
        }
    };

    private void ReplayButton_Click(object? sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        _playing = false;
        _ = PlayAsync();
    }

    private void CloseTestButton_Click(object? sender, RoutedEventArgs e)
        => Close();
}
