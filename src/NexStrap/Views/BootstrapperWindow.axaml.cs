using Avalonia.Controls;
using NexStrap.ViewModels;

namespace NexStrap.Views;

public partial class BootstrapperWindow : Window
{
    public BootstrapperWindow()
    {
        InitializeComponent();
    }

    public BootstrapperWindow(BootstrapperViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += (_, _) => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(Close);
        Closed += (_, _) => vm.Detach();
    }
}
