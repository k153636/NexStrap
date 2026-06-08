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
        try
        {
            SplashContent.Opacity = 0;
            _splashRotate.Angle   = 0;
            _splashLogoShift.X = 0;
            _splashTitleShift.X = 18;
            SplashTitleText.Opacity = 0;
            SplashStatusText.Opacity = 0;

            // Wait for the first rendered frame so the black overlay is on screen
            // before the fade-in begins.
            var ready = new TaskCompletionSource();
            Dispatcher.UIThread.Post(() => ready.TrySetResult(), DispatcherPriority.Render);
            await ready.Task;

            // Phase 1: fade in
            await SplashFadeAsync(SplashContent, from: 0, to: 1, ms: 280, new CubicEaseOut());
            await SplashFadeAsync(SplashStatusText, from: 0, to: 0.72, ms: 160, new CubicEaseOut());

            // Phase 2: spin while startup work is being warmed, capped at 3 cycles.
            for (var cycle = 0; cycle < 3; cycle++)
            {
                await SplashSpinAsync();
                if (DataContext is not MainWindowViewModel { IsStartupLoading: true })
                    break;
                await Task.Delay(400);
            }

            // Phase 3: reveal the wordmark before leaving.
            await SplashRevealBrandAsync();

            // Phase 4: fade out
            await SplashFadeAsync(SplashOverlay, from: 1, to: 0, ms: 260, new CubicEaseIn());
            SplashOverlay.IsVisible        = false;
            SplashOverlay.IsHitTestVisible = false;
        }
        catch (Exception ex)
        {
            Logger.Instance.Exception("Splash", ex);
            SplashOverlay.IsVisible        = false;
            SplashOverlay.IsHitTestVisible = false;
        }
    }

    // Drives one 360° rotation over 1100 ms using a DispatcherTimer.
    private Task SplashSpinAsync()
    {
        const double DurationMs = 1100.0;

        var tcs   = new TaskCompletionSource();
        var sw    = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };

        timer.Tick += (_, _) =>
        {
            double t = sw.ElapsedMilliseconds / DurationMs;
            if (t >= 1.0)
            {
                _splashRotate.Angle = 0; // normalize — 360° == 0° visually
                timer.Stop();
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

    private async Task SplashRevealBrandAsync()
    {
        var logoEase = new CubicEaseInOut();
        var titleEase = new CubicEaseOut();

        var logo = SplashValueAsync(0, -54, 1000, logoEase, v => _splashLogoShift.X = v);
        await Task.Delay(420);

        var titleShift = SplashValueAsync(18, 0, 720, titleEase, v => _splashTitleShift.X = v);
        var titleOpacity = SplashValueAsync(0, 1, 520, titleEase, v => SplashTitleText.Opacity = v);

        await Task.WhenAll(logo, titleShift, titleOpacity);
        await Task.Delay(180);
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
