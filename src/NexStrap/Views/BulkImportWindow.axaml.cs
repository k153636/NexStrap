using Avalonia.Controls;
using Avalonia.Interactivity;
using NexStrap.ViewModels;

namespace NexStrap.Views;

public partial class BulkImportWindow : Window
{
    public BulkImportWindow(FastFlagsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.BulkImportCompleted += OnBulkImportCompleted;
        Closed += (_, _) => vm.BulkImportCompleted -= OnBulkImportCompleted;
    }

    private void OnBulkImportCompleted() => Close();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
