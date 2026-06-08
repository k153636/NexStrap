using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace NexStrap.Views;

public partial class SplashWindow : Window
{
    public bool IsTestMode { get; set; }

    private bool _playing;
    private ScaleTransform _scale = null!;

    public SplashWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Attach ScaleTransform here so the AXAML source-generator field is available
        _scale = new ScaleTransform(0.85, 0.85);
        SplashContent.RenderTransform = _scale;

        if (IsTestMode) TestControls.IsVisible = true;
        _ = PlayAsync();
    }

    public async Task PlayAsync()
    {
        if (_playing) return;
        _playing = true;

        // Reset
        SplashContent.Opacity = 0;
        _scale.ScaleX = _scale.ScaleY = 0.85;
        GlowEllipse.Opacity = 0;

        await Task.Delay(40);

        // --- Phase 1: Scale up + fade in — parallel, 380 ms, ease-out ---
        var fadeIn = OpacityAnim(SplashContent, 0d, 1d, 380, new CubicEaseOut());
        var scaleUp = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(380),
            Easing   = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = {
                    new Setter(ScaleTransform.ScaleXProperty, 0.85d),
                    new Setter(ScaleTransform.ScaleYProperty, 0.85d),
                }},
                new KeyFrame { Cue = new Cue(1d), Setters = {
                    new Setter(ScaleTransform.ScaleXProperty, 1.0d),
                    new Setter(ScaleTransform.ScaleYProperty, 1.0d),
                }},
            }
        };
        await Task.WhenAll(fadeIn.RunAsync(SplashContent), scaleUp.RunAsync(_scale));
        SplashContent.Opacity = 1;
        _scale.ScaleX = _scale.ScaleY = 1.0;

        // Hold so the logo settles before the glow fires
        await Task.Delay(150);

        // --- Phase 2: Glow pulse — one-shot ---
        await OpacityAnim(GlowEllipse, 0d, 0.48d, 150, new LinearEasing()).RunAsync(GlowEllipse);
        GlowEllipse.Opacity = 0.48;
        await OpacityAnim(GlowEllipse, 0.48d, 0d, 280, new CubicEaseOut()).RunAsync(GlowEllipse);
        GlowEllipse.Opacity = 0;

        _playing = false;
        if (IsTestMode) return;

        // Hold before exit
        await Task.Delay(150);

        // --- Phase 3: Fade window to black, then close ---
        await OpacityAnim(this, 1d, 0d, 260, new CubicEaseIn()).RunAsync(this);
        Close();
    }

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
        => _ = PlayAsync();

    private void CloseTestButton_Click(object? sender, RoutedEventArgs e)
        => Close();
}
