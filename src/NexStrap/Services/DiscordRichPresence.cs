using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using DiscordRPC;
using DiscordRPC.Logging;
using NexStrap.Services;

namespace NexStrap.Services;

/// <summary>
/// Discord Rich Presence 邵ｺ・ｮ陷茨ｽｨ驍ゑｽ｡騾・・縺醍ｹ晢ｽｩ郢ｧ・ｹ邵ｲ繝ｻ
/// 陷雁・ｽｸﾂ郢昶・ﾎ慕ｹ晞亂ﾎ・+ 霑･・ｶ隲ｷ蛹ｺ・ｩ貊難ｽ｢・ｰ邵ｺ・ｫ郢ｧ蛹ｻ・顔ｸｲ竏ｬ・､繝ｻ辟夂ｹｧ・､郢晏生ﾎｦ郢晏現繝ｻ陷ｷ譴ｧ蜃ｾ騾具ｽｺ霓｣・ｫ邵ｺ・ｧ郢ｧ繧会ｽｫ・ｶ陷ｷ蛹ｻ・邵ｺ・ｪ邵ｺ繝ｻ・ｨ・ｭ髫ｪ蛹ｻﾂ繝ｻ
/// 陞溷､慚夂ｸｺ荵晢ｽ蛾ｶ・ｴ隰暦ｽ･ presence 郢ｧ雋橸ｽ､逕ｻ蟲ｩ邵ｺ蜷ｶ・狗ｸｺ阮吮・邵ｺ・ｯ邵ｺ・ｧ邵ｺ髦ｪ竊醍ｸｺ繝ｻﾂ繝ｻ
/// </summary>
public sealed class DiscordRichPresence : IDisposable
{
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 霑･・ｶ隲ｷ荵昴Ψ郢ｧ・ｧ郢晢ｽｼ郢ｧ・ｺ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private enum Phase
    {
        NexStrapIdle,  // NexStrap 邵ｺ・ｮ邵ｺ・ｿ
        RobloxMenu,    // Roblox 隘搾ｽｷ陷榊桁・ｼ蛹ｻﾎ鍋ｹ昜ｹ斟礼ｹ晢ｽｼ繝ｻ繝ｻ
        FetchingGame,  // API 陷ｿ髢・ｾ蠍ｺ・ｸ・ｭ
        InGame,        // 郢ｧ・ｲ郢晢ｽｼ郢晢｣ｰ郢晏干ﾎ樒ｹｧ・､闕ｳ・ｭ
        Studio,        // Studio 闖ｴ・ｿ騾包ｽｨ闕ｳ・ｭ
    }

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 郢ｧ・､郢晏生ﾎｦ郢昜ｺ･・ｮ螟ゑｽｾ・ｩ繝ｻ莠･繝ｻ邵ｺ・ｦ邵ｺ・ｮ霑･・ｶ隲ｷ蜿･・､迚吝密邵ｺ・ｯ邵ｺ阮呻ｽ檎ｹｧ蟶敖螢ｹﾂｧ邵ｺ・ｦ郢ｧ・ｭ郢晢ｽ･郢晢ｽｼ邵ｺ・ｫ陷茨ｽ･郢ｧ蠕鯉ｽ九・繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private abstract record Ev;
    private record EvRoblox(bool Running)                                               : Ev;
    private record EvLaunch                                                             : Ev;
    private record EvActivity(InstanceActivity Activity)                                 : Ev;
    private record EvPlaceJoined(long PlaceId, long UniverseId, int Slot)              : Ev;
    private record EvGameLeft(int Slot, int RobloxCount)                               : Ev;
    private record EvGameInfo(long PlaceId, long Seq, int Slot, string? Name, string? Icon, string? Creator) : Ev;
    private record EvServerCode(int Slot, string Code)                                  : Ev;
    private record EvUserUpdated(int Slot, string? Url, string? Label)                 : Ev;
    private record EvAvatar(string? Url)                                               : Ev;
    private record EvLabel(string? Label)                                              : Ev;
    private record EvPage(string Name)                                                 : Ev;
    private record EvDiscordReady                                                      : Ev;
    private record EvStudio(bool Detected, string? PlaceName, bool Testing)            : Ev;
    private record EvStudioInit(NexStrap.Services.StudioRpcData Data)             : Ev;
    private record EvStudioRpc(NexStrap.Services.StudioRpcData Data)             : Ev;
    private record EvCountry(string Code)                                              : Ev;
    private record EvHeartbeat                                                         : Ev;
    private record EvFocus(int? Slot)                                                  : Ev;
    private record EvRefresh                                                           : Ev;
    private record EvDisposeClient(DiscordRpcClient? Client)                           : Ev;

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 陷・ｽｦ騾・・ﾎ晉ｹ晢ｽｼ郢晏干繝ｻ邵ｺ・ｿ邵ｺ迹夲ｽｪ・ｭ邵ｺ・ｿ隴厄ｽｸ邵ｺ髦ｪ笘・ｹｧ迢玲・隲ｷ蜈ｷ・ｼ莠･・､螟慚夂ｸｺ荵晢ｽ臥ｸｺ・ｯ髫ｱ・ｭ邵ｺ・ｿ陷ｿ謔ｶ・願氣繧臥舞郢晏干ﾎ溽ｹ昜ｻ｣繝ｦ郢ｧ・｣驍ｨ讙守ｽｰ繝ｻ繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private readonly record struct SlotGame(
        string? Name, string? IconUrl, string? Creator,
        long PlaceId, long UniverseId, string? AvatarUrl, string? UserLabel,
        string? ServerCode);

    private Phase   _phase            = Phase.NexStrapIdle;
    private string  _appId            = AppConstants.DiscordAppId;
    private string  _pageName         = "Home";
    private string? _avatarUrl;
    private string? _userLabel;
    private string? _myCountry;
    private bool    _studioDetected;
    private string? _studioPlaceName;
    private string? _studioContext;
    private string? _studioMode;
    private bool    _studioTesting;
    private long    _studioPlaceId;
    private string? _studioIconUrl;
    private bool    _studioRpcActive;  // 郢晏干ﾎ帷ｹｧ・ｰ郢ｧ・､郢晢ｽｳ邵ｺ譴ｧ逎・け螢ｻ・ｸ・ｭ邵ｺ荵昶・邵ｺ繝ｻﾂｰ
    private int     _activeFocusedSlot = -1;
    private int     _lastFocusedSlot   = -1;
    private int     _robloxCount;
    private long    _joinSeq;
    private const int MaxFetchRetries = 5;

    private readonly Dictionary<int, SlotGame>                     _games        = new();
    private readonly Dictionary<int, (string? Url, string? Label)> _users        = new();
    private readonly Dictionary<int, long>                         _slotJoinSeqs = new();
    private readonly Dictionary<int, Timestamps>                   _slotGameTs   = new();
    private readonly Dictionary<int, DateTime>                     _slotStartedAt = new();
    private readonly Dictionary<int, long>                         _slotPlaceIds = new();
    private readonly Dictionary<int, long>                         _slotUniverseIds = new();
    private readonly Dictionary<int, int>                          _slotFetchRetries = new();
    private readonly Dictionary<int, string>                       _slotServerCodes = new();

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 陞溷､慚夂ｹｧ・ｵ郢晢ｽｼ郢晁侭縺・
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private readonly SettingsService  _settings;
    private readonly RobloxApiService _robloxApi;
    private readonly FastFlagService  _fastFlags;

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 郢ｧ・､郢晏生ﾎｦ郢晏現繝｡郢晢ｽ｣郢晞亂ﾎ昴・繝ｻingleReader 邵ｺ・ｧ驕ｶ・ｶ陷ｷ蝓溯ｳ憺ｫｯ・､繝ｻ繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private readonly Channel<Ev> _ch = Channel.CreateUnbounded<Ev>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly CancellationTokenSource _cts = new();

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // Discord RPC 郢ｧ・ｯ郢晢ｽｩ郢ｧ・､郢ｧ・｢郢晢ｽｳ郢晁肩・ｼ莠･繝ｻ騾・・ﾎ晉ｹ晢ｽｼ郢晄懶ｽ､謔ｶ縲定抄・ｿ邵ｺ繝ｻ笳・ｹｧ竏晄肩郢晢ｽｭ郢昴・縺代・繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private DiscordRpcClient? _client;
    private bool              _rpcConnected;
    private string            _currentAppId = string.Empty;
    private Timestamps?       _startTs;
    private Timestamps?       _gameTs;
    private readonly object   _rpcLock  = new();
    private Timer?            _debounce;
    private RichPresence?     _pending;

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 郢ｧ・ｿ郢ｧ・､郢晄ｧｭ繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private Timer? _heartbeatTimer;
    private Timer? _studioTimer;
    private const int HeartbeatMs  = 15_000;
    private const int StudioPollMs =  3_000;

    private static readonly Dictionary<string, string> CountryRegions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JP"] = "EA", ["KR"] = "EA", ["CN"] = "EA", ["HK"] = "EA", ["TW"] = "EA",
        ["SG"] = "SEA", ["MY"] = "SEA", ["TH"] = "SEA", ["VN"] = "SEA", ["PH"] = "SEA", ["ID"] = "SEA",
        ["IN"] = "SAS", ["PK"] = "SAS", ["BD"] = "SAS", ["LK"] = "SAS",
        ["AU"] = "OC", ["NZ"] = "OC",
        ["US"] = "NA", ["CA"] = "NA", ["MX"] = "NA",
        ["BR"] = "SAM", ["AR"] = "SAM", ["CL"] = "SAM", ["CO"] = "SAM", ["PE"] = "SAM",
        ["GB"] = "EU", ["IE"] = "EU", ["FR"] = "EU", ["DE"] = "EU", ["NL"] = "EU", ["BE"] = "EU",
        ["ES"] = "EU", ["PT"] = "EU", ["IT"] = "EU", ["SE"] = "EU", ["NO"] = "EU", ["FI"] = "EU",
        ["DK"] = "EU", ["PL"] = "EU", ["CZ"] = "EU", ["AT"] = "EU", ["CH"] = "EU",
        ["TR"] = "ME", ["AE"] = "ME", ["SA"] = "ME", ["IL"] = "ME", ["QA"] = "ME",
        ["ZA"] = "AF", ["EG"] = "AF", ["NG"] = "AF", ["KE"] = "AF"
    };

    private static readonly Dictionary<(string From, string To), int> RegionPingMs = new()
    {
        [("EA", "EA")] = 45,   [("EA", "SEA")] = 85,  [("EA", "SAS")] = 125, [("EA", "OC")] = 130,
        [("EA", "NA")] = 155,  [("EA", "EU")] = 230,  [("EA", "ME")] = 190,  [("EA", "AF")] = 260,
        [("SEA", "EA")] = 85,  [("SEA", "SEA")] = 45, [("SEA", "SAS")] = 95, [("SEA", "OC")] = 115,
        [("SEA", "NA")] = 185, [("SEA", "EU")] = 210, [("SEA", "ME")] = 145, [("SEA", "AF")] = 230,
        [("SAS", "EA")] = 125, [("SAS", "SEA")] = 95, [("SAS", "SAS")] = 55, [("SAS", "OC")] = 180,
        [("SAS", "NA")] = 230, [("SAS", "EU")] = 160, [("SAS", "ME")] = 90, [("SAS", "AF")] = 180,
        [("OC", "EA")] = 130,  [("OC", "SEA")] = 115, [("OC", "SAS")] = 180, [("OC", "OC")] = 40,
        [("OC", "NA")] = 170,  [("OC", "EU")] = 260,  [("OC", "ME")] = 230, [("OC", "AF")] = 280,
        [("NA", "EA")] = 155,  [("NA", "SEA")] = 185, [("NA", "SAS")] = 230, [("NA", "OC")] = 170,
        [("NA", "NA")] = 45,   [("NA", "EU")] = 115,  [("NA", "ME")] = 170, [("NA", "AF")] = 190,
        [("SAM", "NA")] = 120, [("SAM", "SAM")] = 55, [("SAM", "EU")] = 200, [("SAM", "AF")] = 220,
        [("EU", "EA")] = 230,  [("EU", "SEA")] = 210, [("EU", "SAS")] = 160, [("EU", "OC")] = 260,
        [("EU", "NA")] = 115,  [("EU", "EU")] = 40,   [("EU", "ME")] = 95,  [("EU", "AF")] = 120,
        [("ME", "EA")] = 190,  [("ME", "SEA")] = 145, [("ME", "SAS")] = 90, [("ME", "OC")] = 230,
        [("ME", "NA")] = 170,  [("ME", "EU")] = 95,   [("ME", "ME")] = 50,  [("ME", "AF")] = 140,
        [("AF", "EA")] = 260,  [("AF", "SEA")] = 230, [("AF", "SAS")] = 180, [("AF", "OC")] = 280,
        [("AF", "NA")] = 190,  [("AF", "EU")] = 120,  [("AF", "ME")] = 140, [("AF", "AF")] = 70
    };

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 陷茨ｽｬ鬮｢迢玲・隲ｷ蜈ｷ・ｼ驛・ｽｪ・ｭ邵ｺ・ｿ陷ｿ謔ｶ・願氣繧臥舞繝ｻ繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    public bool    IsConnected       => _rpcConnected;
    public bool    GameDetected      => _phase is Phase.InGame or Phase.FetchingGame;
    public long    CurrentUniverseId => GetDisplaySlot() is { } slot && _slotUniverseIds.TryGetValue(slot, out var universeId) ? universeId : 0;
    public string  CurrentPageName   => _pageName;

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 陞溷､慚夊惺莉｣・郢ｧ・､郢晏生ﾎｦ郢晁肩・ｼ繝ｻomeViewModel 邵ｺ繝ｻsubscribe 邵ｺ蜷ｶ・九・繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    public event EventHandler<bool>?               ConnectionChanged;
    public event EventHandler<GameInfoFetchedArgs>? GameInfoFetched;
    public event EventHandler?                     SessionEnded;
    public event EventHandler?                     TeleportOccurred;

    public sealed record GameInfoFetchedArgs(
        int Slot, long PlaceId, long UniverseId, string Name, string IconUrl,
        DateTime PlayedAt, DateTime StartedAt);

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 郢ｧ・ｳ郢晢ｽｳ郢ｧ・ｹ郢晏現ﾎ帷ｹｧ・ｯ郢ｧ・ｿ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    public DiscordRichPresence(SettingsService settings, RobloxApiService robloxApi,
        FastFlagService fastFlags)
    {
        _settings  = settings;
        _robloxApi = robloxApi;
        _fastFlags = fastFlags;

        _ = ProcessLoopAsync(_cts.Token);

        _heartbeatTimer = new Timer(_ => Enqueue(new EvHeartbeat()), null, HeartbeatMs, HeartbeatMs);
        _studioTimer    = new Timer(_ => CheckStudioAndEnqueue(), null, StudioPollMs, StudioPollMs);

        _ = Task.Run(async () =>
        {
            var code = await _robloxApi.GetMyCountryAsync();
            if (code != null) Enqueue(new EvCountry(code));
        });
    }

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 陷茨ｽｬ鬮｢繝ｻAPI繝ｻ莠･繝ｻ邵ｺ・ｦ陷ｷ譴ｧ謔・ｸｺ・ｧ郢ｧ・ｭ郢晢ｽ･郢晢ｽｼ邵ｺ・ｫ陷茨ｽ･郢ｧ蠕鯉ｽ狗ｸｺ・ｰ邵ｺ謇假ｽｼ繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    public void EnqueueRobloxChanged(bool running)   => Enqueue(running ? new EvRoblox(true) : new EvRoblox(false));
    public void EnqueueLaunchStarted()               => Enqueue(new EvLaunch());
    public void EnqueueActivity(InstanceActivity activity) => Enqueue(new EvActivity(activity));
    public void EnqueueGameLeft(int slot, int count) => Enqueue(new EvGameLeft(slot, count));
    public void EnqueueUserUpdated(int slot, string? url, string? label) => Enqueue(new EvUserUpdated(slot, url, label));
    public void EnqueueServerCode(int slot, string? code) { if (code != null) Enqueue(new EvServerCode(slot, code)); }
    public void EnqueueStudioPlaytestStarted()       => Enqueue(new EvStudio(_studioDetected, _studioPlaceName, true));
    public void EnqueueStudioPlaytestStopped()       => Enqueue(new EvStudio(_studioDetected, _studioPlaceName, false));
    public void EnqueueStudioInitialized(NexStrap.Services.StudioRpcData data) => Enqueue(new EvStudioInit(data));
    public void EnqueueStudioRpcMessage(NexStrap.Services.StudioRpcData data) => Enqueue(new EvStudioRpc(data));
    public void EnqueueFocusChanged(int? slot)       => Enqueue(new EvFocus(slot));
    public void EnqueueRefresh()                     => Enqueue(new EvRefresh());
    public void SetCurrentPage(string page)          => Enqueue(new EvPage(page));
    public void SetUserAvatar(string? url)           => Enqueue(new EvAvatar(url));
    public void SetUserLabel(string? label)          => Enqueue(new EvLabel(label));

    /// <summary>郢ｧ・ｲ郢晢ｽｼ郢晢｣ｰ陷ｿ繧・・郢ｧ蟶晄直陷ｷ譴ｧ謔・ｸｺ・ｧ陷・ｽｦ騾・・・陞ｳ蠕｡・ｺ繝ｻ・定輔・笆ｽ陟｢繝ｻ・ｦ竏壹・邵ｺ・ｪ邵ｺ繝ｻﾂ繧・・鬩幢ｽｨ邵ｺ・ｧ郢ｧ・ｷ郢晢ｽｪ郢ｧ・｢郢晢ｽｫ陷・ｽｦ騾・・・・ｹｧ蠕鯉ｽ狗ｸｲ繝ｻ/summary>
    public void EnqueuePlaceJoined(long placeId, long universeIdFromLog, int slot)
        => Enqueue(new EvPlaceJoined(placeId, universeIdFromLog, slot));

    // 隨渉隨渉 RPC 驍ゑｽ｡騾・・・ｼ繝ｻnitialize 邵ｺ・ｯ陷繝ｻﾎ夂ｸｺ・ｧ陷・ｽｦ騾・・ﾎ晉ｹ晢ｽｼ郢晏干ﾂｰ郢ｧ迚吩ｻ也ｸｺ・ｶ繝ｻ繝ｻ隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉

    public void SetDiscordEnabled(bool enabled, string? explicitAppId = null)
    {
        if (!enabled) { Disable(); return; }
        var appId = explicitAppId
            ?? (GameDetected ? AppConstants.DiscordRobloxAppId : AppConstants.DiscordAppId);
        RpcInitialize(appId);
        Enqueue(new EvRefresh());
    }

    // 霑夲ｽｹ隹ｿ鬘假ｽ｡・ｨ驕会ｽｺ繝ｻ驛・ｽｵ・ｷ陷咲ｩゑｽｸ・ｭ郢晢ｽｻ郢ｧ・､郢晢ｽｳ郢ｧ・ｹ郢晏現繝ｻ郢晢ｽｫ闕ｳ・ｭ郢晢ｽｻDev繝ｻ菫・繝ｻ郢晁ｼ斐♂郢晢ｽｼ郢ｧ・ｺ邵ｺ・ｫ陝ｷ・ｲ雋ょｳｨ・邵ｺ・ｪ邵ｺ繝ｻ・ｸﾂ隴弱ｅ縺檎ｹ晢ｽｼ郢晁・繝ｻ郢晢ｽｬ郢ｧ・､
    public void EnqueueLaunchingPresence()        => Enqueue(new EvOverlay(OverlayKind.Launching));
    public void EnqueueInstallingStudioPresence() => Enqueue(new EvOverlay(OverlayKind.InstallingStudio));
    public void SetDevPresence()                  => Enqueue(new EvOverlay(OverlayKind.Dev));

    private enum OverlayKind { None, Launching, InstallingStudio, Dev }
    private record EvOverlay(OverlayKind Kind) : Ev;
    private OverlayKind _overlay;

    public void ResetGameTimestamp() => Enqueue(new EvGameTimestampReset());
    private record EvGameTimestampReset : Ev;

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 陷・ｽｦ騾・・ﾎ晉ｹ晢ｽｼ郢晄圜・ｼ莠･鬮ｪ闕ｳﾂ邵ｺ・ｮ霑･・ｶ隲ｷ蛹ｺ蠍檎ｸｺ閧ｴ驪､邵ｺ驛・繝ｻ・ｼ繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private async Task ProcessLoopAsync(CancellationToken ct)
    {
        await foreach (var ev in _ch.Reader.ReadAllAsync(ct))
        {
            try { await HandleEventAsync(ev); }
            catch { /* 郢ｧ・､郢晏生ﾎｦ郢昜ｺ･繝ｻ騾・・繝ｻ關灘唱・､謔ｶ繝ｻ郢晢ｽｫ郢晢ｽｼ郢晏干・定ｱ・ｽ｢郢ｧ竏壺・邵ｺ繝ｻ*/ }
        }
    }

    private static string AppIdName(string id) => id switch
    {
        AppConstants.DiscordAppId       => "NexStrap",
        AppConstants.DiscordRobloxAppId => "Roblox",
        AppConstants.DiscordStudioAppId => "Roblox Studio",
        _                               => id
    };

    private async Task HandleEventAsync(Ev ev)
    {
        var log = NexStrap.Services.Logger.Instance;
        switch (ev)
        {
            // 隨渉隨渉 Roblox 隘搾ｽｷ陷阪・/ 驍ｨ繧・ｽｺ繝ｻ隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvRoblox { Running: true }:
                log.Info("Discord", "Roblox running");
                ClearAllSlots();
                _phase         = Phase.RobloxMenu;
                await SwitchAppIdAsync(AppConstants.DiscordRobloxAppId);
                ApplyPresence();
                break;

            case EvRoblox { Running: false }:
                log.Info("Discord", "Roblox stopped");
                ClearAllSlots();
                _phase         = Phase.NexStrapIdle;
                await SwitchAppIdAsync(AppConstants.DiscordAppId);
                ApplyPresence();
                break;

            // 隨渉隨渉 Roblox 隘搾ｽｷ陷肴坩蟷戊沂蜈ｷ・ｼ莠･辯慕ｹｧ・ｻ郢昴・縺咏ｹ晢ｽｧ郢晢ｽｳ郢ｧ蛛ｵ縺醍ｹ晢ｽｪ郢ｧ・｢繝ｻ繝ｻ隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvLaunch:
                // 陞ｳ貅ｯ・｡蠕｡・ｸ・ｭ邵ｺ・ｮ郢ｧ・ｹ郢晢ｽｭ郢昴・繝ｨ邵ｺ蠕娯旺郢ｧ蜿･・ｰ・ｴ陷ｷ闌ｨ・ｼ蛹ｻ繝ｻ郢晢ｽｫ郢昶・縺・ｹ晢ｽｳ郢ｧ・ｹ郢ｧ・ｿ郢晢ｽｳ郢ｧ・ｹ繝ｻ蟲ｨ繝ｻ郢ｧ・ｯ郢晢ｽｪ郢ｧ・｢邵ｺ蜉ｱ竊醍ｸｺ繝ｻ
                if (_games.Count == 0 && _slotPlaceIds.Count == 0)
                {
                    ClearAllSlots();
                    _phase = Phase.RobloxMenu;
                }
                break;

            // 隨渉隨渉 郢ｧ・ｲ郢晢ｽｼ郢晢｣ｰ陷ｿ繧・・ 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvActivity { Activity: var activity }:
                await HandleActivityChangedAsync(activity);
                break;

            case EvPlaceJoined { PlaceId: var pid, UniverseId: var uid, Slot: var slot }:
                await HandlePlaceJoinedAsync(pid, uid, slot);
                break;

            // 隨渉隨渉 郢ｧ・ｲ郢晢ｽｼ郢晢｣ｰ鬨ｾﾂ陷・ｽｺ 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvGameLeft { Slot: var slot, RobloxCount: var count }:
                log.Info("Discord", $"Game left detected (slot={slot}, robloxCount={count})");
                HandleGameLeft(slot, count);
                await SwitchAppIdAsync(
                    count > 0 ? AppConstants.DiscordRobloxAppId : AppConstants.DiscordAppId);
                _phase = count > 0
                    ? (_games.Count > 0 ? Phase.InGame : Phase.RobloxMenu)
                    : Phase.NexStrapIdle;
                ApplyPresence();
                break;

            // 隨渉隨渉 API 陷ｿ髢・ｾ遉ｼ・ｵ蜈域｣｡ 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvGameInfo { PlaceId: var pid, Seq: var seq, Slot: var infoSlot, Name: var name, Icon: var icon, Creator: var creator }:
                if (_phase != Phase.FetchingGame && _phase != Phase.InGame) break;
                // 陷ｷ蠕｡・ｸﾂ郢ｧ・ｹ郢晢ｽｭ郢昴・繝ｨ邵ｺ・ｫ隴・ｽｰ邵ｺ蜉ｱ・霸laceJoined邵ｺ譴ｧ謫らｸｺ・ｦ邵ｺ繝ｻ笳・ｹｧ迚吝膚邵ｺ繝ｻ・ｵ蜈域｣｡郢ｧ蜻域・邵ｺ・ｦ郢ｧ繝ｻ
                if (!_slotJoinSeqs.TryGetValue(infoSlot, out var slotLatest) || seq != slotLatest) break;
                if (name == null)
                {
                    _slotFetchRetries.TryGetValue(infoSlot, out var retries);
                    retries++;
                    _slotFetchRetries[infoSlot] = retries;
                    if (retries < MaxFetchRetries)
                    {
                        log.Warning("Discord", $"Game info retry ({retries}/{MaxFetchRetries}) placeId={pid}, slot={infoSlot}");
                        _phase = _games.Count > 0 ? Phase.InGame : Phase.FetchingGame;
                    }
                    else
                    {
                        log.Warning("Discord", $"Game info fetch failed after retries placeId={pid}, slot={infoSlot}");
                        _phase = _games.Count > 0 ? Phase.InGame : Phase.RobloxMenu;
                    }
                    ApplyPresence();
                    break;
                }
                if (icon == null)
                {
                    log.Warning("Discord", $"Game info fetch returned no usable data placeId={pid}, slot={infoSlot}");
                    _phase = _games.Count > 0 ? Phase.InGame : Phase.RobloxMenu;
                    ApplyPresence();
                    break;
                }
                log.Info("Discord", $"Game info fetched: {name} (placeId={pid}, slot={infoSlot})");
                _users.TryGetValue(infoSlot, out var su);
                _slotUniverseIds.TryGetValue(infoSlot, out var infoUniverse);
                var serverCode = _games.TryGetValue(infoSlot, out var oldGame) && oldGame.ServerCode != null
                    ? oldGame.ServerCode
                    : (_slotServerCodes.TryGetValue(infoSlot, out var pendingCode) ? pendingCode : null);
                _games[infoSlot] = new SlotGame(name, icon, creator, pid, infoUniverse, su.Url, su.Label, serverCode);
                _slotFetchRetries[infoSlot] = 0;
                _phase = Phase.InGame;
                await SwitchAppIdAsync(AppConstants.DiscordRobloxAppId);
                ApplyPresence();
                var startedAt = _slotStartedAt.TryGetValue(infoSlot, out var slotStartedAt)
                    ? slotStartedAt : DateTime.UtcNow;
                GameInfoFetched?.Invoke(this, new GameInfoFetchedArgs(infoSlot, pid, infoUniverse, name, icon ?? "roblox", DateTime.Now, startedAt));
                break;

            // 隨渉隨渉 郢ｧ・ｵ郢晢ｽｼ郢晁・繝ｻ陜暦ｽｽ郢ｧ・ｳ郢晢ｽｼ郢昴・隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvServerCode { Slot: var serverSlot, Code: var code }:
                _slotServerCodes[serverSlot] = code;
                if (_games.TryGetValue(serverSlot, out var serverGame))
                    _games[serverSlot] = serverGame with { ServerCode = code };
                if (_phase == Phase.InGame) ApplyPresence();
                break;

            // 隨渉隨渉 郢晢ｽｦ郢晢ｽｼ郢ｧ・ｶ郢晢ｽｼ隲繝ｻ・ｰ・ｱ隴厄ｽｴ隴・ｽｰ 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvUserUpdated { Slot: var slot, Url: var url, Label: var label }:
                _users[slot] = (url, label);
                if (_games.TryGetValue(slot, out var g))
                    _games[slot] = g with { AvatarUrl = url, UserLabel = label };
                if (slot == 0) { _avatarUrl = url; }
                ApplyPresence();
                break;

            case EvAvatar { Url: var url }:
                _avatarUrl = url;
                ApplyPresence();
                break;

            case EvLabel { Label: var label }:
                _userLabel = label;
                ApplyPresence();
                break;

            // 隨渉隨渉 郢晏｣ｹ繝ｻ郢ｧ・ｸ陋ｻ繝ｻ・願ｭ厄ｽｿ邵ｺ繝ｻ隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvPage { Name: var name }:
                _pageName = name;
                ApplyPresence();
                break;

            // 隨渉隨渉 Discord 隰暦ｽ･驍ｯ螟ゑｽ｢・ｺ驕ｶ繝ｻ隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvDiscordReady:
                _rpcConnected = true;
                ApplyPresence(); // 隰暦ｽ･驍ｯ螢ｼ・ｾ蠕娯・霑ｴ・ｾ陜ｨ・ｨ邵ｺ・ｮ霑･・ｶ隲ｷ荵晢ｽ定怙蝓ｼﾂ繝ｻ
                ConnectionChanged?.Invoke(this, true);
                break;

            // 隨渉隨渉 Studio RPC繝ｻ蛹ｻ繝ｻ郢晢ｽｩ郢ｧ・ｰ郢ｧ・､郢晢ｽｳ邵ｺ荵晢ｽ臥ｸｺ・ｮ郢昴・繝ｻ郢ｧ・ｿ 遯ｶ繝ｻ郢ｧ・ｦ郢ｧ・｣郢晢ｽｳ郢晏ｳｨ縺育ｹｧ・ｿ郢ｧ・､郢晏現ﾎ晉ｹｧ蛹ｻ・願怕・ｪ陷郁肩・ｼ菫・･ｳ隨渉
            case EvStudioInit { Data: var d }:
                if (!StudioPluginInstaller.IsInstalled) break;
                _studioRpcActive = true;
                _studioDetected  = true;
                _studioPlaceName = d.Details;
                _studioContext   = d.Context;
                _studioMode      = d.Mode;
                _studioTesting   = d.Testing;
                _studioPlaceId   = d.PlaceId;
                _studioIconUrl   = null;
                if (_studioPlaceId > 0)
                    _ = RefreshStudioIconAsync(_studioPlaceId);
                if (_phase != Phase.Studio)
                {
                    _phase = Phase.Studio;
                    await SwitchAppIdAsync(AppConstants.DiscordStudioAppId);
                }
                ApplyPresence();
                break;

            case EvStudioRpc { Data: var d }:
                if (!StudioPluginInstaller.IsInstalled) break;
                _studioRpcActive = true;
                _studioPlaceName = d.Details;
                _studioContext   = d.Context;
                _studioMode      = d.Mode == "Testing" && !d.Testing ? null : d.Mode;
                _studioTesting   = d.Testing;
                _studioPlaceId   = d.PlaceId;
                if (_studioPlaceId <= 0)
                {
                    _studioIconUrl = null;
                }
                else
                {
                    _ = RefreshStudioIconAsync(_studioPlaceId);
                }
                if (_phase == Phase.NexStrapIdle || _phase == Phase.Studio)
                {
                    _phase = Phase.Studio;
                    await SwitchAppIdAsync(AppConstants.DiscordStudioAppId);
                    ApplyPresence();
                }
                break;

            // 隨渉隨渉 Studio 霑･・ｶ隲ｷ蜈ｷ・ｼ蛹ｻ縺育ｹｧ・｣郢晢ｽｳ郢晏ｳｨ縺育ｹｧ・ｿ郢ｧ・､郢晏現ﾎ晞ｶ・｣髫輔・遯ｶ繝ｻ郢晏干ﾎ帷ｹｧ・ｰ郢ｧ・､郢晢ｽｳ邵ｺ譴ｧ謔ｴ隰暦ｽ･驍ｯ螢ｹ繝ｻ陜｣・ｴ陷ｷ蛹ｻ繝ｻ郢晁ｼ斐°郢晢ｽｼ郢晢ｽｫ郢晁・繝｣郢ｧ・ｯ繝ｻ菫・･ｳ隨渉
            case EvStudio { Detected: var det, PlaceName: var place, Testing: var test }:
                _studioDetected  = det;
                _studioPlaceName = place;
                _studioContext   = det ? (test ? "Testing" : "Studio") : null;
                _studioMode      = det ? (test ? "Testing" : "Studio") : null;
                _studioTesting   = test;
                if (det && test)
                {
                    _studioPlaceName = null;
                    _studioContext   = "Testing";
                    _studioMode      = "Testing";
                }
                if (!det)
                {
                    _studioPlaceId = 0;
                    _studioIconUrl = null;
                }
                if (det) _overlay = OverlayKind.None; // Studio 隶諛ｷ繝ｻ隴弱ｅ繝ｻ Launching 驕ｲ蟲ｨ繝ｻ overlay 郢ｧ蛛ｵ縺醍ｹ晢ｽｪ郢ｧ・｢
                if (_phase == Phase.NexStrapIdle || _phase == Phase.Studio)
                {
                    _phase = det ? Phase.Studio : Phase.NexStrapIdle;
                    await SwitchAppIdAsync(det ? AppConstants.DiscordStudioAppId : AppConstants.DiscordAppId);
                    ApplyPresence();
                }
                break;

            // 隨渉隨渉 陜暦ｽｽ郢ｧ・ｳ郢晢ｽｼ郢昴・隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvCountry { Code: var code }:
                _myCountry = code;
                if (_phase == Phase.InGame) ApplyPresence();
                break;

            // 隨渉隨渉 郢昜ｸ翫・郢晏現繝ｳ郢晢ｽｼ郢昴・隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvHeartbeat:
                if (_phase == Phase.InGame)
                    ApplyPresence(); // 郢ｧ・ｿ郢ｧ・､郢晢｣ｰ郢ｧ・ｹ郢ｧ・ｿ郢晢ｽｳ郢晉､ｼ・ｭ蟲ｨ繝ｻ陷蝓ｼﾂ繝ｻ
                else if (_phase == Phase.FetchingGame)
                {
                    var retrySlot = GetDisplaySlot();
                    if (retrySlot != null
                        && _slotPlaceIds.TryGetValue(retrySlot.Value, out var retryPlace)
                        && _slotUniverseIds.TryGetValue(retrySlot.Value, out var retryUniverse)
                        && _slotJoinSeqs.TryGetValue(retrySlot.Value, out var retrySeq)
                        && (!_slotFetchRetries.TryGetValue(retrySlot.Value, out var retries) || retries < MaxFetchRetries))
                        _ = FetchGameInfoAsync(retryPlace, retryUniverse, retrySeq, retrySlot.Value); // 郢晢ｽｪ郢晏現ﾎ帷ｹｧ・､
                }
                break;

            // 隨渉隨渉 郢晁ｼ斐°郢晢ｽｼ郢ｧ・ｫ郢ｧ・ｹ 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvFocus { Slot: var slot }:
                _activeFocusedSlot = slot ?? -1;
                if (slot.HasValue) _lastFocusedSlot = slot.Value;
                NexStrap.Services.Logger.Instance.Info(
                    "Discord",
                    $"Focus slot changed: {(_activeFocusedSlot >= 0 ? _activeFocusedSlot.ToString() : "none")}");
                if (_phase == Phase.InGame) ApplyPresence();
                break;

            // 隨渉隨渉 陟托ｽｷ陋ｻ・ｶ郢晢ｽｪ郢晁ｼ釆樒ｹ昴・縺咏ｹ晢ｽ･ 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvRefresh:
                _overlay = OverlayKind.None;
                ApplyPresence();
                break;

            // 隨渉隨渉 隴鯉ｽｧ郢ｧ・ｯ郢晢ｽｩ郢ｧ・､郢ｧ・｢郢晢ｽｳ郢晞メ・ｧ・｣隰ｾ・ｾ繝ｻ繝ｻvDiscordReady 邵ｺ・ｮ陟募ｾ娯・陟｢繝ｻ笘・怎・ｦ騾・・・・ｹｧ蠕鯉ｽ九・繝ｻ隨渉隨渉隨渉隨渉隨渉
            case EvDisposeClient { Client: var clientToDispose }:
                // FIFO 邵ｺ・ｫ郢ｧ蛹ｻ・・ApplyPresence() 遶翫・SchedulePresence() 邵ｺ・ｯ隴鯉ｽ｢邵ｺ・ｫ陞ｳ蠕｡・ｺ繝ｻ・邵ｺ・ｦ邵ｺ繝ｻ・狗ｸｲ繝ｻ
                // debounce(300ms) 邵ｺ・ｮ騾具ｽｺ霓｣・ｫ郢ｧ蝣､・｢・ｺ陞ｳ貅倪・陟輔・笆ｽ邵ｲ繝ｻ
                await Task.Delay(350);
                clientToDispose?.Dispose();
                break;

            // 隨渉隨渉 霑夲ｽｹ隹ｿ鄙ｫ縺檎ｹ晢ｽｼ郢晁・繝ｻ郢晢ｽｬ郢ｧ・､繝ｻ驛・ｽｵ・ｷ陷咲ｩゑｽｸ・ｭ郢晢ｽｻ郢ｧ・､郢晢ｽｳ郢ｧ・ｹ郢晏現繝ｻ郢晢ｽｫ闕ｳ・ｭ郢晢ｽｻDev繝ｻ繝ｻ隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvOverlay { Kind: var kind }:
                _overlay = kind;
                ApplyPresence();
                break;

            // 隨渉隨渉 郢ｧ・ｲ郢晢ｽｼ郢晢｣ｰ郢ｧ・ｿ郢ｧ・､郢晢｣ｰ郢ｧ・ｹ郢ｧ・ｿ郢晢ｽｳ郢晏干ﾎ懃ｹｧ・ｻ郢昴・繝ｨ 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉
            case EvGameTimestampReset:
                lock (_rpcLock) { _gameTs = Timestamps.Now; }
                break;
        }
    }

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 郢ｧ・ｲ郢晢ｽｼ郢晢｣ｰ陷ｿ繧・・陷・ｽｦ騾・・・ｼ莠･繝ｻ騾・・ﾎ晉ｹ晢ｽｼ郢晄懊・邵ｺ荵晢ｽ芽惱・ｼ邵ｺ・ｶ繝ｻ繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private async Task HandleActivityChangedAsync(InstanceActivity activity)
    {
        if (activity.PlaceId <= 0) return;

        var slot = activity.Slot;
        var samePlace = _slotPlaceIds.TryGetValue(slot, out var currentPlace)
            && currentPlace == activity.PlaceId
            && (!_slotUniverseIds.TryGetValue(slot, out var currentUniverse)
                || activity.UniverseId == 0
                || currentUniverse == 0
                || currentUniverse == activity.UniverseId);

        if (samePlace)
        {
            _slotPlaceIds[slot] = activity.PlaceId;
            if (activity.UniverseId > 0) _slotUniverseIds[slot] = activity.UniverseId;
            if (!_slotStartedAt.ContainsKey(slot)) _slotStartedAt[slot] = activity.TimeJoined;
            lock (_rpcLock)
            {
                if (!_slotGameTs.ContainsKey(slot))
                    _slotGameTs[slot] = new Timestamps(activity.TimeJoined);
            }
            if (_phase is Phase.FetchingGame or Phase.InGame) ApplyPresence();
            return;
        }

        await HandlePlaceJoinedAsync(activity.PlaceId, activity.UniverseId, activity.Slot, activity.TimeJoined);
    }

    private async Task HandlePlaceJoinedAsync(long placeId, long universeIdFromLog, int slot, DateTime? joinedAt = null)
    {
        var prevPhase    = _phase;
        _slotUniverseIds.TryGetValue(slot, out var prevUniverse);
        var seq          = ++_joinSeq;

        _slotJoinSeqs[slot] = seq; // 郢ｧ・ｹ郢晢ｽｭ郢昴・繝ｨ陋ｻ・･邵ｺ・ｫ隴崢隴・ｽｰ郢ｧ・ｷ郢晢ｽｼ郢ｧ・ｱ郢晢ｽｳ郢ｧ・ｹ郢ｧ螳夲ｽｨ蛟ｬ鮖ｸ
        _phase            = _games.Count > 0 ? Phase.InGame : Phase.FetchingGame;

        long universe = universeIdFromLog;
        if (universe == 0)
        {
            try { universe = (await _robloxApi.GetUniverseIdAsync(placeId)) ?? 0; } catch { }
        }

        // 陷ｷ蠕｡・ｸﾂ郢ｧ・ｹ郢晢ｽｭ郢昴・繝ｨ邵ｺ・ｧ郢ｧ蛹ｻ・願ｭ・ｽｰ邵ｺ蜉ｱ・霸laceJoined邵ｺ譴ｧ謫らｸｺ貅ｷ・ｰ・ｴ陷ｷ蛹ｻ繝ｻ邵ｺ・ｿ郢ｧ・ｭ郢晢ｽ｣郢晢ｽｳ郢ｧ・ｻ郢晢ｽｫ繝ｻ莠包ｽｻ謔ｶ縺帷ｹ晢ｽｭ郢昴・繝ｨ邵ｺ・ｯ陟厄ｽｱ鬮ｻ・ｿ邵ｺ蜉ｱ竊醍ｸｺ繝ｻ・ｼ繝ｻ
        if (_slotJoinSeqs.TryGetValue(slot, out var latestForSlot) && latestForSlot != seq) return;

        bool isTeleport = prevPhase == Phase.InGame && universe != 0 && universe == prevUniverse;

        _slotPlaceIds[slot] = placeId;
        _slotUniverseIds[slot] = universe;
        _slotServerCodes.Remove(slot);
        var startedAt = joinedAt ?? DateTime.UtcNow;
        _slotStartedAt[slot] = startedAt;

        if (isTeleport)
        {
            if (_games.TryGetValue(slot, out var g))
                _games[slot] = g with { PlaceId = placeId, UniverseId = universe, ServerCode = null };
            TeleportOccurred?.Invoke(this, EventArgs.Empty);
            _ = FetchGameInfoAsync(placeId, universe, seq, slot);
            return;
        }

        // 隴・ｽｰ髫穂ｸ翫◎郢昴・縺咏ｹ晢ｽｧ郢晢ｽｳ
        SessionEnded?.Invoke(this, EventArgs.Empty);
        _slotFetchRetries[slot] = 0;
        lock (_rpcLock) { _gameTs = new Timestamps(startedAt); _slotGameTs[slot] = _gameTs; }
        ApplyPresence();

        _ = FetchGameInfoAsync(placeId, universe, seq, slot);
    }

    private async Task FetchGameInfoAsync(long placeId, long universe, long seq, int slot)
    {
        try
        {
            var settings = _settings.Settings;
            var english = !(settings.DiscordRpcGameInformationEnabled && settings.DiscordPlaceNameLocalized);
            var (name, icon, creator) = await _robloxApi.GetGameInfoAsync(placeId, universe, english);
            Enqueue(new EvGameInfo(placeId, seq, slot, name, icon, creator));
        }
        catch
        {
            Enqueue(new EvGameInfo(placeId, seq, slot, null, null, null));
        }
    }

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 郢ｧ・ｲ郢晢ｽｼ郢晢｣ｰ鬨ｾﾂ陷・ｽｺ陷・ｽｦ騾・・・ｼ莠･繝ｻ騾・・ﾎ晉ｹ晢ｽｼ郢晄懊・邵ｺ荵晢ｽ芽惱・ｼ邵ｺ・ｶ繝ｻ繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private async Task RefreshStudioIconAsync(long placeId)
    {
        try
        {
            var (_, iconUrl, _) = await _robloxApi.GetGameInfoAsync(placeId);
            if (_studioPlaceId != placeId) return;
            _studioIconUrl = iconUrl;
            NexStrap.Services.Logger.Instance.Info(
                "Discord",
                $"Studio icon resolved: placeId={placeId}, iconUrl={(iconUrl ?? "null")}");
            if (_phase == Phase.Studio)
                ApplyPresence();
        }
        catch
        {
            if (_studioPlaceId == placeId)
            {
                _studioIconUrl = null;
                NexStrap.Services.Logger.Instance.Warning(
                    "Discord",
                    $"Studio icon lookup failed: placeId={placeId}");
            }
        }
    }

    private void HandleGameLeft(int slot, int count)
    {
        _robloxCount = count;
        if (slot >= 0)
        {
            _games.Remove(slot);
            _slotGameTs.Remove(slot);
            _slotStartedAt.Remove(slot);
            _slotJoinSeqs.Remove(slot);
            _slotPlaceIds.Remove(slot);
            _slotUniverseIds.Remove(slot);
            _slotFetchRetries.Remove(slot);
            _slotServerCodes.Remove(slot);
            // 陞ｳ貅倥・郢晢ｽｭ郢ｧ・ｻ郢ｧ・ｹ隰ｨ・ｰ邵ｺ・ｨ邵ｺ・ｮ闕ｵ螟懷ｱｬ郢ｧ蜻育ｴ幃・・・ｼ閧ｲ・｢・ｺ陞ｳ貅倪・ slot 陷台ｼ∝求陟募ｾ後・邵ｺ・ｿ陞ｳ貅ｯ・｡魃会ｽｼ繝ｻ
            while (_games.Count > count && _games.Count > 0)
            {
                var key = _games.Keys.Min();
                _games.Remove(key);
                _slotGameTs.Remove(key);
                _slotStartedAt.Remove(key);
                _slotJoinSeqs.Remove(key);
                _slotPlaceIds.Remove(key);
                _slotUniverseIds.Remove(key);
                _slotFetchRetries.Remove(key);
            }
        }
        else
        {
            NexStrap.Services.Logger.Instance.Warning("Discord", $"GameLeft: slot removed (count={count})");
            // slot 闕ｳ閧ｴ繝ｻ邵ｺ・ｧ郢ｧ繝ｻcount=0 邵ｺ・ｪ郢ｧ迚吶・郢ｧ・ｯ郢晢ｽｪ郢ｧ・｢
            if (count == 0) ClearAllSlots();
        }
    }

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // Presence 髫ｪ閧ｲ・ｮ蜉ｱ繝ｻ鬩包ｽｩ騾包ｽｨ繝ｻ閧ｲ諞ｾ隲ｷ荵敖ｰ郢ｧ逕ｻ・ｱ・ｺ陞ｳ螟奇ｽｫ荵溷飭邵ｺ・ｫ髫ｪ閧ｲ・ｮ證ｦ・ｼ繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private void ApplyPresence() => UpdatePresence();

    private void UpdatePresence()
    {
        if (!_settings.Settings.DiscordRpcEnabled) { SchedulePresence(null); return; }
        var presence = ComputePresence();

        SchedulePresence(presence);
    }

    private RichPresence? ComputePresence()
    {
        var s = _settings.Settings;
        if (!s.DiscordShowLauncherPresence && _overlay != OverlayKind.Dev) { /* 郢晢ｽｩ郢ｧ・ｦ郢晢ｽｳ郢昶・ﾎ慕ｹ晢ｽｼ髯ｦ・ｨ驕会ｽｺ郢ｧ・ｪ郢晁ｼ斐堤ｹｧ繝ｻDev 邵ｺ・ｯ髯ｦ・ｨ驕会ｽｺ */ }

        // 郢ｧ・ｪ郢晢ｽｼ郢晁・繝ｻ郢晢ｽｬ郢ｧ・､邵ｺ迹夲ｽｨ・ｭ陞ｳ螢ｹ・・ｹｧ蠕娯ｻ邵ｺ繝ｻ・玖撻・ｴ陷ｷ蛹ｻ繝ｻ隴崢陷・ｽｪ陷医・
        if (_overlay != OverlayKind.None)
        {
            if (!s.DiscordRpcNexStrapEnabled || !s.DiscordShowLauncherPresence) return null;
            string? label; lock (_rpcLock) { label = _userLabel; }
            if (!s.DiscordRpcProfileEnabled) label = null;
            var buttons = NexStrapDownloadButtons();
            return _overlay switch
            {
                OverlayKind.Launching        => Build("Launching", null, "nexstrap", "NexStrap Launcher · Created by K", _avatarUrl, label, buttons),
                OverlayKind.InstallingStudio => Build(null, null, "nexstrap", "NexStrap Launcher · Created by K", _avatarUrl, label, buttons),
                OverlayKind.Dev              => Build("NexStrap / Developer", null, "nexstrap", "NexStrap Developer",             null,        null,  buttons),
                _                            => null
            };
        }

        switch (_phase)
        {
            case Phase.NexStrapIdle:
            case Phase.Studio:
                if (!s.DiscordRpcNexStrapEnabled || !s.DiscordShowLauncherPresence) return null;
                return StudioOrPagePresence(s);

            case Phase.RobloxMenu:
            case Phase.FetchingGame:
                return null; // 郢晢ｽ｡郢昜ｹ斟礼ｹ晢ｽｼ郢晢ｽｻAPI陷ｿ髢・ｾ蠍ｺ・ｸ・ｭ邵ｺ・ｯ髯ｦ・ｨ驕会ｽｺ邵ｺ・ｪ邵ｺ繝ｻ

            case Phase.InGame:
                return ComputeInGamePresence(s);

            default:
                return null;
        }
    }

    private RichPresence? StudioOrPagePresence(Models.AppSettings s)
    {
        string? label; lock (_rpcLock) { label = _userLabel; }
        if (!s.DiscordRpcProfileEnabled) label = null;
        Timestamps? ts; lock (_rpcLock) { ts = _startTs; }

        if (_phase == Phase.Studio)
        {
            // details: core action/target, state: related context bundle.
            var details = string.IsNullOrEmpty(_studioPlaceName) ? null : _studioPlaceName;
            var state   = !string.IsNullOrEmpty(_studioContext) && _studioContext != "Testing"
                        ? _studioContext
                        : !string.IsNullOrEmpty(_studioMode) && _studioMode != "Testing"
                            ? _studioMode
                            : (_studioTesting ? "Testing" : null);

            return Build(s.DiscordShowLauncherDetails ? details : null, state,
                _studioIconUrl ?? "nexstrap",
                _studioPlaceName ?? "Roblox Studio",
                _avatarUrl, _avatarUrl != null ? (label ?? "Profile") : null,
                timestamps: ts);
        }

        var pageDetails = s.DiscordRpcNexStrapEnabled && s.DiscordShowLauncherDetails ? _pageName : null;
        return Build(pageDetails, null, "nexstrap", "NexStrap Launcher · Created by K",
            _avatarUrl, label, NexStrapDownloadButtons());
    }

    private RichPresence? ComputeInGamePresence(Models.AppSettings s)
    {
        if (_games.Count == 0) return null;

        string? label; lock (_rpcLock) { label = _userLabel; }
        if (!s.DiscordRpcProfileEnabled) label = null;

        int count;
        try { count = Math.Max(1, CountRobloxProcesses()); } catch { count = 1; }

        if (count == 1)
        {
            var displaySlot = _games.Keys.Max();
            var g = _games[displaySlot];
            Timestamps? gameTs; lock (_rpcLock) { _slotGameTs.TryGetValue(displaySlot, out gameTs); }
            var details = s.DiscordRpcGameInformationEnabled && s.DiscordShowCreator && g.Creator != null
                ? $"{g.Name} · by {g.Creator}" : g.Name ?? "Roblox";
            var buttons = s.DiscordRpcSocialEnabled && s.DiscordShowJoinButton && g.PlaceId > 0
                ? RobloxGameButtons(g.PlaceId)
                : null;
            var partyPreset = ResolvePartyPreset(s, g.PlaceId);
            var partyState = partyPreset?.Label;
            return Build(details, partyState ?? FormatState(s, g), g.IconUrl ?? "roblox",
                g.Name ?? "Roblox", g.AvatarUrl ?? _avatarUrl,
                s.DiscordRpcProfileEnabled && (g.AvatarUrl != null || _avatarUrl != null) ? (g.UserLabel ?? label ?? "Profile") : null,
                buttons, gameTs, ToDiscordParty(partyPreset, g.PlaceId));
        }

        // 郢晄ｧｭﾎ晉ｹ昶・縺・ｹ晢ｽｳ郢ｧ・ｹ郢ｧ・ｿ郢晢ｽｳ郢ｧ・ｹ: 郢晁ｼ斐°郢晢ｽｼ郢ｧ・ｫ郢ｧ・ｹ闕ｳ・ｭ繝ｻ蛹ｻ竏ｪ邵ｺ貅倥・郢ｧ・ｹ郢晢ｽｭ郢昴・繝ｨ隴崢陞滂ｽｧ繝ｻ蟲ｨ繝ｻ郢ｧ・ｲ郢晢ｽｼ郢晢｣ｰ郢ｧ螳夲ｽ｡・ｨ驕会ｽｺ
        var focusedSlot = GetDisplaySlot() ?? _games.Keys.Max();
        var focused = _games[focusedSlot];
        Timestamps? multiGameTs; lock (_rpcLock) { _slotGameTs.TryGetValue(focusedSlot, out multiGameTs); }

        var multiDetails = s.DiscordRpcGameInformationEnabled && s.DiscordShowCreator && focused.Creator != null
            ? $"{focused.Name} · by {focused.Creator}" : focused.Name ?? "Roblox";

        var multiPartyPreset = ResolvePartyPreset(s, focused.PlaceId);
        var baseState   = FormatState(s, focused);
        var instanceStr = $"Instances {count}";
        var multiState  = multiPartyPreset != null
            ? multiPartyPreset.Label
            : (baseState != null ? $"{baseState} · {instanceStr}" : instanceStr);

        var multiButtons = s.DiscordRpcSocialEnabled && s.DiscordShowJoinButton && focused.PlaceId > 0
            ? RobloxGameButtons(focused.PlaceId)
            : null;

        return Build(multiDetails, multiState,
            focused.IconUrl ?? "roblox", focused.Name ?? "Roblox",
            focused.AvatarUrl ?? _avatarUrl,
            s.DiscordRpcProfileEnabled && (focused.AvatarUrl != null || _avatarUrl != null) ? (focused.UserLabel ?? label ?? "Profile") : null,
            multiButtons, multiGameTs, ToDiscordParty(multiPartyPreset, focused.PlaceId));
    }

    private string? FormatState(Models.AppSettings s, SlotGame game)
    {
        if (!s.DiscordRpcGameInformationEnabled) return null;
        var flagStr = s.DiscordShowFlagCount && _fastFlags.GetAll().Count > 0
            ? $"{_fastFlags.GetAll().Count} Flags" : null;
        if (!s.DiscordShowServerRegion || game.ServerCode == null) return flagStr;
        var serverFlag = ToFlagEmoji(game.ServerCode);
        var ping = s.DiscordShowEstimatedPing ? FormatEstimatedPing(_myCountry, game.ServerCode) : null;
        var pingSuffix = ping != null ? $" {ping}" : string.Empty;
        var server = _myCountry != null
            ? $"{ToFlagEmoji(_myCountry)} → {serverFlag} Server{pingSuffix}"
            : $"{serverFlag} Server{pingSuffix}";
        return flagStr != null ? $"{server} · {flagStr}" : server;
    }

    private static string? FormatEstimatedPing(string? fromCountry, string toCountry)
    {
        var ping = EstimatePingMs(fromCountry, toCountry);
        return ping != null ? $"~{ping.Value}ms" : null;
    }

    private static int? EstimatePingMs(string? fromCountry, string toCountry)
    {
        if (string.IsNullOrWhiteSpace(fromCountry) || string.IsNullOrWhiteSpace(toCountry))
            return null;

        if (!CountryRegions.TryGetValue(fromCountry, out var fromRegion))
            return null;

        if (!CountryRegions.TryGetValue(toCountry, out var toRegion))
            return null;

        if (RegionPingMs.TryGetValue((fromRegion, toRegion), out var ping))
            return ping;

        if (RegionPingMs.TryGetValue((toRegion, fromRegion), out ping))
            return ping;

        return null;
    }

    private static RichPresence Build(string? details, string? state,
        string largeImage, string largeText,
        string? smallImage, string? smallText,
        Button[]? buttons = null, Timestamps? timestamps = null, Party? party = null)
    {
        Timestamps? ts = timestamps;
        return new RichPresence
        {
            Details    = details,
            State      = state,
            Assets     = new Assets
            {
                LargeImageKey  = largeImage,
                LargeImageText = largeText,
                SmallImageKey  = smallImage,
                SmallImageText = smallText
            },
            Timestamps = ts,
            Buttons    = buttons,
            Party      = party
        };
    }

    private static Models.DiscordPartyPreset? ResolvePartyPreset(Models.AppSettings settings, long placeId)
    {
        if (!settings.DiscordRpcSocialEnabled
            || !settings.DiscordPartyPresetsEnabled
            || placeId <= 0
            || settings.DiscordPartyPresets.Count == 0)
            return null;

        var preset = settings.DiscordPartyPresets.FirstOrDefault(p =>
            p.Enabled && p.PlaceId == placeId && p.CurrentSize > 0 && p.MaxSize > 0);
        if (preset == null) return null;

        var size = Math.Max(1, preset.CurrentSize);
        var max = Math.Max(size, preset.MaxSize);

        return preset;
    }

    private static Party? ToDiscordParty(Models.DiscordPartyPreset? preset, long placeId)
    {
        if (preset == null) return null;

        var size = Math.Max(1, preset.CurrentSize);
        var max = Math.Max(size, preset.MaxSize);

        return new Party
        {
            ID = $"preset-place-{placeId}",
            Size = size,
            Max = max,
            Privacy = Party.PrivacySetting.Private
        };
    }

    private static Button[] NexStrapDownloadButtons() =>
    [
        new() { Label = "GitHub",  Url = "https://github.com/k153636/NexStrap" },
        new() { Label = "Discord", Url = "https://discord.gg/PPrKt97jRn" }
    ];

    private static Button[] RobloxGameButtons(long placeId) =>
    [
        new() { Label = "Join Game", Url = $"https://www.roblox.com/games/{placeId}" },
        new() { Label = "GitHub",    Url = "https://github.com/k153636/NexStrap" }
    ];

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // RPC 鬨ｾ竏ｽ・ｿ・｡繝ｻ莠･閻ｰ闕ｳﾂ郢晢ｽｫ郢晢ｽｼ郢晁肩・ｼ繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private void SchedulePresence(RichPresence? presence)
    {
        lock (_rpcLock)
        {
            _pending = presence;
            if (_debounce == null)
                _debounce = new Timer(_ => FlushPresence(), null, 300, Timeout.Infinite);
            else
                _debounce.Change(300, Timeout.Infinite);
        }
    }

    private void FlushPresence()
    {
        RichPresence? presence;
        DiscordRpcClient? client;
        lock (_rpcLock) { presence = _pending; _pending = null; client = _client; }

        if (client == null) return;
        try
        {
            if (presence == null)
            {
                client.ClearPresence();
            }
            else
            {
                if (presence.Timestamps == null)
                {
                    Timestamps? ts; lock (_rpcLock) { ts = _startTs; }
                    presence.Timestamps = ts;
                }
                client.SetPresence(presence);
            }
        }
        catch { }
    }

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // App ID 陋ｻ繝ｻ・願ｭ厄ｽｿ邵ｺ闌ｨ・ｼ蝓溽｣・け螟ゑｽ｢・ｺ驕ｶ荵昶穐邵ｺ・ｧ陟輔・笆ｽ繝ｻ繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private async Task SwitchAppIdAsync(string appId)
    {
        bool alreadyConnected;
        lock (_rpcLock)
        {
            alreadyConnected = _currentAppId == appId && _client != null && _rpcConnected;
        }
        if (alreadyConnected) return;

        var from = AppIdName(_currentAppId);
        var to   = AppIdName(appId);
        NexStrap.Services.Logger.Instance.Info("Discord", $"App switch: {from} -> {to}");
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnReady(object? _, bool c) { if (c) tcs.TrySetResult(true); }
        ConnectionChanged += OnReady;
        RpcInitialize(appId);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        ConnectionChanged -= OnReady;
        if (completed == tcs.Task)
            NexStrap.Services.Logger.Instance.Info("Discord", $"App connected: {to}");
        else
            NexStrap.Services.Logger.Instance.Warning("Discord", $"App connection timeout: {to}");
    }

    private void RpcInitialize(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return;

        DiscordRpcClient? oldClient;
        lock (_rpcLock)
        {
            if (_currentAppId == appId && _client != null) return;
            oldClient      = _client;
            _client        = null;
            _currentAppId  = appId;
            _rpcConnected  = false;
            _debounce?.Dispose(); _debounce = null;
            _pending       = null;
        }

        try
        {
            var ts = Timestamps.Now;
            lock (_rpcLock) { _startTs = ts; }

            var nc = new DiscordRpcClient(appId) { Logger = new NullLogger() };
            nc.OnReady += (_, _) =>
            {
                lock (_rpcLock) { _rpcConnected = true; }
                Task.Run(() =>
                {
                    // FIFO 郢ｧ・ｭ郢晢ｽ･郢晢ｽｼ邵ｺ・ｮ霑夲ｽｹ隲､・ｧ郢ｧ雋櫁懸騾包ｽｨ邵ｺ蜉ｱ笳・ｬ・・・ｺ荳茨ｽｿ譎・ｽｨ・ｼ繝ｻ繝ｻ
                    //   EvDiscordReady 遶翫・ApplyPresence() 遶翫・SchedulePresence() 遶翫・debounce(300ms) 遶翫・FlushPresence()
                    //   EvDisposeClient 遶翫・350ms 陟輔・・ｩ繝ｻ遶翫・oldClient.Dispose()
                    //
                    // EvDisposeClient 邵ｺ・ｯ陟｢繝ｻ笘・EvDiscordReady 邵ｺ・ｮ陟募ｾ娯・陷・ｽｦ騾・・・・ｹｧ蠕鯉ｽ九・繝ｻingleReader FIFO繝ｻ蟲ｨﾂ繝ｻ
                    // 350ms > debounce(300ms) 邵ｺ・ｪ邵ｺ・ｮ邵ｺ・ｧ FlushPresence() 邵ｺ・ｯ陟｢繝ｻ笘・怦蛹ｻ竊楢楜蠕｡・ｺ繝ｻ笘・ｹｧ荵敖繝ｻ
                    // 郢ｧ・ｹ郢晏｣ｹ繝｣郢ｧ・ｯ郢晢ｽｻ陜玲ｨ抵ｽｷ螟青貅ｷ・ｺ・ｦ邵ｺ・ｫ關捺剌・ｭ蛟･・邵ｺ・ｪ邵ｺ繝ｻ・ｼ繝ｻiscord RPC 邵ｺ・ｯ郢晢ｽｭ郢晢ｽｼ郢ｧ・ｫ郢晢ｽｫ郢昜ｻ｣縺・ｹ晄圜・ｼ蟲ｨﾂ繝ｻ
                    Enqueue(new EvDiscordReady());
                    Enqueue(new EvDisposeClient(oldClient));
                    ConnectionChanged?.Invoke(this, true);
                });
            };
            nc.OnClose += (_, _) =>
            {
                lock (_rpcLock) { _rpcConnected = false; }
                Task.Run(() => ConnectionChanged?.Invoke(this, false));
            };
            nc.OnError += (_, _) =>
            {
                lock (_rpcLock) { _rpcConnected = false; }
                Task.Run(() => ConnectionChanged?.Invoke(this, false));
            };
            lock (_rpcLock) { _client = nc; }
            nc.Initialize();
        }
        catch { oldClient?.Dispose(); }
    }

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // Studio 騾ｶ・｣髫募私・ｼ蛹ｻ縺｡郢ｧ・､郢晄ｧｭ繝ｻ邵ｺ荵晢ｽ芽惱・ｼ邵ｺ・ｶ繝ｻ繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private string _lastStudioPresenceKey = string.Empty;
    private int    _studioHomeConfirmCount;
    private const int StudioHomeConfirmThreshold = 3; // 3 郢晄亢繝ｻ郢晢ｽｪ郢晢ｽｳ郢ｧ・ｰ繝ｻ繝ｻ驕俶慣・ｼ陋ｾﾂ・｣驍ｯ螢ｹ縲堤ｹ晏ｸ吶・郢晢｣ｰ邵ｺ讙趣ｽ｢・ｺ髫ｱ髦ｪ・・ｹｧ蠕娯螺郢ｧ閾･・｢・ｺ陞ｳ繝ｻ

    private void CheckStudioAndEnqueue()
    {
        try
        {
            var proc = Process.GetProcessesByName("RobloxStudioBeta")
                .Concat(Process.GetProcessesByName("RobloxStudio"))
                .FirstOrDefault(p => !p.HasExited);
            var detected = proc != null;

            if (detected != _studioDetected)
            {
                _lastStudioPresenceKey  = string.Empty;
                _studioHomeConfirmCount = 0;
                Enqueue(new EvStudio(detected, null, false));
                return;
            }

            if (!detected || GameDetected || _studioTesting) return;

            var title = proc!.MainWindowTitle;
            if (string.IsNullOrEmpty(title)) return;

            if (title.Contains(" - Roblox Studio"))
            {
                // 郢晏干ﾎ樒ｹ晢ｽｼ郢ｧ・ｹ邵ｺ遒∝ｹ慕ｸｺ繝ｻ窶ｻ邵ｺ繝ｻ・・遯ｶ繝ｻ* (隴幢ｽｪ闖ｫ譎擾ｽｭ蛟･繝ｻ郢晢ｽｼ郢ｧ・ｯ) 郢ｧ蟶晏求陷ｴ・ｻ邵ｺ蜉ｱ窶ｻ邵ｺ荵晢ｽ芽抄・ｿ邵ｺ繝ｻ
                _studioHomeConfirmCount = 0;
                var placeName = title.Replace(" - Roblox Studio", "").Trim().TrimStart('*').Trim();
                var key       = placeName; // 郢晏干ﾎ樒ｹ晢ｽｼ郢ｧ・ｹ陷ｷ髦ｪ・堤ｹｧ・ｭ郢晢ｽｼ邵ｺ・ｫ邵ｺ蜷ｶ・・
                if (key == _lastStudioPresenceKey) return;
                _lastStudioPresenceKey = key;
                Enqueue(new EvStudio(true, placeName, false));
            }
            else
            {
                // 郢晏ｸ吶・郢晢｣ｰ or 闕ｳ閧ｴ繝ｻ邵ｺ・ｪ郢ｧ・ｿ郢ｧ・､郢晏現ﾎ・遯ｶ繝ｻ闕ｳﾂ隴弱ｉ蝎ｪ邵ｺ・ｪ陞溽甥蝟ｧ郢ｧ蝣､笏碁囎謔ｶ笘・ｹｧ荵昶螺郢ｧ竏ｬ・､繝ｻ辟夊摎讓抵ｽ｢・ｺ髫ｱ繝ｻ
                _studioHomeConfirmCount++;
                if (_studioHomeConfirmCount < StudioHomeConfirmThreshold) return;
                if (_lastStudioPresenceKey == "home") return;
                _lastStudioPresenceKey = "home";
                Enqueue(new EvStudio(true, null, false));
            }
        }
        catch { }
    }

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // 郢晏･ﾎ晉ｹ昜ｻ｣繝ｻ
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    private void Enqueue(Ev ev) => _ch.Writer.TryWrite(ev);

    private int? GetDisplaySlot()
    {
        if (_activeFocusedSlot >= 0 && _games.ContainsKey(_activeFocusedSlot))
            return _activeFocusedSlot;
        if (_lastFocusedSlot >= 0 && _games.ContainsKey(_lastFocusedSlot))
            return _lastFocusedSlot;
        if (_games.Count > 0)
            return _games.Keys.Max();
        if (_slotPlaceIds.Count > 0)
            return _slotPlaceIds.Keys.Max();
        return null;
    }

    private void ClearAllSlots()
    {
        _games.Clear();
        _slotGameTs.Clear();
        _slotStartedAt.Clear();
        _slotJoinSeqs.Clear();
        _slotPlaceIds.Clear();
        _slotUniverseIds.Clear();
        _slotFetchRetries.Clear();
        _slotServerCodes.Clear();
    }

    private static int CountRobloxProcesses() =>
        Process.GetProcessesByName("RobloxPlayerBeta")
               .Concat(Process.GetProcessesByName("RobloxPlayer"))
               .Count();

    private static string ToFlagEmoji(string code)
    {
        if (code.Length != 2) return code;
        var c0 = char.ToUpperInvariant(code[0]);
        var c1 = char.ToUpperInvariant(code[1]);
        if (c0 < 'A' || c0 > 'Z' || c1 < 'A' || c1 > 'Z') return code;
        return char.ConvertFromUtf32(0x1F1E6 + (c0 - 'A')) + char.ConvertFromUtf32(0x1F1E6 + (c1 - 'A'));
    }

    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ
    // Disable / Dispose
    // 隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ隨顔ｵｶ豁ｦ

    public void Disable()
    {
        DiscordRpcClient? client;
        lock (_rpcLock)
        {
            _debounce?.Dispose(); _debounce = null;
            _pending     = null;
            _currentAppId = string.Empty;
            client       = _client;
            _client      = null;
            _rpcConnected = false;
        }
        client?.ClearPresence();
        client?.Dispose();
        ConnectionChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _ch.Writer.Complete();
        _heartbeatTimer?.Dispose();
        _studioTimer?.Dispose();
        DiscordRpcClient? client;
        lock (_rpcLock)
        {
            _debounce?.Dispose(); _debounce = null;
            client       = _client; _client = null;
            _rpcConnected = false;
        }
        client?.ClearPresence();
        client?.Dispose();
        _cts.Dispose();
    }
}
