using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace NexStrap.Services;

internal static class NotificationService
{
    public static void ShowFriendOnline(string displayName)
    {
        try
        {
            var safeName = System.Security.SecurityElement.Escape(displayName);
            var xml = $"""
                <toast>
                    <visual>
                        <binding template="ToastGeneric">
                            <text>フレンドがオンライン</text>
                            <text>{safeName} が Roblox を起動しました</text>
                        </binding>
                    </visual>
                </toast>
                """;
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var toast = new ToastNotification(doc);
            ToastNotificationManager.CreateToastNotifier(JumpListService.AppId).Show(toast);
        }
        catch { }
    }
}
