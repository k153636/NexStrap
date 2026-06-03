using Avalonia.Controls;
using NexStrap.ViewModels.Installer;

namespace NexStrap.Views.Installer;

public partial class InstallerWindow : Window
{
    public InstallerWindow()
    {
        InitializeComponent();
        var vm = new InstallerViewModel();
        vm.StorageProvider = StorageProvider;
        vm.CloseAction = Close;
        DataContext = vm;
    }
}
