using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
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
            _ = PlaySplashAsync();
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

    private async Task PlaySplashAsync()
    {
        const int RotateDurationMs = 1200; // long so deceleration is visible
        const int SlideDurationMs  = 900;

        // Initial state
        var rotate = new RotateTransform(-360);
        SplashIcon.RenderTransform       = rotate;
        SplashIcon.RenderTransformOrigin = RelativePoint.Center;
        SplashIcon.Opacity               = 0;
        SplashTextGroup.Opacity          = 0;
        SplashContentPanel.Margin        = new Thickness(0, 220, 0, 0);

        // Y slide
        SplashContentPanel.Transitions = new Transitions
        {
            new ThicknessTransition
            {
                Property = MarginProperty,
                Duration = TimeSpan.FromMilliseconds(SlideDurationMs),
                Easing   = new CubicEaseOut()
            }
        };

        // Icon opacity
        SplashIcon.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(480),
                Easing   = new CubicEaseOut()
            }
        };

        await Task.Delay(80);

        // Trigger slide + fade-in
        SplashContentPanel.Margin = new Thickness(0);
        SplashIcon.Opacity        = 1;

        // Rotation: QuarticEaseOut — very fast start, nearly stopped by 70% of duration
        // At 840ms (70%): 99.2% done → angle ≈ -3°, visually stopped
        var tcs       = new TaskCompletionSource();
        var startTick = Environment.TickCount64;
        var timer     = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t     = Math.Min(1.0, (Environment.TickCount64 - startTick) / (double)RotateDurationMs);
            var eased = 1.0 - Math.Pow(1.0 - t, 4); // QuarticEaseOut
            rotate.Angle = -360.0 * (1.0 - eased);
            if (t >= 1.0) { rotate.Angle = 0; timer.Stop(); tcs.TrySetResult(); }
        };
        timer.Start();

        // Text appears as rotation is nearly stopped (~800ms)
        await Task.Delay(800);
        SplashTextGroup.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(380),
                Easing   = new CubicEaseOut()
            }
        };
        SplashTextGroup.Opacity = 1;

        // Wait for rotation to fully stop, then fade out immediately (no hold)
        await tcs.Task;

        SplashOverlay.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(550)
            }
        };
        SplashOverlay.Opacity = 0;
        await Task.Delay(600);

        SplashOverlay.IsVisible        = false;
        SplashOverlay.IsHitTestVisible = false;
    }

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
