using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using NexStrap;

namespace NexStrap.Views;

public partial class LaunchWindow : Window
{
    public LaunchWindow()
    {
        InitializeComponent();

        Activated += (_, _) => (Application.Current as App)?.SetBackgroundMode(false);
        Deactivated += (_, _) => (Application.Current as App)?.SetBackgroundMode(true);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        HideToBackground();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Application.Current is App app && !app.IsExiting)
        {
            e.Cancel = true;
            HideToBackground();
            return;
        }

        base.OnClosing(e);
    }

    private void HideToBackground()
    {
        Hide();
        (Application.Current as App)?.SetBackgroundMode(true);
    }
}
