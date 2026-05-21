using Avalonia;
using Avalonia.Controls;
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

    private void WireGlassTheme()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        ApplyGlassTheme(vm.ThemeVM.GlassThemeEnabled);
        vm.ThemeVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ThemeViewModel.GlassThemeEnabled))
                ApplyGlassTheme(vm.ThemeVM.GlassThemeEnabled);
        };
    }

    private static readonly Dictionary<string, (Color glass, Color solid)> _themeColors = new()
    {
        ["CardBg"]     = (Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), Color.Parse("#111111")),
        ["SurfaceBg"]  = (Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF), Color.Parse("#141414")),
        ["ElevatedBg"] = (Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF), Color.Parse("#202020")),
        ["OverlayBg"]  = (Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF), Color.Parse("#1A1A1A")),
        ["InputBg"]    = (Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), Color.Parse("#0D0D0D")),
    };

    private void ApplyGlassTheme(bool glass)
    {
        // UI 全体のカード背景を切り替え
        var res = Application.Current!.Resources;
        foreach (var (key, (glassColor, solidColor)) in _themeColors)
            res[key] = new SolidColorBrush(glass ? glassColor : solidColor);

        // サイドバーペインも半透明化
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
