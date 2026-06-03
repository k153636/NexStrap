using Avalonia.Controls;
using Avalonia.Interactivity;
using NexStrap.ViewModels;

namespace NexStrap.Views;

public partial class AddFlagWindow : Window
{
    public AddFlagWindow(FastFlagsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.FlagAdded += OnFlagAdded;
        Closed += (_, _) => vm.FlagAdded -= OnFlagAdded;
    }

    private void OnFlagAdded() => Close();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
