using System.Text.Json.Serialization;

namespace NexStrap.Core.Models;

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

    [JsonIgnore]
    public bool IsModified { get; set; }
}

public static class FastFlagPresets
{
    public static IReadOnlyList<FastFlag> All => new List<FastFlag>
    {
        new() { Name = "DFIntTaskSchedulerTargetFps", Value = "144", Type = FastFlagType.Integer,
            Description = "FPS上限を設定します（デフォルト60）", Category = "パフォーマンス", IsPreset = true },
        new() { Name = "FFlagDebugGraphicsDisableDirect3D11", Value = "false", Type = FastFlagType.Boolean,
            Description = "Direct3D11を無効化", Category = "グラフィックス", IsPreset = true },
        new() { Name = "FFlagEnableQuickGameLaunch", Value = "true", Type = FastFlagType.Boolean,
            Description = "ゲームの高速起動を有効化", Category = "パフォーマンス", IsPreset = true },
        new() { Name = "DFIntConnectionMTUSize", Value = "1400", Type = FastFlagType.Integer,
            Description = "ネットワークMTUサイズ", Category = "ネットワーク", IsPreset = true },
        new() { Name = "FFlagGraphicsGLEnableHQShadersExclusion", Value = "false", Type = FastFlagType.Boolean,
            Description = "高品質シェーダーの除外を無効化", Category = "グラフィックス", IsPreset = true },
        new() { Name = "DFIntRakNetResendBufferArrayLength", Value = "128", Type = FastFlagType.Integer,
            Description = "ネットワーク再送バッファサイズ", Category = "ネットワーク", IsPreset = true },
        new() { Name = "FFlagNewAvatarLoadingUI", Value = "true", Type = FastFlagType.Boolean,
            Description = "新しいアバター読み込みUIを有効化", Category = "UI", IsPreset = true },
        new() { Name = "DFIntTextureCompositorActiveJobs", Value = "4", Type = FastFlagType.Integer,
            Description = "テクスチャ処理の並列ジョブ数", Category = "パフォーマンス", IsPreset = true },
        new() { Name = "FFlagDisableNewIGMinDUA", Value = "true", Type = FastFlagType.Boolean,
            Description = "インゲームメニューの最小化を無効化", Category = "UI", IsPreset = true },
        new() { Name = "DFIntHttpCurlConnectionTimeoutMs", Value = "30000", Type = FastFlagType.Integer,
            Description = "HTTPタイムアウト時間(ms)", Category = "ネットワーク", IsPreset = true },
        new() { Name = "FFlagEnableBetaFacialAnimation", Value = "true", Type = FastFlagType.Boolean,
            Description = "ベータ版フェイシャルアニメーションを有効化", Category = "アバター", IsPreset = true },
        new() { Name = "DFIntMaxFrameBufferSize", Value = "4", Type = FastFlagType.Integer,
            Description = "フレームバッファ最大サイズ", Category = "グラフィックス", IsPreset = true },
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
            Name = "グラフィックス軽量化",
            IconPath = "M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5C21.27 7.61 17 4.5 12 4.5zm0 12.5c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z",
            Flags =
            [
                new FastFlag { Name = "FFlagDisablePostFx",           Value = "true",  Type = FastFlagType.Boolean, Category = "グラフィックス" },
                new FastFlag { Name = "FFlagDisableShadows",          Value = "true",  Type = FastFlagType.Boolean, Category = "グラフィックス" },
                new FastFlag { Name = "FFlagDisableBloom",            Value = "true",  Type = FastFlagType.Boolean, Category = "グラフィックス" },
                new FastFlag { Name = "FFlagDisableDepthOfField",     Value = "true",  Type = FastFlagType.Boolean, Category = "グラフィックス" },
                new FastFlag { Name = "FFlagDisableGlobalShadows",    Value = "true",  Type = FastFlagType.Boolean, Category = "グラフィックス" },
                new FastFlag { Name = "DFIntTaskSchedulerTargetFps",  Value = "240",   Type = FastFlagType.Integer, Category = "パフォーマンス" },
            ]
        },
        new PresetGroup
        {
            Name = "レンダリング最適化",
            IconPath = "M7 2v11h3v9l7-12h-4l4-8z",
            Flags =
            [
                new FastFlag { Name = "FFlagEnableReducedLatency",    Value = "true",  Type = FastFlagType.Boolean, Category = "パフォーマンス" },
                new FastFlag { Name = "FFlagFastGPULightCulling3",    Value = "true",  Type = FastFlagType.Boolean, Category = "グラフィックス" },
                new FastFlag { Name = "FFlagRenderFixFog",            Value = "true",  Type = FastFlagType.Boolean, Category = "グラフィックス" },
                new FastFlag { Name = "FFlagRenderOptimizedShadows",  Value = "true",  Type = FastFlagType.Boolean, Category = "グラフィックス" },
            ]
        },
        new PresetGroup
        {
            Name = "メモリ / CPU",
            IconPath = "M9 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2h-4V1h-2v2h-4V1H9v2zM5 5h14v14H5V5zm6 6H7v2h4v4h2v-4h4v-2h-4V7h-2v4z",
            Flags =
            [
                new FastFlag { Name = "DFIntPhysicsStepsPerFrame",       Value = "1",   Type = FastFlagType.Integer, Category = "パフォーマンス" },
                new FastFlag { Name = "DFIntGCJobFrequencyMs",           Value = "250", Type = FastFlagType.Integer, Category = "パフォーマンス" },
                new FastFlag { Name = "FFlagLuaAppEnableLowMemoryMode",  Value = "true",Type = FastFlagType.Boolean, Category = "パフォーマンス" },
            ]
        },
        new PresetGroup
        {
            Name = "ネットワーク最適化",
            IconPath = "M1 9l2 2c4.97-4.97 13.03-4.97 18 0l2-2C16.93 2.93 7.08 2.93 1 9zm8 8l3 3 3-3c-1.65-1.66-4.34-1.66-6 0zm-4-4l2 2c2.76-2.76 7.24-2.76 10 0l2-2C15.14 9.14 8.87 9.14 5 13z",
            Flags =
            [
                new FastFlag { Name = "DFIntConnectionMTUSize",              Value = "1380", Type = FastFlagType.Integer, Category = "ネットワーク" },
                new FastFlag { Name = "DFIntRakNetResendTimeoutMS",          Value = "200",  Type = FastFlagType.Integer, Category = "ネットワーク" },
                new FastFlag { Name = "DFIntRakNetResendBufferArrayLength",  Value = "64",   Type = FastFlagType.Integer, Category = "ネットワーク" },
                new FastFlag { Name = "DFIntNetworkPrediction",              Value = "0",    Type = FastFlagType.Integer, Category = "ネットワーク" },
                new FastFlag { Name = "DFIntNetworkLatencyTolerance",        Value = "0",    Type = FastFlagType.Integer, Category = "ネットワーク" },
            ]
        },
    ];

    public static IReadOnlyList<FastFlag> AllFlags =>
        Groups.SelectMany(g => g.Flags).ToList();
}

// フラグ名 → (日本語説明, カテゴリ) の辞書。ClientAppSettings.json にあるフラグに説明を自動付与する。
public static class FlagDescriptions
{
    public static readonly Dictionary<string, (string Description, string Category)> Known = new()
    {
        // パフォーマンス
        ["DFIntTaskSchedulerTargetFps"]         = ("FPS上限を設定します（デフォルト: 60fps）", "パフォーマンス"),
        ["DFIntTextureCompositorActiveJobs"]    = ("テクスチャ処理の並列ジョブ数。多いほど読み込みが速くなります", "パフォーマンス"),
        ["FFlagEnableQuickGameLaunch"]          = ("ゲームの高速起動を有効化します", "パフォーマンス"),
        ["DFIntMaxFrameBufferSize"]             = ("フレームバッファの最大サイズ。小さいほど入力遅延が減ります", "パフォーマンス"),
        ["DFIntDebugFRMQualityLevelOverride"]   = ("レンダリング品質を強制設定します（1〜21、小さいほど軽量）", "パフォーマンス"),
        ["FFlagDisablePostFx"]                  = ("ポストエフェクト（ブルーム等）を無効化して軽量化します", "パフォーマンス"),
        ["FIntRenderShadowIntensity"]           = ("影の強度を設定します（0で影なし、軽量化に有効）", "パフォーマンス"),
        ["DFIntCSGLevelOfDetailSwitchingDistance"] = ("LOD（詳細度）の切り替え距離を調整します", "パフォーマンス"),

        // グラフィックス
        ["FFlagDebugGraphicsDisableDirect3D11"] = ("Direct3D11を無効化します（互換性問題の解決に使います）", "グラフィックス"),
        ["FFlagGraphicsGLEnableHQShadersExclusion"] = ("高品質シェーダーの除外を無効化します", "グラフィックス"),
        ["FIntRenderShadowmapBias"]             = ("シャドウマップのバイアスを調整します（影のちらつき軽減）", "グラフィックス"),
        ["FFlagRenderFixFog"]                   = ("フォグのレンダリングバグを修正します", "グラフィックス"),
        ["FIntRenderLocalLightFadeDistance"]    = ("ローカルライトのフェード距離を設定します", "グラフィックス"),
        ["DFIntCullFactorPixelThresholdShadowMapHighQuality"] = ("高品質シャドウのカリング閾値を設定します", "グラフィックス"),
        ["FFlagEnableGPUPathTracing"]           = ("GPUパストレーシングを有効化します（要高性能GPU）", "グラフィックス"),
        ["FIntRenderShadowMapSize"]             = ("シャドウマップの解像度を設定します（大きいほど高品質）", "グラフィックス"),

        // ネットワーク
        ["DFIntConnectionMTUSize"]              = ("ネットワークのMTUサイズ（1400推奨、大きいと通信効率が上がります）", "ネットワーク"),
        ["DFIntRakNetResendBufferArrayLength"]  = ("パケット再送バッファのサイズ（大きいほど安定します）", "ネットワーク"),
        ["DFIntHttpCurlConnectionTimeoutMs"]    = ("HTTP接続タイムアウト時間(ms)。回線が遅い場合は大きくします", "ネットワーク"),
        ["DFIntS2PhysicsSendRate"]              = ("物理演算データの送信レート（高いほど同期が正確になります）", "ネットワーク"),
        ["DFIntMaxMissedWorldStepsRemembered"]  = ("遅延時にスキップする物理ステップの最大数", "ネットワーク"),
        ["FFlagDebugDisableTimeoutDisconnect"]  = ("タイムアウト切断を無効化します（デバッグ用）", "ネットワーク"),

        // UI
        ["FFlagNewAvatarLoadingUI"]             = ("新しいアバター読み込みUIを有効化します", "UI"),
        ["FFlagDisableNewIGMinDUA"]             = ("インゲームメニューの最小化UIを無効化します", "UI"),
        ["FFlagEnableInGameMenuV3"]             = ("インゲームメニューV3を有効化します", "UI"),
        ["FFlagLuaAppSystemBar"]                = ("LuaAppのシステムバーを有効化します", "UI"),
        ["FFlagEnableNewNotificationUI"]        = ("新しい通知UIを有効化します", "UI"),
        ["FIntChatTextSize"]                    = ("チャットテキストのサイズを設定します", "UI"),

        // アバター
        ["FFlagEnableBetaFacialAnimation"]      = ("ベータ版フェイシャルアニメーションを有効化します", "アバター"),
        ["FFlagAvatarSelfViewEnabled"]          = ("ゲーム中の自分のアバタービューを有効化します", "アバター"),
        ["FFlagEnableAvatarEditorPersistence"]  = ("アバターエディターの設定を保持します", "アバター"),
        ["FFlagAnimateLayeredClothingCollider"] = ("レイヤードクロージングの物理コリジョンを有効化します", "アバター"),
    };

    public static (string Description, string Category) Lookup(string flagName)
    {
        if (Known.TryGetValue(flagName, out var info)) return info;

        // プレフィックスからカテゴリを推定
        var category = flagName switch
        {
            _ when flagName.StartsWith("FFlag")    => "カスタム",
            _ when flagName.StartsWith("DFFlag")   => "カスタム",
            _ when flagName.StartsWith("DFInt")    => "カスタム",
            _ when flagName.StartsWith("FInt")     => "カスタム",
            _ when flagName.StartsWith("FString")  => "カスタム",
            _ => "カスタム"
        };
        return (string.Empty, category);
    }
}
