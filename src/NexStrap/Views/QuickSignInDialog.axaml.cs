using Avalonia.Controls;
using Avalonia.Input;
using NexStrap.ViewModels;

namespace NexStrap.Views;

public partial class QuickSignInDialog : Window
{
    public QuickSignInDialog()
    {
        InitializeComponent();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private async void CopyCode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is QuickSignInViewModel vm)
            await (Clipboard?.SetTextAsync(vm.Code) ?? Task.CompletedTask);
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is QuickSignInViewModel vm)
            vm.Cancel();
        Close();
    }
}
