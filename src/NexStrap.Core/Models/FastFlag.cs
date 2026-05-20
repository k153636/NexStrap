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
