using Avalonia.Controls;
using Avalonia.Interactivity;
using NexStrap.ViewModels;

namespace NexStrap.Views.Pages;

public partial class ThemePage : UserControl
{
    public ThemePage()
    {
        InitializeComponent();
    }

    private async void PickImageButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not ThemeViewModel vm) return;
        await vm.PickBackgroundImageAsync(topLevel.StorageProvider);
    }
}
