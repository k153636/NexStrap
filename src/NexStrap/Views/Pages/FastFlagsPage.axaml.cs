using Avalonia.Controls;
using NexStrap.ViewModels;

namespace NexStrap.Views.Pages;

public partial class FastFlagsPage : UserControl
{
    private FastFlagsViewModel? _vm;

    public FastFlagsPage()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm != null)
        {
            _vm.OpenBulkImportWindowRequested    -= OpenBulkImport;
            _vm.OpenPresetWindowRequested        -= OpenPreset;
            _vm.OpenAddFlagWindowRequested       -= OpenAddFlag;
            _vm.OpenProfileManagerWindowRequested -= OpenProfileMgr;
        }

        _vm = DataContext as FastFlagsViewModel;

        if (_vm != null)
        {
            _vm.OpenBulkImportWindowRequested    += OpenBulkImport;
            _vm.OpenPresetWindowRequested        += OpenPreset;
            _vm.OpenAddFlagWindowRequested       += OpenAddFlag;
            _vm.OpenProfileManagerWindowRequested += OpenProfileMgr;
        }
    }

    private void OpenBulkImport()  => OpenWindow(new BulkImportWindow(_vm!));
    private void OpenPreset()      => OpenWindow(new PresetWindow { DataContext = _vm });
    private void OpenAddFlag()     => OpenWindow(new AddFlagWindow(_vm!));
    private void OpenProfileMgr()  => OpenWindow(new ProfileManagerWindow { DataContext = _vm });

    private void OpenWindow(Window win)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
            win.ShowDialog(owner);
        else
            win.Show();
    }
}
