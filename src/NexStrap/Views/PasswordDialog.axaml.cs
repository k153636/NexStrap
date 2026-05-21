using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;

namespace NexStrap.Views;

public partial class PasswordDialog : Window
{
    // SHA256("K12")
    private const string CorrectHash = "f4b6aa79b2f4df0b44d3ab9060a0d0b54cfd783ecbed7579432c901e9e814f26";

    public bool Authenticated { get; private set; }

    public PasswordDialog()
    {
        InitializeComponent();

        PasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) TryAuth();
            if (e.Key == Key.Escape) Close();
        };

        OkButton.Click     += (_, _) => TryAuth();
        CancelButton.Click += (_, _) => Close();

        Opened += (_, _) => PasswordBox.Focus();
    }

    private void TryAuth()
    {
        var input = PasswordBox.Text ?? string.Empty;
        var hash  = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLower();
        if (hash == CorrectHash)
        {
            Authenticated = true;
            Close();
        }
        else
        {
            PasswordBox.Text = string.Empty;
            PasswordBox.Focus();
        }
    }
}
