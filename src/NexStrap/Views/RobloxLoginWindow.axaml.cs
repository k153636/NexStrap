using System.Runtime.InteropServices;
using Avalonia.Controls;
using Microsoft.Web.WebView2.Core;

namespace NexStrap.Views;

public partial class RobloxLoginWindow : Window
{
    public string? CapturedCookie { get; private set; }

    private CoreWebView2Controller? _controller;
    private bool _cookieCaptured;

    public RobloxLoginWindow()
    {
        InitializeComponent();
        Opened      += OnOpened;
        SizeChanged += OnSizeChanged;
        Closed      += OnWindowClosed;
    }

    private string? _tempUserDataDir;

    private async void OnOpened(object? sender, EventArgs e)
    {
        var hwnd = TryGetPlatformHandle()?.Handle;
        if (hwnd == null || hwnd == IntPtr.Zero) { Close(); return; }

        try
        {
            // 毎回クリーンなセッションにするため一時フォルダを使用
            _tempUserDataDir = Path.Combine(Path.GetTempPath(), "NexStrap_Login_" + Guid.NewGuid().ToString("N"));
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: _tempUserDataDir);
            _controller = await env.CreateCoreWebView2ControllerAsync(hwnd.Value);

            ApplyBounds();
            _controller.IsVisible = true;

            _controller.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _controller.CoreWebView2.Navigate("https://www.roblox.com/login");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            NativeMsgBox(
                "NexStrap requires the Microsoft Edge WebView2 Runtime.\n\n" +
                "Please download and install it, then restart NexStrap:\n" +
                "https://developer.microsoft.com/microsoft-edge/webview2/",
                "WebView2 Runtime Required");
            Close();
        }
        catch { Close(); }
    }

    private void ApplyBounds()
    {
        if (_controller == null) return;
        var scale = VisualRoot?.RenderScaling ?? 1.0;
        var w = (int)(ClientSize.Width  * scale);
        var h = (int)(ClientSize.Height * scale);
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, Math.Max(w, 1), Math.Max(h, 1));
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => ApplyBounds();

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_cookieCaptured || _controller == null) return;
        try
        {
            var cookies = await _controller.CoreWebView2.CookieManager
                .GetCookiesAsync("https://www.roblox.com");
            var target  = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY");
            if (target == null) return;

            _cookieCaptured = true;
            CapturedCookie  = target.Value;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(Close);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private static void NativeMsgBox(string text, string caption) =>
        MessageBox(IntPtr.Zero, text, caption, 0x10); // MB_ICONERROR

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _controller?.Close();
        _controller = null;

        if (_tempUserDataDir != null)
        {
            _ = Task.Run(() =>
            {
                try { Directory.Delete(_tempUserDataDir, recursive: true); } catch { }
            });
            _tempUserDataDir = null;
        }
    }
}
