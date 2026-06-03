using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NexStrap.Views;

public partial class PresetWindow : Window
{
    public PresetWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
