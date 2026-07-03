using System.Windows;
using System.Windows.Threading;
using RdpManager.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace RdpManager.Controls;

public enum SessionVisualState { Connecting, Connected, Disconnected, Reconnecting }

/// <summary>
/// 1 セッション分のタブ内容。WindowsFormsHost に RDP コントロールを載せ、
/// 接続状態をポーリングしてオーバーレイ表示を切り替える。
/// </summary>
public partial class RdpSessionControl : UserControl
{
    private const int MaxAutoRetries = 5;

    private readonly RdpClientHost _client = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _resizeDebounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _reconnect = new() { Interval = TimeSpan.FromSeconds(5) };
    private LaunchInfo? _info;
    private RdpConnectionState? _prevState;
    private bool _wasConnected;       // 一度でも接続できたか（再接続判定用・リセットしない）
    private int _autoRetries;
    private bool _reconnectScheduled;

    public event EventHandler? StateChanged;
    /// <summary>リモート側から仮想チャネル経由の通知を受信したとき。</summary>
    public event EventHandler<RemoteNotification>? NotificationReceived;
    public SessionVisualState VisualState { get; private set; } = SessionVisualState.Connecting;

    public RdpSessionControl()
    {
        InitializeComponent();
        Host.Child = _client;
        _client.NotificationDataReceived += OnNotificationData;
        _poll.Tick += OnPoll;
        // ウィンドウ/タブのサイズ変更に追従（連続変更はデバウンス）
        _resizeDebounce.Tick += (_, _) => { _resizeDebounce.Stop(); ApplyResize(); };
        SizeChanged += (_, _) => { _resizeDebounce.Stop(); _resizeDebounce.Start(); };
        // 自動再接続（一度きりの遅延実行）
        _reconnect.Tick += (_, _) => { _reconnect.Stop(); _reconnectScheduled = false; _autoRetries++; BeginConnect(); };
        // タブ切替で Unloaded しても切断しない（明示的に閉じた時のみ Cleanup）
    }

    /// <summary>現在の表示サイズにリモート解像度を合わせる。</summary>
    private void ApplyResize()
    {
        if (_client.ConnectionState == RdpConnectionState.Connected && _client.Width > 0 && _client.Height > 0)
            _client.ResizeRemote(_client.Width, _client.Height);
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
        if (_info is null) return;
        _prevState = null;
        _client.Connect(_info);
        _poll.Start();
        // 接続直後の失敗（即時 LastError）を反映
        if (_client.LastError is not null)
            SetOverlay(SessionVisualState.Disconnected, "Could not start the connection", _client.LastError);
    }

    private void OnPoll(object? sender, EventArgs e)
    {
        var st = _client.ConnectionState;
        switch (st)
        {
            case RdpConnectionState.Connected:
                if (_prevState != RdpConnectionState.Connected) { ApplyResize(); _autoRetries = 0; } // 接続/再接続成立時にサイズ合わせ
                _wasConnected = true;
                SetOverlay(SessionVisualState.Connected, "");
                break;
            case RdpConnectionState.Connecting:
                SetOverlay(SessionVisualState.Connecting, "Connecting…");
                break;
            default: // Disconnected
                if (_wasConnected)
                {
                    if (RdpManager.App.Settings.AutoReconnect && _autoRetries < MaxAutoRetries && !_reconnectScheduled)
                    {
                        _reconnectScheduled = true;
                        SetOverlay(SessionVisualState.Reconnecting,
                            $"Disconnected — reconnecting ({_autoRetries + 1}/{MaxAutoRetries})…");
                        _reconnect.Start();
                    }
                    else if (!_reconnectScheduled)
                    {
                        SetOverlay(SessionVisualState.Disconnected, "Disconnected");
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

    private void SetOverlay(SessionVisualState state, string status, string? error = null)
    {
        bool changed = state != VisualState;
        VisualState = state;
        StatusText.Text = status;
        Overlay.Visibility = state == SessionVisualState.Connected ? Visibility.Collapsed : Visibility.Visible;
        ReconnectButton.Visibility = state == SessionVisualState.Disconnected ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>手動再接続。切断状態からリトライ回数をリセットして接続し直す。</summary>
    public void Reconnect()
    {
        if (_info is null) return;
        _reconnect.Stop();
        _reconnectScheduled = false;
        _autoRetries = 0;
        Start(_info);
    }

    /// <summary>埋め込み RDP コントロールへキーボードフォーカスを移す。</summary>
    public void FocusSession()
    {
        try { _client.Focus(); } catch { /* 未生成などは無視 */ }
    }

    public void Cleanup()
    {
        _reconnect.Stop();
        _reconnectScheduled = false;
        _poll.Stop();
        _client.DisconnectSession();
    }
}
