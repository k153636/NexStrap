using Avalonia.Controls;
using Avalonia.Input;
using NexStrap.ViewModels;

namespace NexStrap.Views.Pages;

public partial class ShortcutsPage : UserControl
{
    private EventHandler<KeyEventArgs>? _keyHandler;

    public ShortcutsPage()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => WireViewModel();
    }

    private void WireViewModel()
    {
        if (DataContext is not ShortcutsViewModel vm) return;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ShortcutsViewModel.IsRecording)) return;

            if (vm.IsRecording)
                AttachKeyCapture(vm);
            else
                DetachKeyCapture();
        };
    }

    private void AttachKeyCapture(ShortcutsViewModel vm)
    {
        var window = TopLevel.GetTopLevel(this);
        if (window == null) return;

        _keyHandler = (_, e) =>
        {
            var key = e.Key;

            // ignore modifier-only presses
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                     or Key.LeftAlt  or Key.RightAlt  or Key.LWin     or Key.RWin
                     or Key.System)
                return;

            e.Handled = true;

            if (key == Key.Escape)
            {
                vm.IsRecording = false;
                DetachKeyCapture();
                return;
            }

            vm.CommitRecording(e.KeyModifiers, key);
            DetachKeyCapture();
        };

        window.KeyDown += _keyHandler;
    }

    private void DetachKeyCapture()
    {
        if (_keyHandler == null) return;
        var window = TopLevel.GetTopLevel(this);
        if (window != null) window.KeyDown -= _keyHandler;
        _keyHandler = null;
    }
}
