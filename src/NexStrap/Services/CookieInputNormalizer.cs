namespace NexStrap.Services;

public sealed class CookieInputNormalizer
{
    public string StripRobloSecurityPrefix(string raw)
    {
        const string prefix = ".ROBLOSECURITY=";
        if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            raw = raw[prefix.Length..].Trim();
        return raw;
    }
}
