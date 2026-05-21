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
