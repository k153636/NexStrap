using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

    // Non-glass gradient triples (top, mid @45%, bottom)
    private static readonly Dictionary<string, (Color Top, Color Mid, Color Bot)> _solidGradients = new()
    {
        ["CardBg"]     = (Color.Parse("#1E1E1E"), Color.Parse("#141414"), Color.Parse("#0C0C0C")),
        ["SurfaceBg"]  = (Color.Parse("#202020"), Color.Parse("#161616"), Color.Parse("#0E0E0E")),
        ["ElevatedBg"] = (Color.Parse("#2A2A2A"), Color.Parse("#1E1E1E"), Color.Parse("#181818")),
        ["OverlayBg"]  = (Color.Parse("#222222"), Color.Parse("#181818"), Color.Parse("#121212")),
        ["InputBg"]    = (Color.Parse("#151515"), Color.Parse("#0F0F0F"), Color.Parse("#080808")),
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

        if (glass)
        {
            var accentHex = themeVm?.GlassAccentColor ?? "#FFFFFF";
            Color accent;
            try { accent = Color.Parse(accentHex); }
            catch { accent = Colors.White; }
            byte r = accent.R, g = accent.G, b = accent.B;

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
        }
        else
        {
            foreach (var (key, (top, mid, bot)) in _solidGradients)
                res[key] = new SolidColorBrush(mid);
        }

        // Sidebar pane: same gradient direction as cards
        IBrush paneBrush;
        if (glass)
        {
            var topA = (byte)Math.Round(0x88 + (0xE8 - 0x88) * t);
            var botA = (byte)Math.Round(0x78 + (0xD4 - 0x78) * t);
            paneBrush = MakeGradient(
                Color.FromArgb(topA, 0x14, 0x14, 0x14),
                Color.FromArgb(botA, 0x08, 0x08, 0x08));
        }
        else
        {
            paneBrush = MakeGradient(Color.Parse("#1C1C1C"), Color.Parse("#111111"));
        }

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
