using System.Text.Json.Serialization;

namespace NexStrap.Models;

public enum FastFlagType
{
    Boolean,
    Integer,
    Float,
    String
}

public class FastFlag
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public FastFlagType Type { get; set; } = FastFlagType.String;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Custom";
    public bool IsPreset { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public static class FastFlagPresets
{
    public static IReadOnlyList<FastFlag> All => new List<FastFlag>
    {
        new() { Name = "DFIntTaskSchedulerTargetFps", Value = "144", Type = FastFlagType.Integer,
            Description = "Set FPS limit (default 60)", Category = "Performance", IsPreset = true },
        new() { Name = "FFlagDebugGraphicsDisableDirect3D11", Value = "false", Type = FastFlagType.Boolean,
            Description = "Disable Direct3D11", Category = "Graphics", IsPreset = true },
        new() { Name = "FFlagEnableQuickGameLaunch", Value = "true", Type = FastFlagType.Boolean,
            Description = "Enable fast game launch", Category = "Performance", IsPreset = true },
        new() { Name = "DFIntConnectionMTUSize", Value = "1400", Type = FastFlagType.Integer,
            Description = "Network MTU size", Category = "Network", IsPreset = true },
        new() { Name = "FFlagGraphicsGLEnableHQShadersExclusion", Value = "false", Type = FastFlagType.Boolean,
            Description = "Disable high-quality shader exclusion", Category = "Graphics", IsPreset = true },
        new() { Name = "DFIntRakNetResendBufferArrayLength", Value = "128", Type = FastFlagType.Integer,
            Description = "Network resend buffer size", Category = "Network", IsPreset = true },
        new() { Name = "FFlagNewAvatarLoadingUI", Value = "true", Type = FastFlagType.Boolean,
            Description = "Enable new avatar loading UI", Category = "UI", IsPreset = true },
        new() { Name = "DFIntTextureCompositorActiveJobs", Value = "4", Type = FastFlagType.Integer,
            Description = "Parallel texture compositor jobs", Category = "Performance", IsPreset = true },
        new() { Name = "FFlagDisableNewIGMinDUA", Value = "true", Type = FastFlagType.Boolean,
            Description = "Disable in-game menu minimize UI", Category = "UI", IsPreset = true },
        new() { Name = "DFIntHttpCurlConnectionTimeoutMs", Value = "30000", Type = FastFlagType.Integer,
            Description = "HTTP connection timeout (ms)", Category = "Network", IsPreset = true },
        new() { Name = "FFlagEnableBetaFacialAnimation", Value = "true", Type = FastFlagType.Boolean,
            Description = "Enable beta facial animation", Category = "Avatar", IsPreset = true },
        new() { Name = "DFIntMaxFrameBufferSize", Value = "4", Type = FastFlagType.Integer,
            Description = "Max frame buffer size", Category = "Graphics", IsPreset = true },
    };
}

public class PresetGroup
{
    public string Name     { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public IReadOnlyList<FastFlag> Flags { get; init; } = [];
}

public static class FastFlagBundles
{
    public static IReadOnlyList<PresetGroup> Groups =>
    [
        new PresetGroup
        {
            Name = "Max FPS (Allowlisted)",
            IconPath = "M11 21h-1l1-7H7.5c-.58 0-.57-.32-.38-.66.19-.34.05-.08.07-.12C8.48 10.94 10.42 7.54 13 3h1l-1 7h3.5c.49 0 .56.33.47.51l-.07.15C12.96 17.55 11 21 11 21z",
            Flags =
            [
                // Roblox Fast Flag Allowlist (2026年2月施行)に準拠 — FPS最優先の軽量化構成
                new FastFlag { Name = "DFIntDebugFRMQualityLevelOverride",      Value = "1",    Type = FastFlagType.Integer, Category = "Performance" },
                new FastFlag { Name = "DFFlagTextureQualityOverrideEnabled",    Value = "True", Type = FastFlagType.Boolean, Category = "Performance" },
                new FastFlag { Name = "DFIntTextureQualityOverride",            Value = "0",    Type = FastFlagType.Integer, Category = "Performance" },
                new FastFlag { Name = "FIntDebugForceMSAASamples",              Value = "0",    Type = FastFlagType.Integer, Category = "Performance" },
                new FastFlag { Name = "FIntFRMMaxGrassDistance",                Value = "0",    Type = FastFlagType.Integer, Category = "Performance" },
                new FastFlag { Name = "FIntFRMMinGrassDistance",                Value = "0",    Type = FastFlagType.Integer, Category = "Performance" },
                new FastFlag { Name = "FIntGrassMovementReducedMotionFactor",   Value = "100",  Type = FastFlagType.Integer, Category = "Performance" },
                new FastFlag { Name = "DFIntCSGLevelOfDetailSwitchingDistance", Value = "0",    Type = FastFlagType.Integer, Category = "Performance" },
                new FastFlag { Name = "DFIntCSGLevelOfDetailSwitchingDistanceL12", Value = "0", Type = FastFlagType.Integer, Category = "Performance" },
                new FastFlag { Name = "DFIntCSGLevelOfDetailSwitchingDistanceL23", Value = "0", Type = FastFlagType.Integer, Category = "Performance" },
                new FastFlag { Name = "DFIntCSGLevelOfDetailSwitchingDistanceL34", Value = "0", Type = FastFlagType.Integer, Category = "Performance" },
                new FastFlag { Name = "FFlagDebugSkyGray",                      Value = "True", Type = FastFlagType.Boolean, Category = "Graphics" },
            ]
        },
        new PresetGroup
        {
            Name = "Graphics Lite",
            IconPath = "M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5C21.27 7.61 17 4.5 12 4.5zm0 12.5c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z",
            Flags =
            [
                new FastFlag { Name = "FFlagDisablePostFx",           Value = "true",  Type = FastFlagType.Boolean, Category = "Graphics" },
                new FastFlag { Name = "FFlagDisableShadows",          Value = "true",  Type = FastFlagType.Boolean, Category = "Graphics" },
                new FastFlag { Name = "FFlagDisableBloom",            Value = "true",  Type = FastFlagType.Boolean, Category = "Graphics" },
                new FastFlag { Name = "FFlagDisableDepthOfField",     Value = "true",  Type = FastFlagType.Boolean, Category = "Graphics" },
                new FastFlag { Name = "FFlagDisableGlobalShadows",    Value = "true",  Type = FastFlagType.Boolean, Category = "Graphics" },
                new FastFlag { Name = "DFIntTaskSchedulerTargetFps",  Value = "144",   Type = FastFlagType.Integer, Category = "Performance" },
            ]
        },
        new PresetGroup
        {
            Name = "Render Optimized",
            IconPath = "M7 2v11h3v9l7-12h-4l4-8z",
            Flags =
            [
                new FastFlag { Name = "FFlagEnableReducedLatency",    Value = "true",  Type = FastFlagType.Boolean, Category = "Performance" },
                new FastFlag { Name = "FFlagFastGPULightCulling3",    Value = "true",  Type = FastFlagType.Boolean, Category = "Graphics" },
                new FastFlag { Name = "FFlagRenderFixFog",            Value = "true",  Type = FastFlagType.Boolean, Category = "Graphics" },
                new FastFlag { Name = "FFlagRenderOptimizedShadows",  Value = "true",  Type = FastFlagType.Boolean, Category = "Graphics" },
            ]
        },
        new PresetGroup
        {
            Name = "Memory / CPU",
            IconPath = "M9 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2h-4V1h-2v2h-4V1H9v2zM5 5h14v14H5V5zm6 6H7v2h4v4h2v-4h4v-2h-4V7h-2v4z",
            Flags =
            [
                new FastFlag { Name = "DFIntPhysicsStepsPerFrame",       Value = "1",   Type = FastFlagType.Integer, Category = "Performance" },
                new FastFlag { Name = "DFIntGCJobFrequencyMs",           Value = "250", Type = FastFlagType.Integer, Category = "Performance" },
                new FastFlag { Name = "FFlagLuaAppEnableLowMemoryMode",  Value = "true",Type = FastFlagType.Boolean, Category = "Performance" },
            ]
        },
        new PresetGroup
        {
            Name = "Network Optimized",
            IconPath = "M1 9l2 2c4.97-4.97 13.03-4.97 18 0l2-2C16.93 2.93 7.08 2.93 1 9zm8 8l3 3 3-3c-1.65-1.66-4.34-1.66-6 0zm-4-4l2 2c2.76-2.76 7.24-2.76 10 0l2-2C15.14 9.14 8.87 9.14 5 13z",
            Flags =
            [
                new FastFlag { Name = "DFIntConnectionMTUSize",              Value = "1400", Type = FastFlagType.Integer, Category = "Network" },
                new FastFlag { Name = "DFIntRakNetResendTimeoutMS",          Value = "200",  Type = FastFlagType.Integer, Category = "Network" },
                new FastFlag { Name = "DFIntRakNetResendBufferArrayLength",  Value = "128",  Type = FastFlagType.Integer, Category = "Network" },
                new FastFlag { Name = "DFIntNetworkPrediction",              Value = "0",    Type = FastFlagType.Integer, Category = "Network" },
                new FastFlag { Name = "DFIntNetworkLatencyTolerance",        Value = "0",    Type = FastFlagType.Integer, Category = "Network" },
            ]
        },
    ];

    public static IReadOnlyList<FastFlag> AllFlags =>
        Groups.SelectMany(g => g.Flags).ToList();
}

public static class FlagDescriptions
{
    public static readonly Dictionary<string, (string Description, string Category)> Known = new()
    {
        // Performance
        ["DFIntTaskSchedulerTargetFps"]         = ("Set FPS limit (default: 60fps)", "Performance"),
        ["DFIntTextureCompositorActiveJobs"]    = ("Parallel texture compositor jobs — more = faster loading", "Performance"),
        ["FFlagEnableQuickGameLaunch"]          = ("Enable fast game launch", "Performance"),
        ["DFIntMaxFrameBufferSize"]             = ("Max frame buffer size — smaller = lower input latency", "Performance"),
        ["DFIntDebugFRMQualityLevelOverride"]   = ("Force render quality level (1–21, lower = lighter)", "Performance"),
        ["FFlagDisablePostFx"]                  = ("Disable post-effects (bloom etc.) for better performance", "Performance"),
        ["FIntRenderShadowIntensity"]           = ("Shadow intensity (0 = no shadows)", "Performance"),
        ["DFIntCSGLevelOfDetailSwitchingDistance"] = ("CSG LOD switching distance (lower = lighter, allowlisted)", "Performance"),
        ["DFIntCSGLevelOfDetailSwitchingDistanceL12"] = ("CSG LOD switch distance L1→L2 (lower = lighter, allowlisted)", "Performance"),
        ["DFIntCSGLevelOfDetailSwitchingDistanceL23"] = ("CSG LOD switch distance L2→L3 (lower = lighter, allowlisted)", "Performance"),
        ["DFIntCSGLevelOfDetailSwitchingDistanceL34"] = ("CSG LOD switch distance L3→L4 (lower = lighter, allowlisted)", "Performance"),
        ["DFFlagTextureQualityOverrideEnabled"]    = ("Enable texture quality override (allowlisted)", "Performance"),
        ["DFIntTextureQualityOverride"]            = ("Force texture quality (0 = lowest = lighter, allowlisted)", "Performance"),
        ["FIntDebugForceMSAASamples"]              = ("Force MSAA sample count (0 = no AA = lighter, allowlisted)", "Performance"),
        ["FIntFRMMaxGrassDistance"]                = ("Max grass render distance (0 = no grass = lighter, allowlisted)", "Performance"),
        ["FIntFRMMinGrassDistance"]                = ("Min grass render distance (0 = no grass = lighter, allowlisted)", "Performance"),
        ["FIntGrassMovementReducedMotionFactor"]   = ("Reduce grass sway computation (allowlisted)", "Performance"),

        // Graphics
        ["FFlagDebugGraphicsDisableDirect3D11"] = ("Disable Direct3D11 (use for compatibility issues)", "Graphics"),
        ["FFlagGraphicsGLEnableHQShadersExclusion"] = ("Disable high-quality shader exclusion", "Graphics"),
        ["FIntRenderShadowmapBias"]             = ("Shadow map bias (reduces shadow flickering)", "Graphics"),
        ["FFlagRenderFixFog"]                   = ("Fix fog rendering bug", "Graphics"),
        ["FIntRenderLocalLightFadeDistance"]    = ("Set local light fade distance", "Graphics"),
        ["DFIntCullFactorPixelThresholdShadowMapHighQuality"] = ("High-quality shadow culling threshold", "Graphics"),
        ["FFlagEnableGPUPathTracing"]           = ("Enable GPU path tracing (requires high-end GPU)", "Graphics"),
        ["FIntRenderShadowMapSize"]             = ("Shadow map resolution (larger = higher quality)", "Graphics"),
        ["FFlagDebugSkyGray"]                   = ("Replace sky with flat gray (reduces sky render cost, allowlisted)", "Graphics"),
        ["FFlagHandleAltEnterFullscreenManually"] = ("Handle Alt+Enter fullscreen toggle manually (allowlisted)", "Graphics"),
        ["DFFlagDisableDPIScale"]               = ("Disable DPI scaling (allowlisted)", "Graphics"),
        ["FFlagDebugGraphicsPreferD3D11"]       = ("Force Direct3D11 graphics API (allowlisted)", "Graphics"),
        ["FFlagDebugGraphicsPreferVulkan"]      = ("Force Vulkan graphics API (allowlisted)", "Graphics"),
        ["FFlagDebugGraphicsPreferOpenGL"]      = ("Force OpenGL graphics API (allowlisted)", "Graphics"),
        ["DFFlagDebugPauseVoxelizer"]           = ("Pause terrain voxelizer (debug, allowlisted)", "Graphics"),

        // Network
        ["DFIntConnectionMTUSize"]              = ("Network MTU size (1400 recommended)", "Network"),
        ["DFIntRakNetResendBufferArrayLength"]  = ("Packet resend buffer size (larger = more stable)", "Network"),
        ["DFIntHttpCurlConnectionTimeoutMs"]    = ("HTTP connection timeout (ms) — increase for slow connections", "Network"),
        ["DFIntS2PhysicsSendRate"]              = ("Physics data send rate (higher = more accurate sync)", "Network"),
        ["DFIntMaxMissedWorldStepsRemembered"]  = ("Max physics steps to skip when lagging", "Network"),
        ["FFlagDebugDisableTimeoutDisconnect"]  = ("Disable timeout disconnect (for debugging)", "Network"),

        // UI
        ["FFlagNewAvatarLoadingUI"]             = ("Enable new avatar loading UI", "UI"),
        ["FFlagDisableNewIGMinDUA"]             = ("Disable in-game menu minimize UI", "UI"),
        ["FFlagEnableInGameMenuV3"]             = ("Enable in-game menu V3", "UI"),
        ["FFlagLuaAppSystemBar"]                = ("Enable LuaApp system bar", "UI"),
        ["FFlagEnableNewNotificationUI"]        = ("Enable new notification UI", "UI"),
        ["FIntChatTextSize"]                    = ("Set chat text size", "UI"),

        // Avatar
        ["FFlagEnableBetaFacialAnimation"]      = ("Enable beta facial animation", "Avatar"),
        ["FFlagAvatarSelfViewEnabled"]          = ("Enable self-view avatar in game", "Avatar"),
        ["FFlagEnableAvatarEditorPersistence"]  = ("Persist avatar editor settings", "Avatar"),
        ["FFlagAnimateLayeredClothingCollider"] = ("Enable layered clothing physics collider", "Avatar"),
    };

    public static (string Description, string Category) Lookup(string flagName)
    {
        if (Known.TryGetValue(flagName, out var info)) return info;

        var category = flagName switch
        {
            _ when flagName.StartsWith("FFlag")    => "Custom",
            _ when flagName.StartsWith("DFFlag")   => "Custom",
            _ when flagName.StartsWith("DFInt")    => "Custom",
            _ when flagName.StartsWith("FInt")     => "Custom",
            _ when flagName.StartsWith("FString")  => "Custom",
            _ => "Custom"
        };
        return (string.Empty, category);
    }
}
