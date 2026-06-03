using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NexStrap.Views;

public partial class ProfileManagerWindow : Window
{
    public ProfileManagerWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
