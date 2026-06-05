using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Services;

namespace NexStrap.ViewModels;

public partial class QuickSignInViewModel : ViewModelBase
{
    public string  Code         { get; }
    public string? ResultCookie { get; private set; }

    public event EventHandler? Completed;

    [ObservableProperty] private string _statusText  = "Created — Ready for sign-in";
    [ObservableProperty] private string _statusColor = "#60A5FA";
    [ObservableProperty] private bool   _isFinished;

    private readonly string           _privateKey;
    private readonly RobloxApiService _api;
    private readonly CancellationTokenSource _cts = new();

    private string _xsrf = string.Empty;

    public QuickSignInViewModel(string code, string privateKey, RobloxApiService api)
    {
        Code        = code;
        _privateKey = privateKey;
        _api        = api;
        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        // 1回目: XSRF トークン取得（意図的に失敗させる）
        var (xsrf, _) = await _api.PollQuickSignInFirstAsync(Code, _privateKey);
        _xsrf = xsrf;

        while (!_cts.Token.IsCancellationRequested)
        {
            try { await Task.Delay(3000, _cts.Token); }
            catch (OperationCanceledException) { return; }

            var status = await _api.PollQuickSignInStatusAsync(Code, _privateKey, _xsrf);

            (StatusText, StatusColor) = status switch
            {
                "UserLinked" => ("User linked — Awaiting approval", "#FBBF24"),
                "Validated"  => ("Validated — Signing in...",       "#4ADE80"),
                "Cancelled"  => ("Cancelled",                       "#F87171"),
                _            => (StatusText, StatusColor)
            };

            if (status == "Validated")
            {
                ResultCookie = await _api.AuthenticateWithQuickSignInAsync(Code, _privateKey);
                IsFinished   = true;
                Completed?.Invoke(this, EventArgs.Empty);
                _cts.Cancel();
            }
            else if (status is "Cancelled" or "Error")
            {
                IsFinished = true;
                _cts.Cancel();
            }
        }
    }

    [RelayCommand]
    public void Cancel() => _cts.Cancel();
}
