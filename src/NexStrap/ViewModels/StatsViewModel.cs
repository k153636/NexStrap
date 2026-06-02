using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexStrap.Core.Models;
using NexStrap.Core.Services;
using System.Collections.ObjectModel;

namespace NexStrap.ViewModels;

public class DailyBar
{
    public required string DayLabel { get; init; }
    public required string TimeText { get; init; }
    public required double Ratio    { get; init; }
    public required bool   IsToday  { get; init; }
    public double BarHeight => Ratio > 0 ? Math.Max(4, Ratio * 64) : 0;
}

public partial class GameStat : ViewModelBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public string   Name         { get; }
    public string?  IconUrl      { get; }
    public int      Sessions     { get; }
    public int      TotalSeconds { get; }
    public DateTime LastPlayed   { get; }

    [ObservableProperty] private Bitmap? _icon;

    public string TotalTimeText  => FormatSeconds(TotalSeconds);
    public string LastPlayedText => LastPlayed.ToString("MM/dd HH:mm");

    public GameStat(string name, string? iconUrl, int sessions, int totalSeconds, DateTime lastPlayed)
    {
        Name         = name;
        IconUrl      = iconUrl;
        Sessions     = sessions;
        TotalSeconds = totalSeconds;
        LastPlayed   = lastPlayed;

        if (!string.IsNullOrEmpty(iconUrl))
            _ = LoadIconAsync(iconUrl);
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

    private static string FormatSeconds(int s)
    {
        if (s <= 0) return "--";
        var h = s / 3600;
        var m = (s % 3600) / 60;
        if (h == 0 && m == 0) return "1m";
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }
}

public enum StatsSortMode { TotalTime, Sessions, LastPlayed, Name }

public partial class StatsViewModel : ViewModelBase
{
    private readonly GameHistoryService _history;

    [ObservableProperty] private string        _totalPlayTime = "--";
    [ObservableProperty] private string        _todayPlayTime = "--";
    [ObservableProperty] private int           _totalSessions;
    [ObservableProperty] private string        _topGameName   = "--";
    [ObservableProperty] private string        _topGameTime   = "--";
    [ObservableProperty] private StatsSortMode _sortMode      = StatsSortMode.TotalTime;
    [ObservableProperty] private bool          _isSortMenuOpen;

    public ObservableCollection<GameStat> GameStats  { get; } = [];
    public ObservableCollection<DailyBar> DailyBars  { get; } = [];

    public StatsViewModel(GameHistoryService history)
    {
        _history = history;
        Refresh();
    }

    public bool   IsSortTotalTime  => SortMode == StatsSortMode.TotalTime;
    public bool   IsSortLastPlayed => SortMode == StatsSortMode.LastPlayed;
    public string SortLabel        => SortMode == StatsSortMode.LastPlayed ? "Recent First" : "Most Played";

    partial void OnSortModeChanged(StatsSortMode value)
    {
        OnPropertyChanged(nameof(IsSortTotalTime));
        OnPropertyChanged(nameof(IsSortLastPlayed));
        OnPropertyChanged(nameof(SortLabel));
        Refresh();
    }

    [RelayCommand] private void ToggleSortMenu() => IsSortMenuOpen = !IsSortMenuOpen;

    [RelayCommand]
    private void SetSort(StatsSortMode mode)
    {
        SortMode       = mode;
        IsSortMenuOpen = false;
    }

    public void Refresh()
    {
        var entries = _history.Entries;

        TotalSessions = entries.Count;
        TotalPlayTime = FormatSeconds(entries.Sum(e => e.DurationSeconds));

        // 今日のプレイ時間（その日のセッションの DurationSeconds 合計）
        var todaySec = entries
            .Where(e => e.PlayedAt.Date == DateTime.Today)
            .Sum(e => e.DurationSeconds);
        TodayPlayTime = todaySec > 0 ? FormatSeconds(todaySec) : "--";

        // UniverseId 優先でグルーピング（同じゲームのサブプレイスを統合）
        // UniverseId = 0 の古いエントリは PlaceId でグルーピング
        var aggregated = entries
            .GroupBy(e => e.UniverseId != 0 ? e.UniverseId : e.PlaceId)
            .Select(g =>
            {
                // 最も再生数の多い名前・アイコンを代表として使用
                var best = g.GroupBy(e => e.Name)
                            .OrderByDescending(n => n.Count())
                            .First().First();
                return new GameStat(
                    best.Name,
                    best.IconUrl,
                    g.Count(),
                    g.Sum(e => e.DurationSeconds),
                    g.Max(e => e.PlayedAt));
            });

        var grouped = (SortMode switch
        {
            StatsSortMode.Sessions   => aggregated.OrderByDescending(g => g.Sessions),
            StatsSortMode.LastPlayed => aggregated.OrderByDescending(g => g.LastPlayed),
            StatsSortMode.Name       => aggregated.OrderBy(g => g.Name),
            _                        => aggregated.OrderByDescending(g => g.TotalSeconds),
        }).ToList();

        GameStats.Clear();
        foreach (var s in grouped)
            GameStats.Add(s);

        var top = grouped.FirstOrDefault();
        TopGameName = top?.Name ?? "--";
        TopGameTime = top?.TotalTimeText ?? "--";

        // Build last-7-day bar chart data
        var today = DateTime.Today;
        var last7 = Enumerable.Range(0, 7).Select(i => today.AddDays(-6 + i)).ToList();
        var byDay = entries
            .Where(e => e.PlayedAt.Date >= today.AddDays(-6))
            .GroupBy(e => e.PlayedAt.Date)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.DurationSeconds));
        var maxSec = last7.Select(d => byDay.GetValueOrDefault(d, 0)).DefaultIfEmpty(0).Max();

        DailyBars.Clear();
        foreach (var d in last7)
        {
            var sec   = byDay.GetValueOrDefault(d, 0);
            var ratio = maxSec > 0 ? (double)sec / maxSec : 0;
            DailyBars.Add(new DailyBar
            {
                DayLabel = d.ToString("M/d"),
                TimeText = sec > 0 ? FormatSeconds(sec) : "",
                Ratio    = ratio,
                IsToday  = d == today,
            });
        }
    }

    private static string FormatSeconds(int s)
    {
        if (s <= 0) return "--";
        var h = s / 3600;
        var m = (s % 3600) / 60;
        if (h == 0 && m == 0) return "1m";
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }
}
