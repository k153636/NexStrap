using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
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
        // Set up transforms: icon starts 220px below center, rotated -360°
        var translate = new TranslateTransform(0, 220);
        var rotate    = new RotateTransform(-360);
        var tg        = new TransformGroup();
        tg.Children.Add(translate);
        tg.Children.Add(rotate);

        SplashIcon.RenderTransform       = tg;
        SplashIcon.RenderTransformOrigin = RelativePoint.Center;
        SplashIcon.Opacity               = 0;
        SplashTextGroup.Opacity          = 0;

        await Task.Delay(80);

        var dur    = TimeSpan.FromMilliseconds(900);
        var easing = new CubicEaseOut();

        // Icon: slide up + rotate + fade in — all simultaneous
        await Task.WhenAll(
            new Animation
            {
                Duration = dur, Easing = easing, FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(TranslateTransform.YProperty, 220d) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(TranslateTransform.YProperty, 0d)   } }
                }
            }.RunAsync(translate),
            new Animation
            {
                Duration = dur, Easing = easing, FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(RotateTransform.AngleProperty, -360d) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(RotateTransform.AngleProperty, 0d)    } }
                }
            }.RunAsync(rotate),
            new Animation
            {
                Duration = TimeSpan.FromMilliseconds(500), Easing = easing, FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 1d) } }
                }
            }.RunAsync(SplashIcon)
        );
        SplashIcon.Opacity = 1;

        // Text: fade in
        await new Animation
        {
            Duration = TimeSpan.FromMilliseconds(350), Easing = easing, FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 1d) } }
            }
        }.RunAsync(SplashTextGroup);
        SplashTextGroup.Opacity = 1;

        // Hold
        await Task.Delay(700);

        // Fade out entire overlay
        await new Animation
        {
            Duration = TimeSpan.FromMilliseconds(520), FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 0d) } }
            }
        }.RunAsync(SplashOverlay);

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
