using System.Runtime.InteropServices;
using System.Windows.Forms;
using RdpManager.Models;
using RdpManager.Services;

namespace RdpManager.Controls;

/// <summary>RDP ActiveX の接続状態（IDispatch の Connected プロパティ値に対応）。</summary>
public enum RdpConnectionState { Disconnected = 0, Connected = 1, Connecting = 2 }

/// <summary>手動クリップボード同期の方向。</summary>
public enum ClipboardSyncDirection { LocalToRemote, RemoteToLocal }

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

    // ── リモート通知用の静的仮想チャネル ──
    // リモート側スクリプトが WTSVirtualChannelWrite で書き込んだデータを OnChannelReceivedData で受ける。
    private const string NotifyChannelName = "CCNOTIF";
    // IMsTscAxEvents（イベント dispinterface）の DIID と OnChannelReceivedData の DISPID。
    // 型付きイベントインターフェイス（AxMSTSCLib）を生成しない方針のため、DISPID 直指定でシンクする。
    private static readonly Guid EventsInterfaceId = new("336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6");
    private const int DispidOnConnected = 2;
    private const int DispidOnDisconnected = 4;
    private const int DispidOnChannelReceivedData = 7;
    // コンテナ処理全画面（ContainerHandledFullScreen）の切替要求イベント
    private const int DispidOnRequestGoFullScreen = 8;
    private const int DispidOnRequestLeaveFullScreen = 9;
    private const int DispidOnRemoteDesktopSizeChange = 12;

    private delegate void ChannelReceivedDataHandler(string chanName, string data);
    private delegate void DisconnectedHandler(int discReason);
    private delegate void RemoteDesktopSizeChangeHandler(int width, int height);
    private ChannelReceivedDataHandler? _channelSink; // 接続ポイント登録済みデリゲートの GC 防止も兼ねる
    private Action? _goFullScreenSink;
    private Action? _leaveFullScreenSink;
    private Action? _connectedSink;
    private DisconnectedHandler? _disconnectedSink;
    private RemoteDesktopSizeChangeHandler? _sizeChangeSink;

    /// <summary>通知チャネル(CCNOTIF)のデータ受信（コントロールの UI スレッドで発火）。</summary>
    public event Action<string>? NotificationDataReceived;

    /// <summary>セッション内のキー操作（Ctrl+Alt+Break / HotKeyFullScreen）による全画面切替要求。true=全画面化。</summary>
    public event Action<bool>? FullScreenRequested;

    /// <summary>接続確立/切断の即時通知（OnConnected/OnDisconnected）。
    /// ポーリング間隔を待たずにオーバーレイと解像度を更新するためのヒント。</summary>
    public event Action? ConnectionStateChanged;

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

    /// <summary>直近の切断の拡張理由（ExtendedDisconnectReasonCode）。0 = 情報なし。
    /// OnDisconnected イベント時に取得し、Connect() でリセットする。</summary>
    public int LastExtendedDisconnectReason { get; private set; }

    /// <summary>直近の OnDisconnected の理由コード。トランスポート障害の判定に使用する。</summary>
    public int LastDisconnectReason { get; private set; }

    /// <summary>ハンドルを確実に生成して OCX(dynamic) を返す。</summary>
    private dynamic GetClient()
    {
        // CreateControl() は非表示中（Visibility.Hidden の背面タブ等）は何もしないため、
        // その場合は Handle 参照で強制的にハンドルを生成する
        if (!IsHandleCreated) CreateControl();
        if (!IsHandleCreated) _ = Handle;
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
            // 動的解像度が反映されるまでの中間フレームを引き伸ばし表示にするため常時有効。
            // 定常状態では解像度がビューと一致するため見た目の差はなく、
            // 動的解像度非対応サーバーでは従来から ResizeRemote のフォールバックで有効化される
            TrySet(() => adv.SmartSizing = true, "SmartSizing");
            TrySet(() => adv.RedirectDrives = info.RedirectDrives, "RedirectDrives");
            TrySet(() => adv.RedirectClipboard = info.RedirectClipboard, "RedirectClipboard");
            TrySet(() => adv.EnableCredSspSupport = true, "EnableCredSspSupport");
            // サーバー証明書の検証レベル（既定 2 = 不一致で接続不可）。
            TrySet(() => adv.AuthenticationLevel = (uint)info.AuthenticationLevel, "AuthenticationLevel");
            TrySet(() => adv.DisplayConnectionBar = false, "DisplayConnectionBar");
            // DisableConnectionBar / UseMultimon は IMsRdpClientNonScriptable5 のプロパティで
            // dispinterface に載っておらず dynamic では届かないため、RCW キャストで設定する。
            // 新しい mstscax の全画面接続バーは DisplayConnectionBar=false だけでは消えない
            if ((object)ocx is MSTSCLib.IMsRdpClientNonScriptable5 ns)
            {
                TrySet(() => ns.DisableConnectionBar = true, "DisableConnectionBar");
                if (info.UseMultimon) TrySet(() => ns.UseMultimon = true, "UseMultimon");
            }
            // Windows キー組み合わせ(Alt+Tab, Win 等)は純正 mstsc の既定と同じく「全画面時のみ」リモートへ送る (mode 2)。
            // 本アプリの全画面はアプリウィンドウ全画面のため、ContainerHandledFullScreen を有効にした上で
            // SetContainerFullScreen() により FullScreen プロパティをアプリの全画面トグルと同期させて成立させる。
            // KeyboardHookMode は SecuredSettings 側のプロパティで切断状態でのみ設定可。
            TrySet(() => adv.ContainerHandledFullScreen = 1, "ContainerHandledFullScreen");
            TrySet(() => ocx.SecuredSettings.KeyboardHookMode = 2, "SecuredSettings.KeyboardHookMode");
            TrySet(() => ocx.SecuredSettings3.KeyboardHookMode = 2, "SecuredSettings3.KeyboardHookMode");
            // 全画面中はコントロールの低レベルキーフックが RegisterHotKey より先に全キーを奪うため、
            // 解除はコントロール内蔵トグルキー Ctrl+Alt+<HotKeyFullScreen>（既定 Break）→ FullScreenRequested 経由でしか効かない。
            // カスタム全画面キーが Ctrl+Alt+<key> ならコントロールにも同じキーを教える
            if (App.Settings.FullscreenKey != 0 && App.Settings.FullscreenModifiers == 3)
                TrySet(() => adv.HotKeyFullScreen = (int)App.Settings.FullscreenKey, "HotKeyFullScreen");

            // リモート通知用の仮想チャネル登録（切断状態でのみ有効）と受信イベントのシンク
            TrySet(() => ocx.CreateVirtualChannels(NotifyChannelName), "CreateVirtualChannels");
            AttachChannelSink((object)ocx);
            AttachFullScreenSinks((object)ocx);
            AttachStateSinks((object)ocx);
            AttachSizeChangeSink((object)ocx);

            // ── 描画パフォーマンス最適化 ──
            // mstsc.exe と同等のハードウェア描画パイプライン（DX レンダリング + H.264 ハードウェアデコード）を
            // 有効化する。ActiveX 埋め込みの既定はソフトウェア GDI 描画で、リサイズ/タブ切替/セッション内描画の
            // 全てが遅くなる。IMsRdpExtendedSettings のプロパティのため RCW キャストで設定する
            if ((object)ocx is MSTSCLib.IMsRdpExtendedSettings ext)
            {
                object hw = true;
                TrySet(() => ext.set_Property("EnableHardwareMode", ref hw), "EnableHardwareMode");
                // 初期スケールを DPI に合わせて渡す。渡さないと 100% で接続 → 直後の ApplyResize で
                // スケール送信 → サーバー側の再レイアウトが一度必ず走ってしまう
                var (desktopScale, deviceScale) = GetScaleFactors();
                object ds = desktopScale, dv = deviceScale;
                TrySet(() => ext.set_Property("DesktopScaleFactor", ref ds), "DesktopScaleFactor");
                TrySet(() => ext.set_Property("DeviceScaleFactor", ref dv), "DeviceScaleFactor");
            }
            // 再描画削減（視覚変化なしの純粋な高速化）: 永続ビットマップキャッシュ
            TrySet(() => adv.BitmapPeristence = 1, "BitmapPeristence");  // ※API名のスペルは "Peristence"
            TrySet(() => adv.CachePersistenceActive = 1, "CachePersistenceActive");
            // 帯域変化の自動検出（mstsc の既定）。有効時はサーバー側がコーデック/フレームレートを回線に合わせる。
            // NetworkConnectionType は自動検出が使えないサーバー向けの初期ヒントとして残す（LAN=6）
            TrySet(() => adv.BandwidthDetection = true, "BandwidthDetection");
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

            _lastRemoteW = _lastRemoteH = 0; // 新しい接続の初期解像度は DesktopWidth/Height で決まるため再送抑止をリセット
            LastDisconnectReason = 0;
            LastExtendedDisconnectReason = 0; // 前回の切断理由を持ち越さない
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
    /// OnChannelReceivedData だけを ComEventsHelper で購読する（再接続時の二重登録は不可のためガード）。
    /// </summary>
    private void AttachChannelSink(object ocx)
    {
        if (_channelSink != null) return;
        try
        {
            var sink = new ChannelReceivedDataHandler((chan, data) =>
            {
                if (string.Equals(chan, NotifyChannelName, StringComparison.OrdinalIgnoreCase))
                    NotificationDataReceived?.Invoke(data);
            });
            ComEventsHelper.Combine(ocx, EventsInterfaceId, DispidOnChannelReceivedData, sink);
            _channelSink = sink;
        }
        catch (Exception ex)
        {
            // シンク不可でも接続自体は成立させる（通知機能だけ無効になる）
            Logger.Warn($"Notification channel sink could not be attached: {ex.Message}");
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
    private uint _lastRemoteW, _lastRemoteH;
    private long _resizeRequestedAt; // Stopwatch タイムスタンプ（要求→反映の所要時間計測用）

    public void ResizeRemote(int width, int height)
    {
        try
        {
            var o = _ocx;
            if (o is null || (int)o.Connected != 1) return;
            uint w = (uint)Math.Clamp(width & ~1, 200, 8192);   // 偶数・範囲内
            uint h = (uint)Math.Clamp(height & ~1, 200, 8192);
            // 同一サイズの再送はサーバー側の再レイアウトを無駄に起こすためスキップ
            // （ウィンドウ移動だけの WM_EXITSIZEMOVE や、即時適用後のデバウンス発火で通る）
            if (w == _lastRemoteW && h == _lastRemoteH) return;
            var (desktopScale, deviceScale) = GetScaleFactors();
            _resizeRequestedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            o.UpdateSessionDisplaySettings(w, h, w, h, 0u, desktopScale, deviceScale);
            _lastRemoteW = w; _lastRemoteH = h;
        }
        catch
        {
            // 動的解像度に非対応 → スマートサイジングで追従（拡縮表示）
            TrySet(() => { if (_ocx is { } o) o.AdvancedSettings9.SmartSizing = true; }, "SmartSizing(fallback)");
        }
    }

    /// <summary>
    /// 接続確立/切断イベント（DISPID 2/4）をシンクする。状態遷移をポーリング間隔（700ms）を
    /// 待たずに反映するためのもので、状態自体は従来どおり ConnectionState で読む。
    /// </summary>
    private void AttachStateSinks(object ocx)
    {
        if (_connectedSink != null) return;
        try
        {
            _connectedSink = () => ConnectionStateChanged?.Invoke();
            _disconnectedSink = discReason =>
            {
                // OnDisconnected の理由はネットワーク障害、ExtendedDisconnectReason は
                // ユーザー操作やサーバーポリシーの判別にそれぞれ使う。
                LastDisconnectReason = discReason;
                LastExtendedDisconnectReason = ReadExtendedDisconnectReason();
                ConnectionStateChanged?.Invoke();
            };
            ComEventsHelper.Combine(ocx, EventsInterfaceId, DispidOnConnected, _connectedSink);
            ComEventsHelper.Combine(ocx, EventsInterfaceId, DispidOnDisconnected, _disconnectedSink);
        }
        catch (Exception ex)
        {
            _connectedSink = null;
            _disconnectedSink = null;
            Logger.Warn($"Connection state sink could not be attached: {ex.Message}");
        }
    }

    /// <summary>OCX の ExtendedDisconnectReason を読む（切断直後のみ意味のある値が入る）。</summary>
    private int ReadExtendedDisconnectReason()
    {
        try
        {
            return _ocx is { } o ? (int)o.ExtendedDisconnectReason : 0;
        }
        catch { return 0; }
    }

    /// <summary>
    /// リモート解像度反映イベント（DISPID 12: OnRemoteDesktopSizeChange）をシンクし、
    /// リサイズ要求から反映までの所要時間をログへ出す（再描画性能の実測用）。
    /// </summary>
    private void AttachSizeChangeSink(object ocx)
    {
        if (_sizeChangeSink != null) return;
        try
        {
            _sizeChangeSink = (w, h) =>
            {
                var at = _resizeRequestedAt;
                if (at == 0) return;
                _resizeRequestedAt = 0;
                var ms = System.Diagnostics.Stopwatch.GetElapsedTime(at).TotalMilliseconds;
                Logger.Info($"Remote resolution applied: {w}x{h} in {ms:F0} ms");
            };
            ComEventsHelper.Combine(ocx, EventsInterfaceId, DispidOnRemoteDesktopSizeChange, _sizeChangeSink);
        }
        catch (Exception ex)
        {
            _sizeChangeSink = null;
            Logger.Warn($"Size change sink could not be attached: {ex.Message}");
        }
    }

    /// <summary>
    /// 全画面切替要求イベント（DISPID 8/9）をシンクする。ContainerHandledFullScreen 有効時、
    /// セッション内でトグルキーが押されるとコントロールは自前処理せずこのイベントで要求してくる。
    /// </summary>
    private void AttachFullScreenSinks(object ocx)
    {
        if (_goFullScreenSink != null) return;
        try
        {
            _goFullScreenSink = () => FullScreenRequested?.Invoke(true);
            _leaveFullScreenSink = () => FullScreenRequested?.Invoke(false);
            ComEventsHelper.Combine(ocx, EventsInterfaceId, DispidOnRequestGoFullScreen, _goFullScreenSink);
            ComEventsHelper.Combine(ocx, EventsInterfaceId, DispidOnRequestLeaveFullScreen, _leaveFullScreenSink);
        }
        catch (Exception ex)
        {
            _goFullScreenSink = null;
            _leaveFullScreenSink = null;
            Logger.Warn($"FullScreen request sink could not be attached: {ex.Message}");
        }
    }

    /// <summary>
    /// アプリウィンドウの全画面状態をコントロールへ通知する。
    /// ContainerHandledFullScreen 有効時は FullScreen を設定しても画面遷移はコンテナ（本アプリ）任せのまま、
    /// コントロール内部の全画面フラグだけが切り替わり、KeyboardHookMode=2 のキーフックが全画面時のみ有効になる。
    /// FullScreen は接続中のみ設定可のランタイムプロパティ。
    /// </summary>
    public void SetContainerFullScreen(bool fullscreen)
    {
        try
        {
            if (_ocx is { } o && (int)o.Connected == 1 && (bool)o.FullScreen != fullscreen)
                o.FullScreen = fullscreen;
        }
        catch (Exception ex)
        {
            Logger.Warn($"FullScreen state sync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 接続中セッションとローカルのクリップボードを一度だけ明示同期する。
    /// ManualClipboardSyncEnabled は接続後にも変更可能なため、同期中だけ有効にし、完了後は
    /// 自動同期へ戻す。これにより通常の RDP クリップボード共有の挙動を変えずに、複数セッション間で
    /// 確実にクリップボードを受け渡せる。
    /// </summary>
    public bool TrySyncClipboard(ClipboardSyncDirection direction, out string error)
    {
        error = "";
        if (_ocx is not { } ocx || ConnectionState != RdpConnectionState.Connected)
        {
            error = "The session is not connected.";
            return false;
        }

        if ((object)ocx is not MSTSCLib.IMsRdpExtendedSettings ext ||
            (object)ocx is not MSTSCLib.IMsRdpClientNonScriptable7 nonScriptable)
        {
            error = "Clipboard synchronization is not supported by this RDP client.";
            return false;
        }

        bool manualEnabled = false;
        try
        {
            object enabled = true;
            ext.set_Property("ManualClipboardSyncEnabled", ref enabled);
            manualEnabled = true;

            var clipboard = nonScriptable.Clipboard;
            if (clipboard is null)
                throw new InvalidOperationException("The RDP client did not provide a clipboard controller.");

            if (direction == ClipboardSyncDirection.LocalToRemote)
                clipboard.SyncLocalClipboardToRemoteSession();
            else
                clipboard.SyncRemoteClipboardToLocalSession();

        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            if (manualEnabled)
            {
                try
                {
                    object disabled = false;
                    ext.set_Property("ManualClipboardSyncEnabled", ref disabled);
                }
                catch (Exception ex)
                {
                    if (string.IsNullOrEmpty(error)) error = "Automatic clipboard sharing could not be restored.";
                    Logger.Warn($"Automatic clipboard synchronization could not be restored: {ex.Message}");
                }
            }
        }

        if (!string.IsNullOrEmpty(error))
        {
            Logger.Warn($"Clipboard synchronization failed ({direction}): {error}");
            return false;
        }

        Logger.Info($"Clipboard synchronized: {direction}");
        return true;
    }

    public void DisconnectSession()
    {
        try
        {
            if (_ocx is { } o && (int)o.Connected != 0) o.Disconnect();
        }
        catch { /* ignore */ }
    }

    /// <summary>
    /// ComEventsHelper のシンクは COM 接続ポイント側から強参照されるため、解除せずに
    /// タブを閉じるとコントロール一式（mstscax インスタンス・スレッド・HWND）が GC されず
    /// 蓄積する。破棄時にシンクを外して OCX 参照を手放す（OCX 自体の解放は base が行う）。
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ocx is not null)
        {
            object ocx = (object)_ocx;
            RemoveSink(ocx, DispidOnChannelReceivedData, _channelSink);
            RemoveSink(ocx, DispidOnRequestGoFullScreen, _goFullScreenSink);
            RemoveSink(ocx, DispidOnRequestLeaveFullScreen, _leaveFullScreenSink);
            RemoveSink(ocx, DispidOnConnected, _connectedSink);
            RemoveSink(ocx, DispidOnDisconnected, _disconnectedSink);
            RemoveSink(ocx, DispidOnRemoteDesktopSizeChange, _sizeChangeSink);
            _channelSink = null;
            _goFullScreenSink = null;
            _leaveFullScreenSink = null;
            _connectedSink = null;
            _disconnectedSink = null;
            _sizeChangeSink = null;
            _ocx = null;
        }
        base.Dispose(disposing);
    }

    private static void RemoveSink(object ocx, int dispid, Delegate? sink)
    {
        if (sink is null) return;
        try { ComEventsHelper.Remove(ocx, EventsInterfaceId, dispid, sink); }
        catch { /* 破棄経路のため失敗は無視 */ }
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
