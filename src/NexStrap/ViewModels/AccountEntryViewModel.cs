using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Models;
using NexStrap.Services;

namespace NexStrap.ViewModels;

public partial class AccountEntryViewModel : ViewModelBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly QuickLoginService _quickLogin;
    private DispatcherTimer? _timer;

    public RobloxAccount Account { get; }

    public Guid    Id            => Account.Id;
    public long    UserId        => Account.UserId;
    public string  Username      => Account.Username;
    public string  DisplayName   => Account.DisplayName;
    public bool    IsActive      => Account.IsActive;
    public int     InstanceIndex { get; }
    public string  InstanceLabel => $"Instance {InstanceIndex + 1}";
    public string  LastUsedText  => FormatRelativeTime(Account.LastUsedAt);

    public CommunityToolkit.Mvvm.Input.IRelayCommand SetActiveCommand { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand RemoveCommand    { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand LaunchAsCommand  { get; }

    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private string? _activeCode;
    [ObservableProperty] private int     _codeSecondsLeft;
    [ObservableProperty] private int     _presenceType;
    [ObservableProperty] private string? _lastLocation;

    public bool   HasActiveCode => ActiveCode != null;
    public string StatusColor   => PresenceType > 0 ? "#4ADE80" : "#888888";
    public bool   IsInGame      => PresenceType == 2;

    partial void OnPresenceTypeChanged(int value)
    {
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(IsInGame));
    }

    public AccountEntryViewModel(RobloxAccount account, int index,
        Action<AccountEntryViewModel> setActive,
        Action<AccountEntryViewModel> remove,
        QuickLoginService quickLogin,
        Action<AccountEntryViewModel> launchAs)
    {
        Account          = account;
        InstanceIndex    = index;
        _quickLogin      = quickLogin;
        SetActiveCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => setActive(this));
        RemoveCommand    = new CommunityToolkit.Mvvm.Input.RelayCommand(() => remove(this));
        LaunchAsCommand  = new CommunityToolkit.Mvvm.Input.RelayCommand(() => launchAs(this));
        if (!string.IsNullOrEmpty(account.AvatarUrl))
            _ = LoadIconAsync(account.AvatarUrl);
    }

    [RelayCommand]
    private void GenerateCode()
    {
        var code = _quickLogin.GenerateCode(Account.Id);
        if (code == null) return;
        ActiveCode       = code;
        CodeSecondsLeft  = (int)_quickLogin.GetRemaining(ActiveCode)!.Value.TotalSeconds;
        OnPropertyChanged(nameof(HasActiveCode));

        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            if (ActiveCode == null) { _timer.Stop(); return; }
            var rem = _quickLogin.GetRemaining(ActiveCode);
            if (rem == null)
            {
                ActiveCode = null;
                OnPropertyChanged(nameof(HasActiveCode));
                _timer.Stop();
                return;
            }
            CodeSecondsLeft = (int)rem.Value.TotalSeconds;
        };
        _timer.Start();
    }

    private async Task LoadIconAsync(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            Icon = new Bitmap(ms);
        }
        catch { }
    }

    private static string FormatRelativeTime(DateTime? dt)
    {
        if (dt == null) return "";
        var diff = DateTime.UtcNow - dt.Value;
        if (diff.TotalMinutes < 1)  return "Just now";
        if (diff.TotalHours   < 1)  return $"{(int)diff.TotalMinutes} min ago";
        if (diff.TotalDays    < 1)  return $"{(int)diff.TotalHours} hr ago";
        if (diff.TotalDays    < 30) return $"{(int)diff.TotalDays} days ago";
        return dt.Value.ToLocalTime().ToString("MMM d");
    }
}
