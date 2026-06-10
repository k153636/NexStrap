using Avalonia.Controls;
using Avalonia.Input;

namespace NexStrap.Views;

public partial class DiagnosticReportDialog : Window
{
    public DiagnosticReportDialog()
    {
        InitializeComponent();
    }

    public DiagnosticReportDialog(string report) : this()
    {
        ReportTextBox.Text = report;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private async void Copy_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await (Clipboard?.SetTextAsync(ReportTextBox.Text) ?? Task.CompletedTask);
        Close();
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
