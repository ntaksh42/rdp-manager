using System.Windows;
using System.Windows.Threading;
using RdpManager.Common;
using RdpManager.Models;
using RdpManager.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace RdpManager.Controls;

public enum SessionVisualState { Connecting, Connected, Disconnected, Reconnecting }

/// <summary>
/// 1 セッション分のタブ内容。WindowsFormsHost に RDP コントロールを載せ、
/// 接続状態の変化（OnConnected/OnDisconnected イベント＋安全網のポーリング）で
/// オーバーレイ表示を切り替える。
/// </summary>
public partial class RdpSessionControl : UserControl
{
    private readonly RdpClientHost _client = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _resizeThrottle = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _reconnect = new() { Interval = TimeSpan.FromMilliseconds(250) };
    // 接続がこの時間維持できたときだけ自動再接続の回数をリセットする
    // （「接続直後に切断される」相手への無限再接続ループを防ぐ）
    private static readonly TimeSpan StableConnectionTime = TimeSpan.FromSeconds(30);

    private LaunchInfo? _info;
    private RdpConnectionState? _prevState;
    private bool _wasConnected;       // 一度でも接続できたか（再接続判定用・リセットしない）
    private int _autoRetries;
    private bool _reconnectScheduled;
    private bool _autoReconnectStopped;
    private DateTimeOffset _reconnectAt;
    private string _lastDisconnectMessage = "The connection was lost.";
    private long _connectedAt;        // 直近の接続成立時刻（Stopwatch タイムスタンプ）
    private bool _closed;              // Cleanup 済みフラグ（破棄後に飛んでくる遅延ディスパッチを無視する）

    public event EventHandler? StateChanged;
    /// <summary>リモート側から仮想チャネル経由の通知を受信したとき。</summary>
    public event EventHandler<RemoteNotification>? NotificationReceived;
    /// <summary>このセッションがキーボードフォーカスを得たとき（アクティブペイン追跡用。SelectionChanged だけでは
    /// 選択中タブの再クリックや RDP 画面内クリックでのペイン移動を検知できないため補完する）。</summary>
    public event EventHandler? SessionFocused;
    /// <summary>セッション内のキー操作（Ctrl+Alt+Break 等）による全画面切替要求（true=全画面化）。</summary>
    public event Action<bool>? FullScreenRequested;
    /// <summary>切断オーバーレイの Close Tab からタブを閉じる要求。</summary>
    public event EventHandler? CloseRequested;
    public SessionVisualState VisualState { get; private set; } = SessionVisualState.Connecting;
    public bool ClipboardSharingEnabled => _info?.RedirectClipboard == true;

    public RdpSessionControl()
    {
        InitializeComponent();
        Host.Child = _client;
        _client.NotificationDataReceived += OnNotificationData;
        _client.FullScreenRequested += on => FullScreenRequested?.Invoke(on);
        _client.Enter += (_, _) => SessionFocused?.Invoke(this, EventArgs.Empty);
        _poll.Tick += OnPoll;
        // 接続確立/切断は COM イベントで即時反映する（ポーリングは検知漏れ時の安全網）。
        // イベント処理中の OCX への再入を避けるため BeginInvoke で一拍置く
        _client.ConnectionStateChanged += () => Dispatcher.BeginInvoke(new Action(() => OnPoll(null, EventArgs.Empty)));
        // ウィンドウ/タブのサイズ変更に追従。mstsc.exe と同様、ドラッグ中も一定間隔（400ms）で
        // リモート解像度を随時更新するスロットル方式（最終サイズは WM_EXITSIZEMOVE 等の即時適用が拾う）
        _resizeThrottle.Tick += (_, _) => { _resizeThrottle.Stop(); ApplyResize(); };
        SizeChanged += (_, _) => { if (!_resizeThrottle.IsEnabled) _resizeThrottle.Start(); };
        // 再接続待機中だけカウントダウンを更新する。通常時のバックグラウンド処理は増やさない。
        _reconnect.Tick += OnReconnectTick;
        // タブ切替で Unloaded しても切断しない（明示的に閉じた時のみ Cleanup）
    }

    /// <summary>現在の表示サイズにリモート解像度を合わせる。</summary>
    private void ApplyResize()
    {
        if (_client.ConnectionState == RdpConnectionState.Connected && _client.Width > 0 && _client.Height > 0)
            _client.ResizeRemote(_client.Width, _client.Height);
    }

    /// <summary>サイズが確定したタイミング（ドラッグ終了・全画面切替・スプリッター確定）で
    /// デバウンスを待たずにリモート解像度を即時反映する。</summary>
    public void ApplyResizeNow()
    {
        _resizeThrottle.Stop();
        ApplyResize();
    }

    /// <summary>
    /// 接続を開始する。実際に可視（visual tree にロード）された時点で接続することで、
    /// 非表示タブでの不安定なハンドル生成を避け、タブ切替でも接続を維持する。
    /// </summary>
    public void Start(LaunchInfo info)
    {
        _info = info;
        SetOverlay(SessionVisualState.Connecting, "Connecting…");
        if (IsLoaded)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(BeginConnect));
        else
        {
            Loaded -= OnLoadedConnect;
            Loaded += OnLoadedConnect;
        }
    }

    private void OnLoadedConnect(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedConnect;
        BeginConnect();
    }

    private void BeginConnect()
    {
        if (_closed || _info is null) return;
        _prevState = null;
        _client.Connect(_info);
        _poll.Start();
        // 接続直後の失敗（即時 LastError）を反映
        if (_client.LastError is not null)
            SetOverlay(SessionVisualState.Disconnected, "Could not start the connection", _client.LastError);
    }

    private void OnPoll(object? sender, EventArgs e)
    {
        if (_closed) return;
        var st = _client.ConnectionState;
        switch (st)
        {
            case RdpConnectionState.Connected:
                if (_prevState != RdpConnectionState.Connected)
                {
                    ApplyResize(); // 接続/再接続成立時にサイズ合わせ
                    _connectedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                }
                else if (_autoRetries > 0 &&
                         System.Diagnostics.Stopwatch.GetElapsedTime(_connectedAt) >= StableConnectionTime)
                {
                    _autoRetries = 0; // 安定して維持できた接続のみリトライ回数をリセット
                }
                _wasConnected = true;
                SetOverlay(SessionVisualState.Connected, "");
                break;
            case RdpConnectionState.Connecting:
                SetOverlay(SessionVisualState.Connecting, "Connecting…");
                break;
            default: // Disconnected
                if (_wasConnected)
                {
                    var deliberate = DeliberateDisconnectReason(_client.LastExtendedDisconnectReason);
                    if (deliberate is not null)
                    {
                        // 意図的な切断は自動再接続しない。特に「他端末からの接続による置き換え」(reason 5) で
                        // 再接続すると相手のセッションを奪い返してしまう。手動の Reconnect ボタンは残る
                        _reconnect.Stop();
                        _reconnectScheduled = false;
                        SetOverlay(SessionVisualState.Disconnected, "Disconnected", deliberate);
                    }
                    else if (RdpManager.App.Settings.AutoReconnect &&
                             !_autoReconnectStopped &&
                             ReconnectPolicy.IsTransientDisconnect(
                                 _client.LastDisconnectReason, _client.LastExtendedDisconnectReason) &&
                             !_reconnectScheduled && ReconnectPolicy.NextDelay(_autoRetries) is { } delay)
                    {
                        _lastDisconnectMessage = ReconnectPolicy.DescribeDisconnect(_client.LastDisconnectReason);
                        ScheduleReconnect(delay);
                    }
                    else if (!_reconnectScheduled)
                    {
                        _lastDisconnectMessage = ReconnectPolicy.DescribeDisconnect(_client.LastDisconnectReason);
                        SetOverlay(SessionVisualState.Disconnected, "Connection lost", _lastDisconnectMessage);
                    }
                }
                else if (VisualState != SessionVisualState.Disconnected)
                {
                    SetOverlay(SessionVisualState.Disconnected, "Could not connect",
                        _client.LastError ?? "The host is unreachable or authentication failed.");
                }
                break;
        }
        _prevState = st;
    }

    /// <summary>
    /// ExtendedDisconnectReasonCode のうち「サーバー/ユーザーの意図による切断」なら説明文を返す
    /// （ネットワーク断と違い自動再接続すべきでないもの）。それ以外は null。
    /// </summary>
    private static string? DeliberateDisconnectReason(int reason) => reason switch
    {
        1 or 2 => "The session was disconnected by the server.",             // APIInitiatedDisconnect/Logoff
        3 => "The session was disconnected due to server idle timeout.",     // ServerIdleTimeout
        4 => "The session was disconnected due to server logon timeout.",    // ServerLogonTimeout
        5 => "Another device connected to this session.",                    // ReplacedByOtherConnection
        11 or 12 => "The session was disconnected or signed out remotely.",  // RpcInitiatedDisconnectByUser/LogoffByUser
        _ => null,
    };

    private void ScheduleReconnect(TimeSpan delay)
    {
        _reconnectScheduled = true;
        _reconnectAt = DateTimeOffset.UtcNow + delay;
        UpdateReconnectCountdown();
        _reconnect.Start();
    }

    private void OnReconnectTick(object? sender, EventArgs e)
    {
        if (!_reconnectScheduled) { _reconnect.Stop(); return; }
        if (!RdpManager.App.Settings.AutoReconnect)
        {
            _reconnect.Stop();
            _reconnectScheduled = false;
            SetOverlay(SessionVisualState.Disconnected, "Connection lost", _lastDisconnectMessage);
            return;
        }
        if (DateTimeOffset.UtcNow < _reconnectAt) { UpdateReconnectCountdown(); return; }

        _reconnect.Stop();
        _reconnectScheduled = false;
        _autoRetries++;
        BeginConnect();
    }

    private void UpdateReconnectCountdown()
    {
        int seconds = Math.Max(1, (int)Math.Ceiling((_reconnectAt - DateTimeOffset.UtcNow).TotalSeconds));
        SetOverlay(SessionVisualState.Reconnecting,
            $"Connection lost — retrying in {seconds}s ({_autoRetries + 1}/{ReconnectPolicy.MaxRetries})",
            _lastDisconnectMessage);
    }

    private void SetOverlay(SessionVisualState state, string status, string? error = null)
    {
        bool changed = state != VisualState;
        VisualState = state;
        StatusText.Text = status;
        Overlay.Visibility = state == SessionVisualState.Connected ? Visibility.Collapsed : Visibility.Visible;
        bool recoverable = state is SessionVisualState.Disconnected or SessionVisualState.Reconnecting;
        RecoveryButtons.Visibility = recoverable ? Visibility.Visible : Visibility.Collapsed;
        ReconnectButton.Visibility = recoverable ? Visibility.Visible : Visibility.Collapsed;
        StopRetryingButton.Visibility = state == SessionVisualState.Reconnecting ? Visibility.Visible : Visibility.Collapsed;
        CloseButton.Visibility = recoverable ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
        ErrorText.Text = error ?? "";
        if (changed) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>チャネル生データを検証し、正しい通知だけをイベントに変換する。</summary>
    private void OnNotificationData(string payload)
    {
        if (RemoteNotification.TryParse(payload, out var n))
            NotificationReceived?.Invoke(this, n);
    }

    private void OnReconnect(object sender, RoutedEventArgs e) => Reconnect();

    private void OnStopRetrying(object sender, RoutedEventArgs e)
    {
        _reconnect.Stop();
        _reconnectScheduled = false;
        _autoReconnectStopped = true;
        SetOverlay(SessionVisualState.Disconnected, "Connection lost", _lastDisconnectMessage);
    }

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>手動再接続。切断状態からリトライ回数をリセットして接続し直す。</summary>
    public void Reconnect()
    {
        if (_info is null) return;
        _reconnect.Stop();
        _reconnectScheduled = false;
        _autoReconnectStopped = false;
        _autoRetries = 0;
        Start(_info);
    }

    /// <summary>アプリの全画面状態をコントロールへ反映する（全画面時のみ Win キー組み合わせをリモートへ送るため）。</summary>
    public void SyncFullScreenState(bool fullscreen) => _client.SetContainerFullScreen(fullscreen);

    /// <summary>この接続で共有が許可されていれば、ローカルとリモートのクリップボードを明示同期する。</summary>
    public bool TrySyncClipboard(ClipboardSyncDirection direction, out string error)
    {
        if (!ClipboardSharingEnabled)
        {
            error = "Clipboard sharing is disabled for this connection.";
            return false;
        }
        return _client.TrySyncClipboard(direction, out error);
    }

    /// <summary>埋め込み RDP コントロールへキーボードフォーカスを移す。</summary>
    public void FocusSession()
    {
        try { _client.Focus(); } catch { /* 未生成などは無視 */ }
    }

    public void Cleanup()
    {
        _closed = true;
        _reconnect.Stop();
        _reconnectScheduled = false;
        _poll.Stop();
        _resizeThrottle.Stop();
        _client.DisconnectSession();
        // Disconnect だけでは OCX が解放されず、タブを閉じるたびに mstscax インスタンスが
        // 蓄積して不安定化する。AxHost.Dispose の OLE 正規手順（deactivate → Close）で解放する
        Host.Child = null;
        _client.Dispose();
        Host.Dispose();
    }
}
