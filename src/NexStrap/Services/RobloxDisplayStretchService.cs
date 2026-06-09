using System.Runtime.InteropServices;

namespace NexStrap.Services;

public sealed class RobloxDisplayStretchService
{
    // -------------------------------------------------------------------------
    // Display Scaling — SetDisplayConfig で GPU/モニター設定を上書き
    // DISPLAYCONFIG_SCALING_STRETCHED(3) = 黒帯なし強制引き伸ばし
    // -------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    private struct ScLuid { public uint L; public int H; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ScRational { public uint N; public uint D; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScPathSrc { public ScLuid adapterId; public uint id, modeIdx, flags; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ScPathTgt {
        public ScLuid adapterId;                           // 8
        public uint id, modeIdx, outTech, rotation;        // 4×4=16
        public uint scaling;                               // 4  ← DISPLAYCONFIG_SCALING
        public ScRational refreshRate;                     // 8
        public uint scanLineOrder;                         // 4
        public byte available;                             // 1  ← BOOLEAN は BYTE (1バイト)
        public byte _pad0, _pad1, _pad2;                   // 3  padding
        public uint statusFlags;                           // 4
    }   // total = 48 bytes

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ScPath { public ScPathSrc src; public ScPathTgt tgt; public uint flags; }
    // total = 20 + 48 + 4 = 72 bytes

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct ScMode {
        // DISPLAYCONFIG_MODE_INFO = 4(infoType)+4(id)+8(luid)+48(union) = 64 bytes
        [FieldOffset( 0)] public uint infoType;
        [FieldOffset( 4)] public uint id;
        [FieldOffset( 8)] public ScLuid adapterId;
        [FieldOffset(16)] public long _u0; [FieldOffset(24)] public long _u1;
        [FieldOffset(32)] public long _u2; [FieldOffset(40)] public long _u3;
        [FieldOffset(48)] public long _u4; [FieldOffset(56)] public long _u5;
    }

    [DllImport("user32.dll")] private static extern uint GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    // IntPtr 版 — struct marshaling を完全にバイパスしてオフセット直接操作
    [DllImport("user32.dll", EntryPoint = "QueryDisplayConfig")]
    private static extern uint QueryDisplayConfigPtr(uint f, ref uint np, IntPtr pa, ref uint nm, IntPtr ma, IntPtr id);
    [DllImport("user32.dll", EntryPoint = "SetDisplayConfig")]
    private static extern uint SetDisplayConfigPtr(uint np, IntPtr pa, uint nm, IntPtr ma, uint flags);

    private const uint QDC_ACTIVE   = 0x2;
    private const uint QDC_VMAWARE  = 0x10;
    private const uint SDC_APPLY    = 0x200;
    private const uint SDC_SUPPLIED = 0x10;
    private const uint SDC_CHANGES  = 0x1000;
    private const uint SDC_NO_OPT   = 0x400;
    private const uint SDC_VMAWARE  = 0x80000;
    private const uint SC_STRETCHED = 3;
    private const uint SC_ASPECT    = 4;
    private uint _origScaling = SC_ASPECT;

    // DISPLAYCONFIG_PATH_INFO 内のフィールドオフセット
    // sourceInfo(20) + targetInfo: adapterId(8)+id(4)+modeIdx(4)+outTech(4)+rot(4)+scaling(4)=offset 44
    private const int PATH_SIZE       = 72;
    private const int PATH_SRC_MIDX   = 12;  // sourceInfo.modeInfoIdx
    private const int PATH_TGT_MIDX   = 32;  // targetInfo.modeInfoIdx  (20+12)
    private const int PATH_TGT_OTECH  = 36;  // targetInfo.outputTechnology (20+16)
    private const int PATH_TGT_SCALE  = 44;  // targetInfo.scaling (20+24)

    /// <summary>
    /// SetDisplayConfig でソースモードの解像度を変更する。
    /// ChangeDisplaySettings と違い完全モード再設定を行うため
    /// Intel/NVIDIA ドライバーのデフォルトパネルスケール（フルパネル）が適用され黒帯が出ない。
    /// </summary>
    private const uint INTERNAL_DISPLAY = 0x80000000; // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL

    /// <summary>
    /// QueryDisplayConfig で現在の設定を取得し、無変更で SetDisplayConfig に渡す。
    /// CCD パスを通ることで Intel/NVIDIA ドライバーがフルモード再設定を行い
    /// フルパネルスケール（引き伸ばし）が適用される。
    /// Windows Settings でリフレッシュレートを変更するのと同じ効果。
    /// </summary>
    /// <summary>
    /// 1280×960 で利用可能な「現在とは異なる Hz」を探して一時的に切り替え、すぐ戻す。
    /// Windows Settings でリフレッシュレートを変更すると黒帯が消える現象を自動再現。
    /// </summary>
    private void TriggerFullModeSetByHzToggle(int width, int height)
    {
        try
        {
            var cur = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref cur)) return;
            int origHz = cur.dmDisplayFrequency;
            RobloxService.Log($"TriggerHzToggle: current Hz={origHz} at {width}x{height}");

            // 現在と違う Hz を探す
            int altHz = -1;
            for (int n = 0; ; n++)
            {
                var m = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (!EnumDisplaySettings(null, n, ref m)) break;
                if (m.dmPelsWidth == width && m.dmPelsHeight == height &&
                    m.dmDisplayFrequency != origHz && m.dmDisplayFrequency > 0)
                {
                    altHz = m.dmDisplayFrequency;
                    break;
                }
            }

            if (altHz < 0) { RobloxService.Log("TriggerHzToggle: no alternate Hz found"); return; }

            RobloxService.Log($"TriggerHzToggle: switching {origHz}Hz → {altHz}Hz → {origHz}Hz");
            const int DM_FREQ = 0x400000;

            // 別 Hz に切り替え（Intel ドライバーのフルモード再設定を起動）
            var dm1 = cur;
            dm1.dmPelsWidth = width; dm1.dmPelsHeight = height;
            dm1.dmDisplayFrequency = altHz;
            dm1.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_FREQ;
            var r1 = ChangeDisplaySettings(ref dm1, 0);
            RobloxService.Log($"TriggerHzToggle switch to {altHz}Hz: {r1}");

            // 元の Hz に戻す
            var dm2 = cur;
            dm2.dmPelsWidth = width; dm2.dmPelsHeight = height;
            dm2.dmDisplayFrequency = origHz;
            dm2.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_FREQ;
            var r2 = ChangeDisplaySettings(ref dm2, 0);
            RobloxService.Log($"TriggerHzToggle restore to {origHz}Hz: {r2}");
        }
        catch (Exception ex) { RobloxService.Log($"TriggerHzToggle: {ex.Message}"); }
    }

    /// <summary>
    /// QDC_ONLY_ACTIVE_PATHS で取得した target mode の Hz を 1 フレーム分だけ変えて
    /// SetDisplayConfig で適用する。Windows Settings の Hz 変更と同じ操作。
    /// </summary>
    private void TrySetDisplayConfigWithHzNudge()
    {
        try
        {
            const uint QF = QDC_ACTIVE; // NO virtual-mode-aware → tgtModeIdx が直接インデックス
            if (GetDisplayConfigBufferSizes(QF, out uint np, out uint nm) != 0) return;

            const int MODE_SIZE = 64;
            var pBuf = Marshal.AllocHGlobal(PATH_SIZE * (int)np);
            var mBuf = Marshal.AllocHGlobal(MODE_SIZE * (int)nm);
            try
            {
                uint np2 = np, nm2 = nm;
                if (QueryDisplayConfigPtr(QF, ref np2, pBuf, ref nm2, mBuf, IntPtr.Zero) != 0) return;

                // 内蔵ディスプレイの path を探して tgtModeIdx を取得
                for (int i = 0; i < np2; i++)
                {
                    uint ot = (uint)Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_TGT_OTECH);
                    if (ot != INTERNAL_DISPLAY) continue;

                    int tgtIdx = Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_TGT_MIDX);
                    RobloxService.Log($"HzNudge: internal path[{i}] tgtModeIdx={tgtIdx} nm={nm2}");
                    if (tgtIdx < 0 || tgtIdx >= nm2) { RobloxService.Log("HzNudge: invalid tgtIdx"); break; }

                    // target mode: DISPLAYCONFIG_VIDEO_SIGNAL_INFO の pixelRate は offset 16
                    // hSyncFreq(N=20,D=24), vSyncFreq(N=28,D=32)
                    int mBase = tgtIdx * MODE_SIZE + 16; // union start in MODE_INFO
                    uint pixRateHi = (uint)Marshal.ReadInt32(mBuf, mBase + 4); // high 32 of UINT64
                    uint vNumer   = (uint)Marshal.ReadInt32(mBuf, mBase + 20);
                    uint vDenom   = (uint)Marshal.ReadInt32(mBuf, mBase + 24);
                    RobloxService.Log($"HzNudge: vSyncFreq={vNumer}/{vDenom} ({(vDenom>0?(double)vNumer/vDenom:0):F2}Hz)");

                    // Hz を +1 してから元に戻す（各 SetDisplayConfig を独立して試みる）
                    uint origNumer = vNumer;
                    Marshal.WriteInt32(mBuf, mBase + 20, (int)(vNumer + vDenom)); // +1Hz
                    var r1 = SetDisplayConfigPtr(np2, pBuf, nm2, mBuf,
                        SDC_SUPPLIED | SDC_APPLY | SDC_CHANGES | SDC_NO_OPT);
                    RobloxService.Log($"HzNudge +1Hz result={r1}");

                    Marshal.WriteInt32(mBuf, mBase + 20, (int)origNumer); // restore
                    var r2 = SetDisplayConfigPtr(np2, pBuf, nm2, mBuf,
                        SDC_SUPPLIED | SDC_APPLY | SDC_CHANGES | SDC_NO_OPT);
                    RobloxService.Log($"HzNudge restore result={r2}");
                    break;
                }
            }
            finally { Marshal.FreeHGlobal(pBuf); Marshal.FreeHGlobal(mBuf); }
        }
        catch (Exception ex) { RobloxService.Log($"TrySetDisplayConfigWithHzNudge: {ex.Message}"); }
    }

    private void ReapplyCurrentConfigViaSetDisplayConfig()
    {
        try
        {
            const uint SDC_USE_DB_CURRENT = 0xF; // SDC_TOPOLOGY_INTERNAL|CLONE|EXTEND|EXTERNAL
            const uint SDC_SAVE_DB        = 0x800;

            // 試行0: QDC_ONLY_ACTIVE_PATHS (no VMAWARE) で target mode の Hz を微調整して再適用
            // → Windows Settings が Hz 変更に使う SetDisplayConfig と同等の操作
            TrySetDisplayConfigWithHzNudge();

            // 試行1: データベースの現在トポロジを再適用（パスなし）
            // Windows Settings が内部的に行うことと同等
            var r0 = SetDisplayConfigPtr(0, IntPtr.Zero, 0, IntPtr.Zero,
                SDC_USE_DB_CURRENT | SDC_APPLY);
            RobloxService.Log($"ReapplyUseDbCurrent result={r0}");
            if (r0 == 0) return;

            // 試行2: オーバーサイズバッファで QueryDisplayConfig → SetDisplayConfig
            // PATH_SIZE の計算ミスによるバッファオーバーフロー回避のため 256 バイト/要素で確保
            foreach (uint qf in new uint[] { QDC_ACTIVE, QDC_ACTIVE | QDC_VMAWARE })
            {
                if (GetDisplayConfigBufferSizes(qf, out uint np, out uint nm) != 0) continue;

                const int OVERSIZED_PATH = 256;
                const int OVERSIZED_MODE = 256;
                var pBuf = Marshal.AllocHGlobal(OVERSIZED_PATH * (int)np);
                var mBuf = Marshal.AllocHGlobal(OVERSIZED_MODE * (int)nm);
                try
                {
                    // ゼロ初期化
                    for (int i = 0; i < OVERSIZED_PATH * np; i++) Marshal.WriteByte(pBuf, i, 0);
                    for (int i = 0; i < OVERSIZED_MODE * nm; i++) Marshal.WriteByte(mBuf, i, 0);

                    uint np2 = np, nm2 = nm;
                    var qr = QueryDisplayConfigPtr(qf, ref np2, pBuf, ref nm2, mBuf, IntPtr.Zero);
                    RobloxService.Log($"QueryDisplayConfig qf=0x{qf:X} oversized: result={qr} np={np2} nm={nm2}");
                    if (qr != 0) continue;

                    foreach (uint flags in new uint[] {
                        SDC_SUPPLIED | SDC_APPLY,
                        SDC_SUPPLIED | SDC_APPLY | SDC_CHANGES,
                        SDC_SUPPLIED | SDC_APPLY | SDC_NO_OPT | SDC_CHANGES,
                        SDC_SUPPLIED | SDC_APPLY | SDC_NO_OPT | SDC_VMAWARE | SDC_CHANGES,
                        SDC_SUPPLIED | SDC_APPLY | SDC_SAVE_DB | SDC_CHANGES,
                    })
                    {
                        var r = SetDisplayConfigPtr(np2, pBuf, nm2, mBuf, flags);
                        RobloxService.Log($"Reapply oversized qf=0x{qf:X} flags=0x{flags:X} result={r}");
                        if (r == 0) return;
                    }
                }
                finally { Marshal.FreeHGlobal(pBuf); Marshal.FreeHGlobal(mBuf); }
            }
        }
        catch (Exception ex) { RobloxService.Log($"ReapplyCurrentConfig: {ex.Message}"); }
    }

    private bool ApplyResolutionViaSetDisplayConfig(int width, int height)
    {
        try
        {
            uint qf = QDC_ACTIVE | QDC_VMAWARE;
            if (GetDisplayConfigBufferSizes(qf, out uint np, out uint nm) != 0) return false;

            foreach (int modeSize in new[] { 64, 80 })
            {
                var pBuf = Marshal.AllocHGlobal(PATH_SIZE * (int)np);
                var mBuf = Marshal.AllocHGlobal(modeSize * (int)nm);
                try
                {
                    uint np2 = np, nm2 = nm;
                    if (QueryDisplayConfigPtr(qf, ref np2, pBuf, ref nm2, mBuf, IntPtr.Zero) != 0) continue;

                    // 内蔵パネル (INTERNAL) のパスが参照するソースモードインデックスを収集
                    // デュアルモニター時に外部モニター非対応解像度で全体が失敗するのを防ぐ
                    var internalSrcIdx = new System.Collections.Generic.HashSet<int>();
                    for (int i = 0; i < np2; i++)
                    {
                        uint outTech  = (uint)Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_TGT_OTECH);
                        int srcModeIdx = Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_SRC_MIDX);
                        int tgtModeIdx = Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_TGT_MIDX);
                        int pathFlags  = Marshal.ReadInt32(pBuf, i * PATH_SIZE + (PATH_SIZE - 4));
                        RobloxService.Log($"  path[{i}] outTech=0x{outTech:X} srcIdx={srcModeIdx} tgtIdx={tgtModeIdx} pathFlags=0x{pathFlags:X}");
                        if (outTech == INTERNAL_DISPLAY)
                        {
                            internalSrcIdx.Add(srcModeIdx & 0xFFFF);
                        }
                    }

                    if (internalSrcIdx.Count == 0) { RobloxService.Log("No internal display found"); continue; }

                    // 内蔵パネルのソースモードのみ変更
                    bool changed = false;
                    for (int i = 0; i < nm2; i++)
                    {
                        int baseOff = i * modeSize;
                        if ((uint)Marshal.ReadInt32(mBuf, baseOff) != 1) continue; // SOURCE mode only
                        if (!internalSrcIdx.Contains(i)) continue; // internal only

                        int origW = Marshal.ReadInt32(mBuf, baseOff + 16);
                        int origH = Marshal.ReadInt32(mBuf, baseOff + 20);
                        RobloxService.Log($"  Modifying mode[{i}] modeSize={modeSize} {origW}x{origH} → {width}x{height}");
                        Marshal.WriteInt32(mBuf, baseOff + 16, width);
                        Marshal.WriteInt32(mBuf, baseOff + 20, height);
                        changed = true;
                    }
                    if (!changed) { RobloxService.Log($"No internal SOURCE mode found (modeSize={modeSize})"); continue; }

                    uint flags = SDC_SUPPLIED | SDC_APPLY | SDC_CHANGES | SDC_NO_OPT | SDC_VMAWARE;
                    var r = SetDisplayConfigPtr(np2, pBuf, nm2, mBuf, flags);
                    RobloxService.Log($"SetDisplayConfig internal resolution {width}x{height} (modeSize={modeSize}) result={r}");
                    if (r == 0) return true;
                }
                finally { Marshal.FreeHGlobal(pBuf); Marshal.FreeHGlobal(mBuf); }
            }
        }
        catch (Exception ex) { RobloxService.Log($"ApplyResolutionViaSetDisplayConfig: {ex.Message}"); }
        return false;
    }

    private void ApplyDisplayScaling(bool stretch)
    {
        try
        {
            uint qf = QDC_ACTIVE | QDC_VMAWARE;
            if (GetDisplayConfigBufferSizes(qf, out uint np, out uint nm) != 0) { RobloxService.Log("GetDisplayConfigBufferSizes failed"); return; }
            RobloxService.Log($"DisplayConfig buffers: paths={np} modes={nm}");

            // sizeof(DISPLAYCONFIG_MODE_INFO): 64 or 80 depending on SDK/Windows version → try both
            foreach (int modeSize in new[] { 64, 80 })
            {
                var pBuf = Marshal.AllocHGlobal(PATH_SIZE * (int)np);
                var mBuf = Marshal.AllocHGlobal(modeSize * (int)nm);
                try
                {
                    uint np2 = np, nm2 = nm;
                    var qr = QueryDisplayConfigPtr(qf, ref np2, pBuf, ref nm2, mBuf, IntPtr.Zero);
                    if (qr != 0) { RobloxService.Log($"QueryDisplayConfig(modeSize={modeSize}) fail={qr}"); continue; }

                    for (int i = 0; i < np2; i++)
                    {
                        int curScale = Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_TGT_SCALE);
                        int outTech  = Marshal.ReadInt32(pBuf, i * PATH_SIZE + PATH_TGT_OTECH);
                        RobloxService.Log($"  path[{i}] modeSize={modeSize} scaling={curScale} outTech=0x{outTech:X}");
                        if (stretch && i == 0) _origScaling = (uint)curScale;
                    }

                    uint target = stretch ? SC_STRETCHED : _origScaling;
                    for (int i = 0; i < np2; i++)
                        Marshal.WriteInt32(pBuf, i * PATH_SIZE + PATH_TGT_SCALE, (int)target);

                    // 試行A: modes ありで適用
                    uint fA = SDC_SUPPLIED | SDC_APPLY | SDC_CHANGES | SDC_NO_OPT | SDC_VMAWARE;
                    var rA = SetDisplayConfigPtr(np2, pBuf, nm2, mBuf, fA);
                    RobloxService.Log($"SetDisplayConfig A (modeSize={modeSize}) scaling={target} result={rA}");
                    if (rA == 0) return;

                    // 試行B: modeIdx を INVALID に設定して modes なし
                    for (int i = 0; i < np2; i++)
                    {
                        Marshal.WriteInt32(pBuf, i * PATH_SIZE + PATH_SRC_MIDX, -1);
                        Marshal.WriteInt32(pBuf, i * PATH_SIZE + PATH_TGT_MIDX, -1);
                    }
                    uint fB = SDC_SUPPLIED | SDC_APPLY | SDC_CHANGES | SDC_VMAWARE;
                    var rB = SetDisplayConfigPtr(np2, pBuf, 0, IntPtr.Zero, fB);
                    RobloxService.Log($"SetDisplayConfig B (modeSize={modeSize},noModes) scaling={target} result={rB}");
                    if (rB == 0) return;
                }
                finally { Marshal.FreeHGlobal(pBuf); Marshal.FreeHGlobal(mBuf); }
            }
            RobloxService.Log("All SetDisplayConfig attempts failed");
        }
        catch (Exception ex) { RobloxService.Log($"ApplyDisplayScaling: {ex.Message}"); }
    }

    // -------------------------------------------------------------------------
    // Stretched Resolution — ChangeDisplaySettings (Win32)
    // -------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int   dmFields;
        public int   dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int   dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int   dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public int   dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll")]
    private static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DM_PELSWIDTH  = 0x80000;
    private const int DM_PELSHEIGHT = 0x100000;
    private const int DISP_CHANGE_SUCCESSFUL = 0;

    private DEVMODE _originalDevMode;
    private bool    _stretchActive;
    private bool    _originalFullscreen;

    public bool IsStretchActive => _stretchActive;

    // GlobalBasicSettings_13.xml のパス（全 Roblox アカウント共通）
    private static readonly string GlobalBasicSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "GlobalBasicSettings_13.xml");

    /// <summary>
    /// GlobalBasicSettings の bool 値を書き換える。
    /// name="Fullscreen" → true/false
    /// </summary>
    private static bool SetGlobalBasicBool(string name, bool value)
    {
        try
        {
            if (!File.Exists(GlobalBasicSettingsPath)) return false;
            var xml = File.ReadAllText(GlobalBasicSettingsPath);
            var updated = System.Text.RegularExpressions.Regex.Replace(
                xml,
                $@"<bool name=""{name}"">(true|false)</bool>",
                $"<bool name=\"{name}\">{(value ? "true" : "false")}</bool>");
            File.WriteAllText(GlobalBasicSettingsPath, updated);
            return true;
        }
        catch { return false; }
    }

    private static bool GetGlobalBasicBool(string name)
    {
        try
        {
            if (!File.Exists(GlobalBasicSettingsPath)) return false;
            var xml = File.ReadAllText(GlobalBasicSettingsPath);
            var m = System.Text.RegularExpressions.Regex.Match(
                xml, $@"<bool name=""{name}"">(true|false)</bool>");
            return m.Success && m.Groups[1].Value == "true";
        }
        catch { return false; }
    }

    public bool ApplyStretchResolution(int width, int height)
    {
        // 既に適用済みなら二重適用しない
        if (_stretchActive) return true;

        // 現在の解像度を保存
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)) return false;
        _originalDevMode = dm;

        // 1. Roblox を強制フルスクリーンに設定（ウィンドウモードでは GPU stretch が効かない）
        _originalFullscreen = GetGlobalBasicBool("Fullscreen");
        SetGlobalBasicBool("Fullscreen", true);
        RobloxService.Log($"Roblox Fullscreen set to true (was {_originalFullscreen})");

        // 2. SetDisplayConfig でソース解像度を変更（完全モード再設定 → ドライバーがフルパネルスケールを適用）
        //    失敗時は従来の ChangeDisplaySettings にフォールバック
        bool ok;
        if (ApplyResolutionViaSetDisplayConfig(width, height))
        {
            ok = true;
            _stretchActive = true;
            RobloxService.Log($"Stretch resolution applied via SetDisplayConfig: {width}x{height}");
        }
        else
        {
            // SetDisplayConfig は NVIDIA Optimus + Intel iGPU 環境で全パターン result=87
            // → ChangeDisplaySettings で解像度のみ変更（黒帯はユーザーが Intel GCC で設定）
            dm.dmPelsWidth  = width;
            dm.dmPelsHeight = height;
            dm.dmFields     = DM_PELSWIDTH | DM_PELSHEIGHT;
            ok = ChangeDisplaySettings(ref dm, 0) == DISP_CHANGE_SUCCESSFUL;
            if (ok) _stretchActive = true;
            RobloxService.Log(ok ? $"Stretch resolution applied: {width}x{height}" : $"Stretch resolution failed: {width}x{height}");
        }
        return ok;
    }

    public void RestoreResolution()
    {
        if (!_stretchActive) return;

        // 表示解像度を復元
        ChangeDisplaySettings(ref _originalDevMode, 0);
        _stretchActive = false;
        RobloxService.Log("Display resolution restored");

        // Windows スケーリングを元に戻す
        ApplyDisplayScaling(false);

        // Roblox のフルスクリーン設定を元に戻す
        SetGlobalBasicBool("Fullscreen", _originalFullscreen);
        RobloxService.Log($"Roblox Fullscreen restored to {_originalFullscreen}");
    }

}