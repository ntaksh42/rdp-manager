using System.Runtime.InteropServices;
using System.Windows.Forms;
using RdpManager.Services;

namespace RdpManager.Controls;

/// <summary>
/// RDP ActiveX コントロール（mstscax.dll）を AxHost としてホストするラッパー。
/// 型付き相互運用アセンブリ（AxMSTSCLib）を生成せず、IDispatch を dynamic で操作する。
/// </summary>
public sealed class RdpClientHost : AxHost
{
    // 新しい順に試す。"NotSafeForScripting"（完全制御）クラスの CLSID（本機の HKCR から確認済み）。
    private static readonly string[] CandidateClsids =
    {
        "3F859AA3-C2D4-4FAA-B0E4-FD0C9C4E5E3A", // v13
        "1DF7C823-B2D4-4B54-975A-F2AC5D7CF8B8", // v12
        "A0C63C30-F08D-4AB4-907C-34905D770C7D", // v11
        "8B918B82-7985-4C24-89DF-C33AD2BBFBCD", // v10
        "A3BC03A0-041D-42E3-AD22-882B7865C9C5", // v9
    };

    private static string? _resolvedClsid;
    private dynamic? _ocx;

    public RdpClientHost() : base(ResolveClsid()) { }

    /// <summary>
    /// 実際に CoCreate できる CLSID を選ぶ。GetTypeFromCLSID は未登録でも非 null を返すため、
    /// 一部マシンで登録はあっても生成不可（CLASS_E_CLASSNOTAVAILABLE）の版を避けるには
    /// 実インスタンス化で確認する必要がある。結果は静的にキャッシュ。
    /// </summary>
    private static string ResolveClsid()
    {
        if (_resolvedClsid != null) return _resolvedClsid;
        foreach (var clsid in CandidateClsids)
        {
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid(clsid));
                if (type is null) continue;
                var probe = Activator.CreateInstance(type);
                if (probe is null) continue;
                Marshal.FinalReleaseComObject(probe);
                _resolvedClsid = clsid;
                return clsid;
            }
            catch
            {
                // この版は生成不可。次の候補へ。
            }
        }
        _resolvedClsid = CandidateClsids[^1];
        return _resolvedClsid;
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

    /// <summary>0=切断 / 1=接続済み / 2=接続中。</summary>
    public int ConnectionState
    {
        get
        {
            try
            {
                var o = _ocx;
                return o is null ? 0 : (int)o.Connected;
            }
            catch { return 0; }
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
            TrySet(() => ocx.DesktopWidth = w);
            TrySet(() => ocx.DesktopHeight = h);
            TrySet(() => ocx.ColorDepth = 32);

            dynamic adv = ocx.AdvancedSettings9;
            if (info.Port != 3389) TrySet(() => adv.RDPPort = info.Port);
            if (!string.IsNullOrEmpty(info.Password)) TrySet(() => adv.ClearTextPassword = info.Password);
            TrySet(() => adv.SmartSizing = info.SmartSizing);
            TrySet(() => adv.RedirectDrives = info.RedirectDrives);
            TrySet(() => adv.RedirectClipboard = info.RedirectClipboard);
            TrySet(() => adv.EnableCredSspSupport = true);
            TrySet(() => adv.AuthenticationLevel = 0u);
            TrySet(() => adv.DisplayConnectionBar = false);
            // Windows キー組み合わせ(Alt+Tab, Win, Ctrl+Alt+End 等)を常にリモートへ送る。
            // KeyboardHookMode は SecuredSettings 側のプロパティ（既定は「コントロール全画面時のみ」）。
            TrySet(() => ocx.SecuredSettings.KeyboardHookMode = 1);
            TrySet(() => ocx.SecuredSettings3.KeyboardHookMode = 1);

            if (!string.IsNullOrWhiteSpace(info.Gateway))
            {
                dynamic ts = ocx.TransportSettings2;
                TrySet(() => ts.GatewayUsageMethod = 1u);
                TrySet(() => ts.GatewayProfileUsageMethod = 1u);
                TrySet(() => ts.GatewayHostname = info.Gateway!);
            }

            ocx.Connect();
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
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
            o.UpdateSessionDisplaySettings(w, h, w, h, 0u, 100u, 100u);
        }
        catch
        {
            // 動的解像度に非対応 → スマートサイジングで追従（拡縮表示）
            TrySet(() => _ocx!.AdvancedSettings9.SmartSizing = true);
        }
    }

    public void DisconnectSession()
    {
        try
        {
            var o = _ocx;
            if (o != null && (int)o.Connected != 0) o.Disconnect();
        }
        catch { /* ignore */ }
    }

    private static void TrySet(Action set)
    {
        try { set(); } catch { /* プロパティ未対応はスキップ */ }
    }
}
