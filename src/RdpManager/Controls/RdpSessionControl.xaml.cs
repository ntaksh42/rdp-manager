using System.Windows;
using System.Windows.Threading;
using RdpManager.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace RdpManager.Controls;

public enum SessionVisualState { Connecting, Connected, Disconnected }

/// <summary>
/// 1 セッション分のタブ内容。WindowsFormsHost に RDP コントロールを載せ、
/// 接続状態をポーリングしてオーバーレイ表示を切り替える。
/// </summary>
public partial class RdpSessionControl : UserControl
{
    private readonly RdpClientHost _client = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _resizeDebounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private LaunchInfo? _info;
    private bool _everConnected;

    public event EventHandler? StateChanged;
    public SessionVisualState VisualState { get; private set; } = SessionVisualState.Connecting;

    public RdpSessionControl()
    {
        InitializeComponent();
        Host.Child = _client;
        _poll.Tick += OnPoll;
        // ウィンドウ/タブのサイズ変更に追従（連続変更はデバウンス）
        _resizeDebounce.Tick += (_, _) => { _resizeDebounce.Stop(); ApplyResize(); };
        SizeChanged += (_, _) => { _resizeDebounce.Stop(); _resizeDebounce.Start(); };
        // タブ切替で Unloaded しても切断しない（明示的に閉じた時のみ Cleanup）
    }

    /// <summary>現在の表示サイズにリモート解像度を合わせる。</summary>
    private void ApplyResize()
    {
        if (_client.ConnectionState == 1 && _client.Width > 0 && _client.Height > 0)
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
        _everConnected = false;
        _client.Connect(_info);
        _poll.Start();
        // 接続直後の失敗（即時 LastError）を反映
        if (_client.LastError is not null)
            SetOverlay(SessionVisualState.Disconnected, "Could not start the connection", _client.LastError);
    }

    private void OnPoll(object? sender, EventArgs e)
    {
        switch (_client.ConnectionState)
        {
            case 1: // connected
                if (!_everConnected) { _everConnected = true; ApplyResize(); } // 接続直後に現在サイズへ合わせる
                SetOverlay(SessionVisualState.Connected, "");
                break;
            case 2: // connecting
                SetOverlay(SessionVisualState.Connecting, "Connecting…");
                break;
            default: // 0 disconnected
                if (_everConnected)
                    SetOverlay(SessionVisualState.Disconnected, "Disconnected");
                else if (VisualState != SessionVisualState.Disconnected)
                    SetOverlay(SessionVisualState.Disconnected, "Could not connect",
                        _client.LastError ?? "The host is unreachable or authentication failed.");
                break;
        }
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

    private void OnReconnect(object sender, RoutedEventArgs e) => Start(_info!);

    public void Cleanup()
    {
        _poll.Stop();
        _client.DisconnectSession();
    }
}
