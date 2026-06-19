using Avalonia.Controls;
using Avalonia.Input;
using NexStrap.ViewModels;

namespace NexStrap.Views;

public partial class LaunchWindow : Window
{
    public LaunchWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
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

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is LaunchWindowViewModel vm)
            vm.SetTemporaryDetails(null);
    }
}
