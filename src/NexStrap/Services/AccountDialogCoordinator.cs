using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NexStrap.ViewModels;
using NexStrap.Views;

namespace NexStrap.Services;

public sealed class AccountDialogCoordinator(QuickSignInViewModelFactory quickSignInFactory)
{
    public async Task ShowQuickSignInAsync(string code, string privateKey, Func<string, Task> importCookieAsync)
    {
        var vm     = quickSignInFactory.Create(code, privateKey);
        var dialog = new QuickSignInDialog { DataContext = vm };
        vm.Completed += async (_, _) =>
        {
            if (vm.ResultCookie != null)
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await importCookieAsync(vm.ResultCookie);
                    dialog.Close();
                });
        };

        var mainWin = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWin != null) await dialog.ShowDialog(mainWin);
        else dialog.Show();
    }

    public async Task<string?> ShowBrowserLoginAsync()
    {
        var mainWindow = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var loginWindow = new RobloxLoginWindow();
        if (mainWindow != null) await loginWindow.ShowDialog(mainWindow);
        else loginWindow.Show();
        return loginWindow.CapturedCookie;
    }
}
