using NexStrap.Services;

namespace NexStrap.Modules.Roblox.Protocol;

public sealed class RobloxProtocolLaunchHandler(
    RobloxService roblox,
    RobloxApiService robloxApi,
    AccountService accounts,
    FastFlagService fastFlags,
    SettingsService settings,
    ModService mods)
{
    public bool TryGetRobloxUrl(string[] args, out string robloxUrl)
    {
        robloxUrl = args.Skip(1).FirstOrDefault(a =>
            a.StartsWith("roblox://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("roblox-player://", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        return robloxUrl.Length > 0;
    }

    public bool TryGetJumpLaunch(string[] args, out long placeId)
    {
        placeId = 0;
        var idx = Array.IndexOf(args, "--launch-game");
        return idx >= 0 && idx + 1 < args.Length && long.TryParse(args[idx + 1], out placeId);
    }

    public async Task HandleJumpLaunchAsync(long placeId)
    {
        try
        {
            ApplyPerformanceFlags();
            await fastFlags.SaveAsync();
            await mods.ApplyEnabledModsAsync();

            await BloxstrapLaunchAsync(placeId);
        }
        catch (Exception ex)
        {
            RobloxService.Log($"HandleJumpLaunchAsync: {ex.Message}");
        }
    }

    public async Task HandleRobloxUrlLaunchAsync(string url)
    {
        try
        {
            RobloxService.Log($"Web launch: {url}");

            ApplyPerformanceFlags();
            await fastFlags.SaveAsync();
            await mods.ApplyEnabledModsAsync();

            var (placeId, gameId, accessCode) = ParseRobloxUrl(url);
            if (placeId > 0)
                await BloxstrapLaunchAsync(placeId, gameId, accessCode);
            else
                RobloxService.Log($"Could not extract placeId from: {url}");
        }
        catch (Exception ex)
        {
            RobloxService.Log($"HandleRobloxUrlLaunchAsync failed: {ex.Message}");
        }
    }

    private void ApplyPerformanceFlags()
        => fastFlags.ApplyPerformanceSettings(settings.Settings);

    private async Task BloxstrapLaunchAsync(long placeId, string? gameId = null, string? accessCode = null)
    {
        var playerPath = roblox.RobloxPlayerPath;
        if (playerPath == null)
        {
            RobloxService.Log("Player not found");
            return;
        }

        var workDir = Path.GetDirectoryName(playerPath)!;
        var cookie = accounts.GetActiveCookie();

        if (cookie != null)
        {
            var (joinScriptUrl, authTicket) =
                await robloxApi.GetJoinInfoAsync(cookie, placeId, gameId, accessCode);

            if (!string.IsNullOrEmpty(joinScriptUrl) && !string.IsNullOrEmpty(authTicket))
            {
                var jArgs = $"--joinscript \"{joinScriptUrl}\" " +
                            $"--authenticationTicket {authTicket} " +
                            $"--authenticationUrl \"https://auth.roblox.com\" " +
                            $"--joinAttemptId {Guid.NewGuid()} " +
                            $"--joinAttemptOrigin ExperiencesListAndGrid " +
                            $"--launchMode play";
                RobloxService.Log($"Bloxstrap launch: placeId={placeId} gameId={gameId}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(playerPath)
                    { UseShellExecute = false, WorkingDirectory = workDir, Arguments = jArgs });
                return;
            }
        }

        string fbArgs;
        if (cookie != null)
        {
            RobloxService.Log($"join API failed, fallback with auth: placeId={placeId}");
            var ticket = await robloxApi.GetAuthTicketAsync(cookie);
            fbArgs = ticket != null
                ? $"--launchMode play --placeId {placeId} --authenticationTicket {ticket} --authenticationUrl https://auth.roblox.com"
                : $"--launchMode play --placeId {placeId}";
        }
        else
        {
            RobloxService.Log($"No account, launching without auth: placeId={placeId}");
            fbArgs = $"--launchMode play --placeId {placeId}";
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(playerPath)
            { UseShellExecute = false, WorkingDirectory = workDir, Arguments = fbArgs });
    }

    private static (long PlaceId, string? GameId, string? AccessCode) ParseRobloxUrl(string url)
    {
        long placeId = 0;
        string? gameId = null;
        string? accessCode = null;

        try
        {
            var q = url.Contains('?') ? url[(url.IndexOf('?') + 1)..] : string.Empty;
            foreach (var kv in q.Split('&'))
            {
                var p = kv.Split('=', 2);
                if (p.Length != 2) continue;

                var val = Uri.UnescapeDataString(p[1]);
                switch (p[0].ToLowerInvariant())
                {
                    case "placeid": long.TryParse(val, out placeId); break;
                    case "gameid": gameId = val; break;
                    case "accesscode": accessCode = val; break;
                }
            }
        }
        catch
        {
        }

        return (placeId, gameId, accessCode);
    }
}
