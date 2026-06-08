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
using Avalonia.Media.Transformation;
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

    private RotateTransform _splashRotate = null!;

    private static readonly Logger Log = Logger.Instance;
    private const string Cat = "Splash";

    private void StartSplashOverlay()
    {
        Log.Info(Cat, "StartSplashOverlay called");
        // Rotate the wrapper Border (not the Image directly).
        // RenderTransformOrigin="0.5,0.5" is set in XAML on SplashLogoWrapper
        // and is never touched from code, so Avalonia applies T(29,29)*R*T(-29,-29).
        _splashRotate = new RotateTransform(0);
        SplashLogoWrapper.RenderTransform = _splashRotate;
        _ = PlaySplashAsync();
    }

    private async Task PlaySplashAsync()
    {
        try
        {
            SplashContent.Opacity = 0;

            // Opened fires during Show(), before Loaded — already past by here.
            // Post at Render priority so the black overlay reaches the screen
            // before the fade-in begins.
            Log.Info(Cat, "Waiting for render frame");
            var frameTcs = new TaskCompletionSource();
            Dispatcher.UIThread.Post(() => frameTcs.TrySetResult(), DispatcherPriority.Render);
            await frameTcs.Task;
            Log.Info(Cat, "Render frame ready — starting fade-in");

            await RunOpacityAsync(SplashContent, 0d, 1d, 280, new CubicEaseOut());
            SplashContent.Opacity = 1;
            Log.Info(Cat, "Fade-in complete — starting spin (1500ms)");

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
            await SplashSpinLoopAsync(cts.Token);
            Log.Info(Cat, "Spin complete — starting fade-out");

            await RunOpacityAsync(SplashOverlay, 1d, 0d, 260, new CubicEaseIn());
            SplashOverlay.IsVisible        = false;
            SplashOverlay.IsHitTestVisible = false;
            Log.Info(Cat, "Splash done — overlay hidden");
        }
        catch (Exception ex)
        {
            Log.Exception(Cat, ex);
            SplashOverlay.IsVisible        = false;
            SplashOverlay.IsHitTestVisible = false;
        }
    }

    private async Task SplashSpinLoopAsync(CancellationToken ct)
    {
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
                _splashRotate.Angle = SplashSpline(elapsed / tSpin) * 360.0;
            };
            timer.Start();

            if (await tcs.Task) break;
            _splashRotate.Angle = 0;

            try { await Task.Delay(tHold, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static double SplashSpline(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        double s = t;
        for (int i = 0; i < 8; i++)
        {
            double dx = Bz(s, 0.2, 0.4) - t;
            if (Math.Abs(dx) < 1e-6) break;
            double d = BzD(s, 0.2, 0.4);
            if (Math.Abs(d)  < 1e-6) break;
            s -= dx / d;
        }
        return Bz(s, 0.8, 1.0);
    }
    private static double Bz (double t, double c1, double c2) => 3*c1*t*(1-t)*(1-t) + 3*c2*t*t*(1-t) + t*t*t;
    private static double BzD(double t, double c1, double c2) => 3*c1*(1-t)*(1-2*t) + 3*c2*t*(2-3*t) + 3*t*t;

    private static Task RunOpacityAsync(Animatable target, double from, double to, int ms, Easing easing)
    {
        var anim = new Animation
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
