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
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
    }
}
