namespace NexStrap.Core.Models;

public record FriendInfo(long UserId, string DisplayName);
public record PresenceInfo(long UserId, int UserPresenceType); // 0=Offline 1=Online 2=InGame 3=InStudio
public record FriendPresenceDetail(long UserId, int PresenceType, long? PlaceId, string? LastLocation);
