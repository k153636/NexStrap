using System.Collections.Generic;
using System.Runtime.InteropServices;
using NexStrap.Services;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using NexStrap.ViewModels;

namespace NexStrap.Views;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public MainWindow()
    {
        InitializeComponent();

        if (NavView.MenuItems.FirstOrDefault() is NavigationViewItem first)
            NavView.SelectedItem = first;

        DataContextChanged += (_, _) => WireGlassTheme();
        KeyDown += OnKeyDown;

        NavView.TemplateApplied += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                ApplyGlassTheme(vm.ThemeVM.GlassThemeEnabled);
        };

        Loaded += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                ApplyGlassTheme(vm.ThemeVM.GlassThemeEnabled);
            StartSplashOverlay();
        };

        Activated += (_, _) =>
        {
            if (Application.Current is App app)
                app.SetBackgroundMode(false);
        };

        Deactivated += (_, _) =>
        {
            if (Application.Current is App app)
                app.SetBackgroundMode(true);
        };
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void TitleBar_DoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.K &&
            e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt))
        {
            var dialog = new PasswordDialog();
            await dialog.ShowDialog(this);
            if (dialog.Authenticated && DataContext is MainWindowViewModel vm)
                vm.NavigateToCommand.Execute("Dev");
        }
    }

    private void WireGlassTheme()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        ApplyGlassTheme(vm.ThemeVM.GlassThemeEnabled);
        vm.ThemeVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ThemeViewModel.GlassThemeEnabled) ||
                e.PropertyName == nameof(ThemeViewModel.GlassAccentColor) ||
                e.PropertyName == nameof(ThemeViewModel.GlassOpacity))
                ApplyGlassTheme(vm.ThemeVM.GlassThemeEnabled);
        };
    }

    // Non-glass solid colors (mid value used as SolidColorBrush)
    private static readonly Dictionary<string, (Color Top, Color Mid, Color Bot)> _solidGradients = new()
    {
        ["CardBg"]     = (Color.Parse("#000000"), Color.Parse("#000000"), Color.Parse("#000000")),
        ["SurfaceBg"]  = (Color.Parse("#000000"), Color.Parse("#000000"), Color.Parse("#000000")),
        ["ElevatedBg"] = (Color.Parse("#101010"), Color.Parse("#101010"), Color.Parse("#101010")),
        ["OverlayBg"]  = (Color.Parse("#0A0A0A"), Color.Parse("#0A0A0A"), Color.Parse("#0A0A0A")),
        ["InputBg"]    = (Color.Parse("#000000"), Color.Parse("#000000"), Color.Parse("#000000")),
        ["FgSub"]      = (Color.Parse("#555555"), Color.Parse("#555555"), Color.Parse("#555555")),
        ["FgMuted"]    = (Color.Parse("#2E2E2E"), Color.Parse("#2E2E2E"), Color.Parse("#2E2E2E")),
    };

    // Glass min/max alpha (slider 0% → 100%)
    private static readonly Dictionary<string, (byte Min, byte Max)> _glassAlphaRange = new()
    {
        ["CardBg"]     = (0x18, 0xD0),
        ["SurfaceBg"]  = (0x20, 0xD8),
        ["ElevatedBg"] = (0x2A, 0xE0),
        ["OverlayBg"]  = (0x24, 0xD4),
        ["InputBg"]    = (0x10, 0xC8),
    };

    private static readonly Dictionary<string, Color> _glassTextColors = new()
    {
        ["FgSub"]   = Color.Parse("#AAAAAA"),
        ["FgMuted"] = Color.Parse("#888888"),
    };

    private static LinearGradientBrush MakeGradient(Color top, Color bot) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint   = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = [new GradientStop(top, 0.0), new GradientStop(bot, 1.0)],
    };

    private static LinearGradientBrush MakeGradient3(Color top, Color mid, Color bot) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint   = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = [new GradientStop(top, 0.0), new GradientStop(mid, 0.45), new GradientStop(bot, 1.0)],
    };

    private void ApplyGlassTheme(bool glass)
    {
        var res = Application.Current!.Resources;
        var themeVm = (DataContext as MainWindowViewModel)?.ThemeVM;

        var opacity = Math.Clamp(themeVm?.GlassOpacity ?? 0.75, 0.0, 0.75);
        var t = opacity / 0.75;

        // Accent color parsed once — used for both card brushes and sidebar pane
        var accentHex = themeVm?.GlassAccentColor ?? "#FFFFFF";
        Color accent;
        try { accent = Color.Parse(accentHex); }
        catch { accent = Colors.White; }
        byte r = accent.R, g = accent.G, b = accent.B;

        if (glass)
        {
            foreach (var (key, (min, max)) in _glassAlphaRange)
            {
                var alpha  = (byte)Math.Round(min + (max - min) * t);
                var topA   = (byte)Math.Min(255, (int)(alpha * 1.18));
                var botA   = (byte)Math.Max(0,   (int)(alpha * 0.82));
                res[key] = MakeGradient(
                    Color.FromArgb(topA, r, g, b),
                    Color.FromArgb(botA, r, g, b));
            }
            foreach (var (key, color) in _glassTextColors)
                res[key] = new SolidColorBrush(color);

            res["DividerBrush"] = new SolidColorBrush(Colors.Transparent);
        }
        else
        {
            foreach (var (key, (top, mid, bot)) in _solidGradients)
                res[key] = new SolidColorBrush(mid);

            res["DividerBrush"] = new SolidColorBrush(Color.Parse("#10FFFFFF"));
        }

        // Sidebar pane: Glass ON follows accent color; Glass OFF is near-black
        IBrush paneBrush;
        if (glass)
        {
            var topA = (byte)Math.Round(0x88 + (0xE8 - 0x88) * t);
            var botA = (byte)Math.Round(0x78 + (0xD4 - 0x78) * t);
            paneBrush = MakeGradient(
                Color.FromArgb(topA, r, g, b),
                Color.FromArgb(botA, r, g, b));
        }
        else
        {
            paneBrush = MakeGradient(Color.Parse("#0D0D0D"), Color.Parse("#000000"));
        }

        var splitView = NavView.GetVisualDescendants().OfType<SplitView>().FirstOrDefault();
        if (splitView != null)
            splitView.PaneBackground = paneBrush;
    }

    // ── Splash overlay ──────────────────────────────────────────────────────

    // ── Splash overlay ──────────────────────────────────────────────────────
    //
    //  Timeline: fade-in(280ms) → spin-once(1100ms) → hold(400ms) → fade-out(260ms)
    //  Logo spins one full revolution with cubic-bezier(0.2, 0.8, 0.4, 1.0) easing,
    //  driven by DispatcherTimer so the angle is set every ~16ms on the UI thread.
    //  The 58x58 logo wrapper is rotated so the pivot matches splash-preview.html.

    private RotateTransform _splashRotate = null!;
    private TranslateTransform _splashLogoShift = null!;
    private TranslateTransform _splashTitleShift = null!;
    private double _splashProgress;

    private static readonly Logger Log = Logger.Instance;

    private void StartSplashOverlay()
    {
        _splashRotate = (RotateTransform)SplashLogoWrapper.RenderTransform!;
        _splashLogoShift = (TranslateTransform)SplashLogoMotion.RenderTransform!;
        _splashTitleShift = (TranslateTransform)SplashTitleText.RenderTransform!;
        _splashRotate.Angle = 0;
        Log.Info("Splash", $"StartSplashOverlay — ImageBounds={SplashLogoImage.Bounds}");
        _ = PlaySplashAsync();
    }

    private async Task PlaySplashAsync()
    {
        CancellationTokenSource? spinCts = null;
        Task? spinTask = null;

        try
        {
            SplashContent.Opacity = 0;
            _splashRotate.Angle   = 0;
            _splashLogoShift.X = 0;
            _splashTitleShift.X = 0;
            SplashTitleText.Opacity = 1;
            SplashStatusText.Opacity = 0;
            SplashProgressArea.Opacity = 0;
            SetSplashProgress(0);

            // Wait for the first rendered frame so the black overlay is on screen
            // before the fade-in begins.
            var ready = new TaskCompletionSource();
            Dispatcher.UIThread.Post(() => ready.TrySetResult(), DispatcherPriority.Render);
            await ready.Task;

            // Phase 1: fade in
            await SplashFadeAsync(SplashContent, from: 0, to: 1, ms: 280, new CubicEaseOut());
            await Task.WhenAll(
                SplashFadeAsync(SplashStatusText, from: 0, to: 0.58, ms: 180, new CubicEaseOut()),
                SplashFadeAsync(SplashProgressArea, from: 0, to: 0.82, ms: 220, new CubicEaseOut()),
                SplashProgressToAsync(0.08, 420, new CubicEaseOut()));

            // Phase 2: keep the logo rotating independently while startup work warms.
            spinCts = new CancellationTokenSource();
            spinTask = SplashSpinLoopAsync(spinCts.Token);

            await SplashProgressToAsync(0.82, 1000, new CubicEaseOut());

            // Phase 3: hold near completion until the startup status reaches Ready.
            await Task.WhenAll(
                SplashSettleBrandAsync(),
                SplashProgressToAsync(0.95, 900, new CubicEaseInOut()));
            await WaitForStartupReadyAsync(DataContext as MainWindowViewModel);
            await SplashProgressToAsync(1.0, 360, new CubicEaseOut());

            // Phase 4: fade out
            await SplashFadeAsync(SplashOverlay, from: 1, to: 0, ms: 260, new CubicEaseIn());
            spinCts.Cancel();
            await spinTask;
            SplashOverlay.IsVisible        = false;
            SplashOverlay.IsHitTestVisible = false;
        }
        catch (Exception ex)
        {
            Logger.Instance.Exception("Splash", ex);
            if (spinCts is not null && spinTask is not null)
            {
                spinCts.Cancel();
                await spinTask;
            }
            SplashOverlay.IsVisible        = false;
            SplashOverlay.IsHitTestVisible = false;
        }
        finally
        {
            spinCts?.Dispose();
        }
    }

    // Drives one 360° rotation over 1100 ms using a DispatcherTimer.
    private async Task SplashSpinLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
                await SplashSpinAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Splash is hidden, so the final angle does not need to be normalized.
        }
    }

    private Task SplashSpinAsync(CancellationToken cancellationToken)
    {
        const double DurationMs = 1500.0;

        var tcs   = new TaskCompletionSource();
        var sw    = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        CancellationTokenRegistration registration = default;

        registration = cancellationToken.Register(() =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                timer.Stop();
                registration.Dispose();
                tcs.TrySetCanceled(cancellationToken);
            });
        });

        timer.Tick += (_, _) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                timer.Stop();
                registration.Dispose();
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            double t = sw.ElapsedMilliseconds / DurationMs;
            if (t >= 1.0)
            {
                _splashRotate.Angle = 0; // normalize — 360° == 0° visually
                timer.Stop();
                registration.Dispose();
                tcs.TrySetResult();
                return;
            }
            _splashRotate.Angle = SplashEase(t) * 360.0;
        };

        timer.Start();
        return tcs.Task;
    }

    // cubic-bezier(0.2, 0.8, 0.4, 1.0) — solved via Newton-Raphson on the x axis.
    private static double SplashEase(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        double s = t;
        for (int i = 0; i < 8; i++)
        {
            double err = CbBez(s, 0.2, 0.4) - t;
            if (Math.Abs(err) < 1e-6) break;
            double d = CbBezD(s, 0.2, 0.4);
            if (Math.Abs(d)   < 1e-6) break;
            s -= err / d;
        }
        return CbBez(s, 0.8, 1.0);
    }

    private static double CbBez (double t, double c1, double c2) =>
        3 * c1 * t * (1 - t) * (1 - t) + 3 * c2 * t * t * (1 - t) + t * t * t;

    private static double CbBezD(double t, double c1, double c2) =>
        3 * c1 * (1 - t) * (1 - 2 * t) + 3 * c2 * t * (2 - 3 * t) + 3 * t * t;

    private async Task SplashSettleBrandAsync()
    {
        _splashTitleShift.X = 0;
        SplashTitleText.Opacity = 1;
        await Task.Delay(180);
    }

    private static async Task WaitForStartupReadyAsync(MainWindowViewModel? vm)
    {
        while (vm?.IsStartupLoading == true)
            await Task.Delay(80);
    }

    private Task SplashProgressToAsync(double to, int ms, Easing easing)
    {
        var from = _splashProgress;
        return SplashValueAsync(from, Math.Clamp(to, 0, 1), ms, easing, SetSplashProgress);
    }

    private void SetSplashProgress(double progress)
    {
        _splashProgress = Math.Clamp(progress, 0, 1);
        var trackWidth = SplashProgressTrack.Bounds.Width;
        SplashProgressFill.Width = Math.Max(0, trackWidth * _splashProgress);
    }

    private static Task SplashValueAsync(double from, double to, int ms, Easing easing, Action<double> set)
    {
        var tcs = new TaskCompletionSource();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };

        timer.Tick += (_, _) =>
        {
            var t = Math.Min(sw.ElapsedMilliseconds / (double)ms, 1.0);
            var eased = easing.Ease(t);
            set(from + (to - from) * eased);
            if (t < 1.0) return;
            timer.Stop();
            tcs.TrySetResult();
        };

        set(from);
        timer.Start();
        return tcs.Task;
    }

    private static Task SplashFadeAsync(Animatable target, double from, double to, int ms, Easing easing)
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            Easing   = easing,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, to)   } },
            }
        };
        return anim.RunAsync(target);
    }

    // ── End splash ──────────────────────────────────────────────────────────

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SettingsVM.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            (Application.Current as App)?.SetBackgroundMode(true);
            return;
        }
        base.OnClosing(e);
    }

    private void NavView_SelectionChanged(object? sender,
        NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is not NavigationViewItem item) return;
        var tag = item.Tag?.ToString() ?? "Home";

        if (DataContext is not MainWindowViewModel vm) return;

        if (tag.StartsWith("Settings_"))
        {
            var subTab = tag["Settings_".Length..];
            vm.SettingsVM.SelectTabCommand.Execute(subTab);
            return;
        }

        vm.NavigateToCommand.Execute(tag);
    }
}
