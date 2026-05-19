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
