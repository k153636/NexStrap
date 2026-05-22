using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using NexStrap.ViewModels;
using System.Collections.Generic;

namespace NexStrap.Views;

public partial class MainWindow : Window
{
    private bool                    _isMinimizing;
    private bool                    _hasOpened;
    private CancellationTokenSource? _animCts;
    private ScaleTransform          _scale     = new(1, 1);
    private TranslateTransform      _translate = new(0, 0);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public MainWindow()
    {
        InitializeComponent();

        if (NavView.MenuItems.FirstOrDefault() is NavigationViewItem first)
            NavView.SelectedItem = first;

        DataContextChanged += (_, _) => WireGlassTheme();
        KeyDown += OnKeyDown;

        // TransformGroup: Scale（中心基準）→ Translate（Y方向移動）
        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_translate);
        RootGrid.RenderTransform       = group;
        RootGrid.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);

        // 初期状態を不可視にしてウィンドウ表示直後のフラッシュを防ぐ
        RootGrid.Opacity = 0;

        Opened += (_, _) =>
        {
            _hasOpened = true;
            DisableDwmTransitions();
            StartShowAnimation();
        };

        NavView.TemplateApplied += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                ApplyGlassTheme(vm.ThemeVM.GlassThemeEnabled);
        };

        Loaded += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                ApplyGlassTheme(vm.ThemeVM.GlassThemeEnabled);
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // トレイから復元時（Hide→Show）のアニメーション
        if (change.Property == IsVisibleProperty)
        {
            if (_hasOpened && change.NewValue is true && change.OldValue is false)
                StartShowAnimation();
            return;
        }

        if (change.Property != WindowStateProperty) return;

        var newState = (WindowState)change.NewValue!;
        var oldState = change.OldValue is WindowState s ? s : WindowState.Normal;

        if (newState == WindowState.Minimized && !_isMinimizing)
        {
            // _isMinimizingを先に立ててから WindowState を戻す
            // ← これをしないと再帰的に else-if が発火して ShowAnimation が走る
            _isMinimizing = true;
            WindowState = oldState;
            _ = AnimateAndMinimize();
        }
        else if (oldState == WindowState.Minimized && newState != WindowState.Minimized && !_isMinimizing)
        {
            StartShowAnimation();
        }
    }

    private void DisableDwmTransitions()
    {
        try
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero) { int v = 1; DwmSetWindowAttribute(hwnd, 3, ref v, sizeof(int)); }
        }
        catch { }
    }

    private void StartShowAnimation()
    {
        DisableDwmTransitions();
        _animCts?.Cancel();
        _animCts = new CancellationTokenSource();
        SetAnimState(false);
        _ = RunAnimation(true, _animCts.Token);
    }

    private async Task AnimateAndMinimize()
    {
        // _isMinimizing はすでに true（OnPropertyChanged で設定済み）
        try
        {
            _animCts?.Cancel();
            _animCts = new CancellationTokenSource();
            var ct = _animCts.Token;
            await RunAnimation(false, ct);
            if (!ct.IsCancellationRequested)
                WindowState = WindowState.Minimized;
        }
        finally
        {
            _isMinimizing = false;
        }
    }

    // RenderTransformOrigin=(0,0) のとき、Scale で中心を維持するには
    // tx=(1-s)*w/2, ty=(1-s)*h/2 のオフセットが必要
    private void SetAnimState(bool visible)
    {
        const double scaleMin = 0.42;
        // 最小化直後は Bounds が 0 になることがあるので XAML の Width/Height をフォールバックに使う
        var w  = Bounds.Width  > 0 ? Bounds.Width  : Width;
        var h  = Bounds.Height > 0 ? Bounds.Height : Height;
        var ty = CalcMinimizeY();
        if (visible)
        {
            RootGrid.Opacity = 1; _scale.ScaleX = _scale.ScaleY = 1;
            _translate.X = _translate.Y = 0;
        }
        else
        {
            RootGrid.Opacity = 0; _scale.ScaleX = _scale.ScaleY = scaleMin;
            _translate.X = (1.0 - scaleMin) * w / 2.0;
            _translate.Y = (1.0 - scaleMin) * h / 2.0 + ty;
        }
    }

    private double CalcMinimizeY()
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this);
            if (screen == null) return 220;
            var sc            = screen.Scaling;
            var winBottomDip  = Position.Y / sc + Height;
            var workBottomDip = (screen.WorkingArea.Y + screen.WorkingArea.Height) / sc;
            return Math.Max(80, workBottomDip - winBottomDip + 50);
        }
        catch { return 220; }
    }

    private async Task RunAnimation(bool fadeIn, CancellationToken ct = default)
    {
        const int    durationMs = 260;
        const double scaleMin   = 0.42;
        var          targetY    = CalcMinimizeY();
        var          sw         = Stopwatch.StartNew();

        while (true)
        {
            if (ct.IsCancellationRequested) return;

            double t    = Math.Min(sw.Elapsed.TotalMilliseconds / durationMs, 1.0);
            double ease = fadeIn
                ? 1.0 - Math.Pow(1.0 - t, 3.0)   // cubic ease-out（速→遅）
                : t * t * t;                        // cubic ease-in（遅→速）

            double s  = scaleMin + (1.0 - scaleMin) * (fadeIn ? ease : 1.0 - ease);
            // フレームごとに Bounds を読み直す（最小化復元直後は 0 になる場合がある）
            double w  = Bounds.Width  > 0 ? Bounds.Width  : Width;
            double h  = Bounds.Height > 0 ? Bounds.Height : Height;
            double tx = (1.0 - s) * w / 2.0;
            double ty = (1.0 - s) * h / 2.0 + targetY * (fadeIn ? 1.0 - ease : ease);
            double op = fadeIn ? ease : 1.0 - ease;

            RootGrid.Opacity = op;
            _scale.ScaleX    = _scale.ScaleY = s;
            _translate.X     = tx;
            _translate.Y     = ty;

            if (t >= 1.0) break;
            await Task.Delay(11);
        }

        if (ct.IsCancellationRequested) return;

        if (fadeIn)
        {
            RootGrid.Opacity = 1; _scale.ScaleX = _scale.ScaleY = 1;
            _translate.X = _translate.Y = 0;
        }
        else
        {
            double w = Bounds.Width  > 0 ? Bounds.Width  : Width;
            double h = Bounds.Height > 0 ? Bounds.Height : Height;
            RootGrid.Opacity = 0; _scale.ScaleX = _scale.ScaleY = scaleMin;
            _translate.X = (1.0 - scaleMin) * w / 2.0;
            _translate.Y = (1.0 - scaleMin) * h / 2.0 + targetY;
        }
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
                e.PropertyName == nameof(ThemeViewModel.GlassAccentColor))
                ApplyGlassTheme(vm.ThemeVM.GlassThemeEnabled);
        };
    }

    private static readonly Dictionary<string, Color> _solidColors = new()
    {
        ["CardBg"]     = Color.Parse("#111111"),
        ["SurfaceBg"]  = Color.Parse("#141414"),
        ["ElevatedBg"] = Color.Parse("#202020"),
        ["OverlayBg"]  = Color.Parse("#1A1A1A"),
        ["InputBg"]    = Color.Parse("#0D0D0D"),
    };

    private static readonly Dictionary<string, byte> _glassAlphas = new()
    {
        ["CardBg"]     = 0x18,
        ["SurfaceBg"]  = 0x20,
        ["ElevatedBg"] = 0x2A,
        ["OverlayBg"]  = 0x24,
        ["InputBg"]    = 0x10,
    };

    private void ApplyGlassTheme(bool glass)
    {
        var res = Application.Current!.Resources;

        if (glass)
        {
            var accentHex = (DataContext as MainWindowViewModel)?.ThemeVM.GlassAccentColor ?? "#FFFFFF";
            Color accent;
            try { accent = Color.Parse(accentHex); }
            catch { accent = Colors.White; }
            byte r = accent.R, g = accent.G, b = accent.B;

            foreach (var (key, alpha) in _glassAlphas)
                res[key] = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
        }
        else
        {
            foreach (var (key, color) in _solidColors)
                res[key] = new SolidColorBrush(color);
        }

        var paneBrush = glass
            ? new SolidColorBrush(Color.FromArgb(0x55, 0x0D, 0x0D, 0x0D))
            : new SolidColorBrush(Color.FromArgb(0xFF, 0x14, 0x14, 0x14));

        var splitView = NavView.GetVisualDescendants().OfType<SplitView>().FirstOrDefault();
        if (splitView != null)
            splitView.PaneBackground = paneBrush;
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

        if (DataContext is MainWindowViewModel vm)
            vm.NavigateToCommand.Execute(tag);
    }
}
