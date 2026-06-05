namespace NexStrap.Core.Models;

public class RobloxAccount
{
    public Guid    Id              { get; set; } = Guid.NewGuid();
    public long    UserId          { get; set; }
    public string  Username        { get; set; } = string.Empty;
    public string  DisplayName     { get; set; } = string.Empty;
    public string? AvatarUrl       { get; set; }
    public string  EncryptedCookie { get; set; } = string.Empty;
    public bool      IsActive        { get; set; }
    public DateTime? LastUsedAt      { get; set; }
}
