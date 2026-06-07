using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NexStrap.Views.Pages;

public partial class DevPage : UserControl
{
    public DevPage()
    {
        InitializeComponent();
    }

    private void PreviewSplash_Click(object? sender, RoutedEventArgs e)
    {
        var splash = new NexStrap.Views.SplashWindow { IsTestMode = true };
        splash.Show();
    }
}
