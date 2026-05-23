using Avalonia;
using Avalonia.Controls;
using NexStrap.ViewModels;

namespace NexStrap.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is SettingsViewModel vm)
            vm.StorageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
    }
}
