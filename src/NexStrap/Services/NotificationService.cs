using System.Runtime.InteropServices;

namespace NexStrap.Services;

/// <summary>友達オンライン通知 — WinRT 不要の実装</summary>
internal static class NotificationService
{
    public static void ShowFriendOnline(string displayName)
    {
        // システムトレイのバルーン通知は NotifyIcon が必要で Avalonia では複雑なため
        // 現在は NexStrap の Friends ページ内でリアルタイム表示するのみ
        // 将来的に Avalonia ネイティブ通知 API が安定したら移行予定
        _ = displayName;
    }
}
