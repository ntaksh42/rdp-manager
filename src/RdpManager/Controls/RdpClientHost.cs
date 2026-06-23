using System.Runtime.InteropServices;
using System.Windows.Forms;
using RdpManager.Services;

namespace RdpManager.Controls;

/// <summary>RDP ActiveX の接続状態（IDispatch の Connected プロパティ値に対応）。</summary>
public enum RdpConnectionState { Disconnected = 0, Connected = 1, Connecting = 2 }

/// <summary>
/// RDP ActiveX コントロール（mstscax.dll）を AxHost としてホストするラッパー。
/// 型付き相互運用アセンブリ（AxMSTSCLib）を生成せず、IDispatch を dynamic で操作する。
/// </summary>
public sealed class RdpClientHost : AxHost
{
    // 既知 CLSID（ProgID が引けない環境向けのフォールバック）。新しい順に試す。
    private static readonly string[] CandidateClsids =
    {
        "3F859AA3-C2D4-4FAA-B0E4-FD0C9C4E5E3A", // v13
        "1DF7C823-B2D4-4B54-975A-F2AC5D7CF8B8", // v12
        "A0C63C30-F08D-4AB4-907C-34905D770C7D", // v11
        "8B918B82-7985-4C24-89DF-C33AD2BBFBCD", // v10
        "A3BC03A0-041D-42E3-AD22-882B7865C9C5", // v9
    };

    // ProgID 走査の上限版数（将来版にハードコード無しで追従するため）。
    private const int MaxProbeVersion = 20;

    private static string? _resolvedClsid;
    private dynamic? _ocx;

    public RdpClientHost() : base(ResolveClsid()) { }

    /// <summary>
    /// 実際に CoCreate できる CLSID を選ぶ。GetTypeFromCLSID は未登録でも非 null を返すため、
    /// 一部マシンで登録はあっても生成不可（CLASS_E_CLASSNOTAVAILABLE）の版を避けるには
    /// 実インスタンス化で確認する必要がある。結果は静的にキャッシュ。
    /// まず ProgID(MsRdpClientNNotSafeForScripting) を新しい順に走査し、将来版にも追従する。
    /// </summary>
    private static string ResolveClsid()
    {
        if (_resolvedClsid != null) return _resolvedClsid;

        // 1) ProgID から新しい順に探索（CLSID のハードコードに依存しない）
        for (int v = MaxProbeVersion; v >= 7; v--)
        {
            var type = Type.GetTypeFromProgID($"MsRdpClient{v}NotSafeForScripting");
            if (TryProbe(type, out var guid)) { _resolvedClsid = guid; return guid; }
        }

        // 2) 既知 CLSID 候補
        foreach (var clsid in CandidateClsids)
        {
            if (TryProbe(Type.GetTypeFromCLSID(new Guid(clsid)), out _))
            {
                _resolvedClsid = clsid;
                return clsid;
            }
        }

        // 3) 最後の手段（最古の既知版）
        Logger.Warn("No RDP ActiveX control could be instantiated; falling back to the oldest known CLSID.");
        _resolvedClsid = CandidateClsids[^1];
        return _resolvedClsid;
    }

    /// <summary>type を実体化して生成可否を確認し、成功なら CLSID 文字列を返す。</summary>
    private static bool TryProbe(Type? type, out string clsid)
    {
        clsid = "";
        if (type is null) return false;
        try
        {
            var probe = Activator.CreateInstance(type);
            if (probe is null) return false;
            Marshal.FinalReleaseComObject(probe);
            clsid = type.GUID.ToString();
            return true;
        }
        catch
        {
            return false; // この版は生成不可。次の候補へ。
        }
    }

    public string? LastError { get; private set; }

    /// <summary>ハンドルを確実に生成して OCX(dynamic) を返す。</summary>
    private dynamic GetClient()
    {
        if (!IsHandleCreated) CreateControl();
        return _ocx ??= GetOcx()!;
    }

    /// <summary>埋め込み検証用: ハンドル生成・OCX取得・プロパティ読み書きを確認。</summary>
    public string SelfCheck()
    {
        dynamic ocx = GetClient();
        ocx.Server = "192.0.2.1";
        string server = ocx.Server;
        int connected = (int)ocx.Connected;
        return $"OK: clsid={_resolvedClsid}, OCX実体化/読み書き成功 (Server={server}, Connected={connected})";
    }

    public RdpConnectionState ConnectionState
    {
        get
        {
            try
            {
                var o = _ocx;
                return o is null ? RdpConnectionState.Disconnected : (RdpConnectionState)(int)o.Connected;
            }
            catch { return RdpConnectionState.Disconnected; }
        }
    }

    public void Connect(LaunchInfo info)
    {
        try
        {
            dynamic ocx = GetClient();

            ocx.Server = info.Host;
            if (!string.IsNullOrEmpty(info.Username)) ocx.UserName = info.Username;
            if (!string.IsNullOrEmpty(info.Domain)) ocx.Domain = info.Domain;

            int w = Width > 0 ? Width : 1280;
            int h = Height > 0 ? Height : 800;
            TrySet(() => ocx.DesktopWidth = w, "DesktopWidth");
            TrySet(() => ocx.DesktopHeight = h, "DesktopHeight");
            TrySet(() => ocx.ColorDepth = 32, "ColorDepth");

            dynamic adv = ocx.AdvancedSettings9;
            if (info.Port != 3389) TrySet(() => adv.RDPPort = info.Port, "RDPPort");
            if (!string.IsNullOrEmpty(info.Password)) TrySet(() => adv.ClearTextPassword = info.Password, "ClearTextPassword");
            TrySet(() => adv.SmartSizing = info.SmartSizing, "SmartSizing");
            TrySet(() => adv.RedirectDrives = info.RedirectDrives, "RedirectDrives");
            TrySet(() => adv.RedirectClipboard = info.RedirectClipboard, "RedirectClipboard");
            TrySet(() => adv.EnableCredSspSupport = true, "EnableCredSspSupport");
            // サーバー証明書の検証レベル（既定 2 = 不一致で接続不可）。
            TrySet(() => adv.AuthenticationLevel = (uint)info.AuthenticationLevel, "AuthenticationLevel");
            TrySet(() => adv.DisplayConnectionBar = false, "DisplayConnectionBar");
            if (info.UseMultimon) TrySet(() => adv.UseMultimon = true, "UseMultimon");
            // Windows キー組み合わせ(Alt+Tab, Win, Ctrl+Alt+End 等)を常にリモートへ送る。
            // KeyboardHookMode は SecuredSettings 側のプロパティ（既定は「コントロール全画面時のみ」）。
            TrySet(() => ocx.SecuredSettings.KeyboardHookMode = 1, "SecuredSettings.KeyboardHookMode");
            TrySet(() => ocx.SecuredSettings3.KeyboardHookMode = 1, "SecuredSettings3.KeyboardHookMode");

            // ── 描画パフォーマンス最適化 ──
            // 再描画削減（視覚変化なしの純粋な高速化）: 永続ビットマップキャッシュ
            TrySet(() => adv.BitmapPeristence = 1, "BitmapPeristence");  // ※API名のスペルは "Peristence"
            TrySet(() => adv.CachePersistenceActive = 1, "CachePersistenceActive");
            // 帯域/コーデック自動最適化のヒント（LAN=6）
            TrySet(() => adv.NetworkConnectionType = 6u, "NetworkConnectionType");
            if (info.PerformanceMode)
            {
                // サーバー側の装飾を無効化（壁紙/フルウィンドウドラッグ/メニューアニメ/テーマ/カーソル影）
                // TS_PERF_DISABLE_WALLPAPER|FULLWINDOWDRAG|MENUANIMATIONS|THEMING|CURSOR_SHADOW = 0x2F
                TrySet(() => adv.PerformanceFlags = 0x2F, "PerformanceFlags");
            }

            if (!string.IsNullOrWhiteSpace(info.Gateway))
            {
                dynamic ts = ocx.TransportSettings2;
                TrySet(() => ts.GatewayUsageMethod = 1u, "GatewayUsageMethod");
                TrySet(() => ts.GatewayProfileUsageMethod = 1u, "GatewayProfileUsageMethod");
                TrySet(() => ts.GatewayHostname = info.Gateway!, "GatewayHostname");
            }

            ocx.Connect();
            LastError = null;
            Logger.Info($"RDP connect initiated: {info.Host}:{info.Port}");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Logger.Error($"RDP connect failed: {info.Host}:{info.Port}", ex);
        }
    }

    /// <summary>
    /// コントロールの DPI から RDP のスケール率を求める。
    /// DesktopScaleFactor は {100,125,150,175,200,...}、DeviceScaleFactor は {100,140,180} のみ許容。
    /// </summary>
    private (uint desktop, uint device) GetScaleFactors()
    {
        int dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        int pct = dpi * 100 / 96;
        uint desktop = pct >= 500 ? 500u : pct >= 450 ? 450u : pct >= 400 ? 400u : pct >= 350 ? 350u
            : pct >= 300 ? 300u : pct >= 250 ? 250u : pct >= 225 ? 225u : pct >= 200 ? 200u
            : pct >= 175 ? 175u : pct >= 150 ? 150u : pct >= 125 ? 125u : 100u;
        uint device = pct >= 180 ? 180u : pct >= 140 ? 140u : 100u;
        return (desktop, device);
    }

    /// <summary>
    /// 接続中セッションのリモート解像度を指定サイズに合わせる（動的解像度, RDP 8.1+）。
    /// 非対応サーバーでは例外になるため、その場合は SmartSizing による拡縮にフォールバック。
    /// </summary>
    public void ResizeRemote(int width, int height)
    {
        try
        {
            var o = _ocx;
            if (o is null || (int)o.Connected != 1) return;
            uint w = (uint)Math.Clamp(width & ~1, 200, 8192);   // 偶数・範囲内
            uint h = (uint)Math.Clamp(height & ~1, 200, 8192);
            var (desktopScale, deviceScale) = GetScaleFactors();
            o.UpdateSessionDisplaySettings(w, h, w, h, 0u, desktopScale, deviceScale);
        }
        catch
        {
            // 動的解像度に非対応 → スマートサイジングで追従（拡縮表示）
            TrySet(() => { if (_ocx is { } o) o.AdvancedSettings9.SmartSizing = true; }, "SmartSizing(fallback)");
        }
    }

    public void DisconnectSession()
    {
        try
        {
            if (_ocx is { } o && (int)o.Connected != 0) o.Disconnect();
        }
        catch { /* ignore */ }
    }

    private static void TrySet(Action set, string name)
    {
        try { set(); }
        catch (Exception ex)
        {
            // プロパティ未対応はスキップ。どのプロパティが効かなかったかはログで追跡できる。
            Logger.Warn($"RDP property '{name}' could not be applied: {ex.Message}");
        }
    }
}
