using Avalonia.Controls;
using NexStrap.ViewModels;

namespace NexStrap.Views.Pages;

public partial class FastFlagsPage : UserControl
{
    public FastFlagsPage()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is FastFlagsViewModel vm)
        {
            vm.OpenBulkImportWindowRequested  += () => OpenWindow(new BulkImportWindow(vm));
            vm.OpenPresetWindowRequested      += () => OpenWindow(new PresetWindow { DataContext = vm });
            vm.OpenAddFlagWindowRequested     += () => OpenWindow(new AddFlagWindow(vm));
            vm.OpenProfileManagerWindowRequested += () => OpenWindow(new ProfileManagerWindow { DataContext = vm });
        }
    }

    private void OpenWindow(Window win)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
            win.ShowDialog(owner);
        else
            win.Show();
    }
}
