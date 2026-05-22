using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NexStrap.Views.Pages;

public partial class FriendsPage : UserControl
{
    public FriendsPage()
    {
        InitializeComponent();
    }

    private void JoinButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is long placeId && placeId > 0)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = $"roblox://experiences/start?placeId={placeId}",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
