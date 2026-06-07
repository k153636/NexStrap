using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace NexStrap.Views;

public partial class SplashWindow : Window
{
    public bool IsTestMode { get; set; }

    private bool _playing;

    public SplashWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (IsTestMode) TestControls.IsVisible = true;
        _ = PlayAsync();
    }

    public async Task PlayAsync()
    {
        if (_playing) return;
        _playing = true;

        // Reset
        SplashContent.Opacity = 0;
        SplashContent.Margin  = new Thickness(0, 28, 0, 0);

        await Task.Delay(60);

        // --- Fade in + slide up ---
        var animIn = new Animation
        {
            Duration  = TimeSpan.FromMilliseconds(750),
            Easing    = new CubicEaseOut(),
            FillMode  = FillMode.Forward,
            Children  =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0d),
                        new Setter(MarginProperty,  new Thickness(0, 28, 0, 0))
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1d),
                        new Setter(MarginProperty,  new Thickness(0))
                    }
                }
            }
        };
        await animIn.RunAsync(SplashContent);
        SplashContent.Opacity = 1;
        SplashContent.Margin  = new Thickness(0);

        _playing = false;

        if (IsTestMode) return;

        // Hold
        await Task.Delay(1500);

        // --- Fade out ---
        var animOut = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(450),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 0d) } }
            }
        };
        await animOut.RunAsync(this);
        Close();
    }

    private void ReplayButton_Click(object? sender, RoutedEventArgs e)
        => _ = PlayAsync();

    private void CloseTestButton_Click(object? sender, RoutedEventArgs e)
        => Close();
}
