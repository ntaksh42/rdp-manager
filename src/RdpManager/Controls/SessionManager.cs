using System.Windows;
using System.Windows.Shapes;
using RdpManager.Common;
using RdpManager.Models;
using RdpManager.Services;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using MouseButton = System.Windows.Input.MouseButton;
using Orientation = System.Windows.Controls.Orientation;
using Brushes = System.Windows.Media.Brushes;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using TabControl = System.Windows.Controls.TabControl;
using TabItem = System.Windows.Controls.TabItem;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
using Grid = System.Windows.Controls.Grid;
using GridSplitter = System.Windows.Controls.GridSplitter;
using DispatcherPriority = System.Windows.Threading.DispatcherPriority;

namespace RdpManager.Controls;

/// <summary>タブ Tag に持たせる、セッション復元・後処理用の付随情報。SessionKey はトースト活性化用の一意キー。
/// Session は常駐ホスト方式のため TabItem.Content ではなくここで持つ。</summary>
public sealed record SessionTag(string? NodeId, string? PostCommand, LaunchInfo? Info, string SessionKey, RdpSessionControl Session);

/// <summary>
/// 左右2ペインの RDP セッションタブのライフサイクル（生成/クローズ/巡回/分割表示）を担う。
/// MVVM 非適用は意図的（埋め込み ActiveX をデータテンプレート化すると接続が切れるため）。
/// セッション本体は TabItem.Content ではなく各ペインのホスト Grid に常駐させ、タブ切替は
/// Visibility の切替だけで行う（Content 方式は切替ごとに Unloaded/Loaded でホスト HWND の
/// 破棄・再作成と全面再描画が走り遅いため）。
/// MainWindow から責務を分離するためのコードビハインド側ヘルパー。
/// </summary>
public sealed class SessionManager
{
    private readonly TabControl _left;
    private readonly TabControl _right;
    private readonly Grid _leftHost;
    private readonly Grid _rightHost;
    private readonly TextBlock _emptyHint;
    private readonly ColumnDefinition _leftCol;
    private readonly ColumnDefinition _rightCol;
    private readonly ColumnDefinition _rightSplitterCol;
    private readonly GridSplitter _rightSplitter;
    private TabControl _activePane;
    // Ctrl+Tab タブスイッチャー用の MRU（最近アクティブ化順）リスト。先頭が最新。
    private readonly List<TabItem> _mru = new();
    // UpdateRightPane で直近に適用したペインの表示状態。null は未適用（初回は必ず反映）。
    // 表示/非表示が変わらない限り Width を書き換えず、GridSplitter で調整した比率を保つ。
    private bool? _leftVisible;
    private bool? _rightVisible;

    /// <summary>タブの開閉でセッション数が変わったときに通知（ステータスバー表示用）。</summary>
    public event Action? SessionsChanged;

    /// <summary>セッションからリモート通知を受信したとき（通知元タブ, タブタイトル, 通知内容）。</summary>
    public event Action<TabItem, string, Services.RemoteNotification>? SessionNotification;

    /// <summary>セッション内のキー操作（Ctrl+Alt+Break / カスタムキー）による全画面切替要求（true=全画面化）。</summary>
    public event Action<bool>? FullscreenChangeRequested;

    /// <summary>クリップボード同期の結果（成功可否, ユーザー向けメッセージ）。</summary>
    public event Action<bool, string>? ClipboardSyncCompleted;

    public SessionManager(TabControl left, TabControl right, Grid leftHost, Grid rightHost, TextBlock emptyHint,
        ColumnDefinition leftCol, ColumnDefinition rightCol, ColumnDefinition rightSplitterCol, GridSplitter rightSplitter)
    {
        _left = left;
        _right = right;
        _leftHost = leftHost;
        _rightHost = rightHost;
        _emptyHint = emptyHint;
        _leftCol = leftCol;
        _rightCol = rightCol;
        _rightSplitterCol = rightSplitterCol;
        _rightSplitter = rightSplitter;
        _activePane = left;

        // SelectionChanged はネストしたコントロールからバブリングすることもあるため、
        // ペイン自身が発火元のときだけ MRU・表示セッションを更新する
        _left.SelectionChanged += (_, e) => { if (e.Source == _left) { TrackMruSelection(_left); SyncSessionVisibility(_left); } };
        _right.SelectionChanged += (_, e) => { if (e.Source == _right) { TrackMruSelection(_right); SyncSessionVisibility(_right); } };
    }

    /// <summary>タブに対応するセッション本体（常駐ホスト方式のため Content ではなく Tag から引く）。</summary>
    public static RdpSessionControl? SessionOf(TabItem tab) => (tab.Tag as SessionTag)?.Session;

    private Grid HostOf(TabControl pane) => pane == _right ? _rightHost : _leftHost;

    /// <summary>選択中タブのセッションだけを表示する。Hidden（Collapsed でなく）はレイアウトサイズを
    /// 保つため、背面のセッションもウィンドウサイズに追従し続け、タブ切替時のリサイズが発生しない。</summary>
    private void SyncSessionVisibility(TabControl pane)
    {
        var selected = pane.SelectedItem is TabItem tab ? SessionOf(tab) : null;
        foreach (UIElement child in HostOf(pane).Children)
            child.Visibility = child == selected ? Visibility.Visible : Visibility.Hidden;
    }

    private void TrackMruSelection(TabControl pane)
    {
        if (pane.SelectedItem is TabItem tab)
        {
            _mru.Remove(tab);
            _mru.Insert(0, tab);
        }
    }

    public TabControl DefaultPane => _left;

    /// <summary>左右両ペインの全セッションタブ。</summary>
    public IEnumerable<TabItem> AllTabs
        => _left.Items.OfType<TabItem>().Concat(_right.Items.OfType<TabItem>());

    public int SessionCount => _left.Items.Count + _right.Items.Count;

    /// <summary>ペイン選択時に呼ぶ（ホットキーの対象ペイン追跡）。</summary>
    public void OnPaneActivated(TabControl pane) => _activePane = pane;

    // アプリウィンドウの全画面状態。全セッションの KeyboardHookMode=2（全画面時のみ Win キーをリモートへ）と連動させる
    private bool _appFullscreen;

    /// <summary>アプリの全画面トグル時に呼び、全セッションへ状態を反映する。</summary>
    public void SetAppFullscreen(bool fullscreen)
    {
        _appFullscreen = fullscreen;
        foreach (var tab in AllTabs)
            SessionOf(tab)?.SyncFullScreenState(fullscreen);
    }

    /// <summary>
    /// 同じノードのタブが既に開いていれば、それを前面に出して true を返す。
    /// 切断状態なら再接続も開始する（同一接続の二重タブを作らない）。
    /// </summary>
    public bool TryActivateExisting(string nodeId)
    {
        var tab = AllTabs.FirstOrDefault(t => (t.Tag as SessionTag)?.NodeId == nodeId);
        if (tab is null) return false;
        if (tab.Parent is TabControl tc)
        {
            _activePane = tc;
            tc.SelectedItem = tab;
            FocusSelected(tc);
        }
        if (SessionOf(tab) is { VisualState: SessionVisualState.Disconnected } s)
            s.Reconnect();
        return true;
    }

    /// <summary>セッションキー（トースト活性化用）でタブを特定し、前面に出す。</summary>
    public bool ActivateBySessionKey(string key)
    {
        var tab = AllTabs.FirstOrDefault(t => (t.Tag as SessionTag)?.SessionKey == key);
        if (tab?.Parent is not TabControl tc) return false;
        _activePane = tc;
        tc.SelectedItem = tab;
        FocusSelected(tc);
        return true;
    }

    /// <summary>指定タブを前面に出す（Ctrl+Tab タブスイッチャーからの確定用）。</summary>
    public void ActivateTab(TabItem tab)
    {
        if (tab.Parent is not TabControl tc) return;
        _activePane = tc;
        tc.SelectedItem = tab;
        FocusSelected(tc);
    }

    /// <summary>開いている全タブを MRU（最近アクティブ化）順で返す。MRU に無いタブは末尾に補完する。</summary>
    public IReadOnlyList<TabItem> GetMruTabs()
        => _mru.Concat(AllTabs.Where(t => !_mru.Contains(t))).ToList();

    public void OpenSession(LaunchInfo info, string title, string? nodeId = null,
                            string? postCommand = null, TabControl? target = null)
    {
        target ??= _left;
        _activePane = target;
        var session = new RdpSessionControl();
        var dot = new Ellipse
        {
            Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center, Fill = Brushes.Orange
        };

        var tab = new TabItem
        {
            Tag = new SessionTag(nodeId, postCommand, info, Guid.NewGuid().ToString("N"), session),
            ToolTip = HostAddress.FormatWithPort(info.Host, info.Port)
        };
        session.NotificationReceived += (_, n) => SessionNotification?.Invoke(tab, title, n);
        session.FullScreenRequested += on => FullscreenChangeRequested?.Invoke(on);
        session.CloseRequested += (_, _) => CloseSession(tab, session);
        // SelectionChanged は選択が「変化」した時しか発火しないため、選択中タブの再クリックや
        // RDP 画面内クリックでのペイン移動はこちらで補足する
        session.SessionFocused += (_, _) => { if (tab.Parent is TabControl tc) OnPaneActivated(tc); };

        var close = new Button
        {
            Content = "✕", FontSize = 10, Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(8, 0, 0, 0), BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Cursor = Cursors.Hand,
            ToolTip = "Close tab"
        };
        close.Click += (_, _) => CloseSession(tab, session);

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(dot);
        header.Children.Add(new TextBlock
        {
            Text = title, VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = title
        });
        header.Children.Add(close);
        tab.Header = header;

        // 中クリックで閉じる（一般的なタブ UI の慣習に合わせる）
        tab.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle) { CloseSession(tab, session); e.Handled = true; }
        };
        tab.ContextMenu = BuildTabMenu(tab, session);

        session.StateChanged += (_, _) => dot.Fill = session.VisualState switch
        {
            SessionVisualState.Connected => Brushes.LimeGreen,
            SessionVisualState.Disconnected => Brushes.Gray,
            SessionVisualState.Reconnecting => Brushes.Gold,
            _ => Brushes.Orange
        };
        // 全画面中に接続が成立した（開いた/再接続した）セッションにも全画面状態を反映する
        session.StateChanged += (_, _) =>
        {
            if (_appFullscreen && session.VisualState == SessionVisualState.Connected)
                session.SyncFullScreenState(true);
        };

        // セッション本体はホスト Grid に常駐させる（SelectedItem 設定 → SyncSessionVisibility で表示される）
        session.Visibility = Visibility.Hidden;
        HostOf(target).Children.Add(session);
        target.Items.Add(tab);
        _mru.Insert(0, tab);
        target.SelectedItem = tab;
        SyncSessionVisibility(target); // 最初のタブは Items.Add 時点で自動選択され SelectionChanged が来ないため明示同期
        UpdateEmptyHint();
        UpdateRightPane();
        SessionsChanged?.Invoke();

        session.Start(info);
    }

    private ContextMenu BuildTabMenu(TabItem tab, RdpSessionControl session)
    {
        var reconnect = new MenuItem { Header = "Reconnect" };
        reconnect.Click += (_, _) => { if (session.VisualState == SessionVisualState.Disconnected) session.Reconnect(); };
        var clipboardToRemote = new MenuItem
        {
            Header = "Send Local Clipboard to Session",
            InputGestureText = "Ctrl+Alt+Shift+V"
        };
        clipboardToRemote.Click += (_, _) => SyncClipboard(session, ClipboardSyncDirection.LocalToRemote);
        var clipboardFromRemote = new MenuItem
        {
            Header = "Get Clipboard from Session",
            InputGestureText = "Ctrl+Alt+Shift+C"
        };
        clipboardFromRemote.Click += (_, _) => SyncClipboard(session, ClipboardSyncDirection.RemoteToLocal);
        var move = new MenuItem { Header = "Move to Other Pane (Split)", InputGestureText = "Ctrl+Shift+M" };
        move.Click += (_, _) => MoveToOtherPane(tab);
        var moveLeft = new MenuItem { Header = "Move Tab Left", InputGestureText = "Ctrl+Alt+Shift+PageUp" };
        moveLeft.Click += (_, _) => MoveTab(tab, -1);
        var moveRight = new MenuItem { Header = "Move Tab Right", InputGestureText = "Ctrl+Alt+Shift+PageDown" };
        moveRight.Click += (_, _) => MoveTab(tab, 1);
        var close = new MenuItem { Header = "Close", InputGestureText = "Ctrl+W" };
        close.Click += (_, _) => CloseSession(tab, session);
        var closeOthers = new MenuItem { Header = "Close Other Tabs" };
        closeOthers.Click += (_, _) => CloseOthers(tab);
        var closeAll = new MenuItem { Header = "Close All Tabs" };
        closeAll.Click += (_, _) => CloseAll();

        var menu = new ContextMenu();
        // 接続中の Reconnect は ActiveX が例外を投げるため、切断時のみ有効化
        menu.Opened += (_, _) =>
        {
            reconnect.IsEnabled = session.VisualState == SessionVisualState.Disconnected;
            clipboardToRemote.IsEnabled = clipboardFromRemote.IsEnabled =
                session.VisualState == SessionVisualState.Connected && session.ClipboardSharingEnabled;
        };
        menu.Items.Add(reconnect);
        menu.Items.Add(new Separator());
        menu.Items.Add(clipboardToRemote);
        menu.Items.Add(clipboardFromRemote);
        menu.Items.Add(move);
        menu.Items.Add(moveLeft);
        menu.Items.Add(moveRight);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        menu.Items.Add(closeOthers);
        menu.Items.Add(closeAll);
        return menu;
    }

    private void SyncClipboard(RdpSessionControl session, ClipboardSyncDirection direction)
    {
        bool success = session.TrySyncClipboard(direction, out var error);
        string message = success
            ? direction == ClipboardSyncDirection.LocalToRemote
                ? "Local clipboard sent to the active session."
                : "Clipboard received from the active session."
            : error;
        ClipboardSyncCompleted?.Invoke(success, message);
    }

    /// <summary>アクティブペインの接続中セッションとクリップボードを明示同期する。</summary>
    public void SyncActiveClipboard(ClipboardSyncDirection direction)
    {
        var pane = ResolveActivePane();
        if (pane.SelectedItem is TabItem tab && SessionOf(tab) is { } session)
            SyncClipboard(session, direction);
        else
            ClipboardSyncCompleted?.Invoke(false, "There is no active session.");
    }

    public void CloseSession(TabItem tab, RdpSessionControl session)
    {
        session.Cleanup();
        if (tab.Tag is SessionTag { PostCommand: { Length: > 0 } cmd, Info: { } info })
            ExternalTools.Run(cmd, info);
        if (tab.Parent is TabControl pane)
        {
            pane.Items.Remove(tab);
            HostOf(pane).Children.Remove(session);
        }
        _mru.Remove(tab);
        UpdateEmptyHint();
        UpdateRightPane();
        SessionsChanged?.Invoke();
    }

    /// <summary>アクティブペインの選択中タブを閉じる（Ctrl+W）。</summary>
    public void CloseActiveTab()
    {
        var pane = ResolveActivePane();
        if (pane.SelectedItem is TabItem tab && SessionOf(tab) is { } s)
            CloseSession(tab, s);
    }

    public void CloseOthers(TabItem keep)
    {
        foreach (var tab in AllTabs.Where(t => t != keep).ToList())
            if (SessionOf(tab) is { } s)
                CloseSession(tab, s);
    }

    public void CloseAll()
    {
        foreach (var tab in AllTabs.ToList())
            if (SessionOf(tab) is { } s)
                CloseSession(tab, s);
    }

    /// <summary>アクティブなペイン内でタブを巡回し、選択したセッションへフォーカスを移す。</summary>
    public void CycleTab(int delta)
    {
        var pane = ResolveActivePane();
        int n = pane.Items.Count;
        if (n == 0) return;
        int idx = pane.SelectedIndex < 0 ? 0 : pane.SelectedIndex;
        idx = (idx + delta + n) % n;
        pane.SelectedIndex = idx;
        FocusSelected(pane);
    }

    /// <summary>アクティブペインの index 番目（0始まり）のタブへ移動。</summary>
    public void JumpToTab(int index)
    {
        var pane = ResolveActivePane();
        if (index < 0 || index >= pane.Items.Count) return;
        pane.SelectedIndex = index;
        FocusSelected(pane);
    }

    /// <summary>タブをもう一方のペインへ移動する（分割表示の開始/解除）。セッション本体も
    /// 移動先ペインのホスト Grid へ付け替える。一度だけ WindowsFormsHost の HWND 再作成が
    /// 走るが接続は維持される（タブ切替の常時コストとは異なり明示操作の一回きりのため許容）。</summary>
    public void MoveToOtherPane(TabItem tab)
    {
        if (tab.Parent is not TabControl source || SessionOf(tab) is not { } session) return;
        var target = source == _left ? _right : _left;
        source.Items.Remove(tab);
        HostOf(source).Children.Remove(session);
        HostOf(target).Children.Add(session);
        target.Items.Add(tab);
        _activePane = target;
        target.SelectedItem = tab;
        SyncSessionVisibility(source);
        SyncSessionVisibility(target);
        UpdateEmptyHint();
        UpdateRightPane();
        FocusSelected(target);
        // ペイン構成の変化で両ペインの幅が変わるため、レイアウト確定後に全セッションの解像度を即時反映
        _left.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyResizeToAll));
    }

    /// <summary>アクティブペインの選択中タブをもう一方のペインへ移動する（Ctrl+Shift+M）。</summary>
    public void MoveActiveTabToOtherPane()
    {
        if (ResolveActivePane().SelectedItem is TabItem tab) MoveToOtherPane(tab);
    }

    /// <summary>同じペイン内でタブを左右へ移動する。</summary>
    private void MoveTab(TabItem tab, int delta)
    {
        if (tab.Parent is not TabControl pane) return;
        int oldIndex = pane.Items.IndexOf(tab);
        int newIndex = Math.Clamp(oldIndex + delta, 0, pane.Items.Count - 1);
        if (oldIndex == newIndex) return;
        pane.Items.RemoveAt(oldIndex);
        pane.Items.Insert(newIndex, tab);
        pane.SelectedItem = tab;
        _activePane = pane;
        FocusSelected(pane);
    }

    /// <summary>アクティブタブを同じペイン内で左右へ移動する。</summary>
    public void MoveActiveTab(int delta)
    {
        if (ResolveActivePane().SelectedItem is TabItem tab) MoveTab(tab, delta);
    }

    /// <summary>もう一方のペインへフォーカスを移す（F6 / Ctrl+Alt+F6）。相手ペインが空なら何もしない。</summary>
    public void FocusOtherPane()
    {
        var other = ResolveActivePane() == _left ? _right : _left;
        if (other.Items.Count == 0) return;
        _activePane = other;
        if (other.SelectedItem is null) other.SelectedIndex = 0;
        FocusSelected(other);
    }

    private TabControl ResolveActivePane()
        => _activePane is { Items.Count: > 0 } p ? p
            : (_left.Items.Count > 0 ? _left : _right);

    private static void FocusSelected(TabControl pane)
    {
        if (pane.SelectedItem is TabItem ti && SessionOf(ti) is { } s)
            pane.Dispatcher.BeginInvoke(new Action(s.FocusSession), DispatcherPriority.Input);
    }

    /// <summary>全セッションのリモート解像度を現在サイズへ即時反映する
    /// （ウィンドウドラッグ終了・全画面切替・スプリッター確定などサイズ確定時用）。</summary>
    public void ApplyResizeToAll()
    {
        foreach (var tab in AllTabs)
            SessionOf(tab)?.ApplyResizeNow();
    }

    /// <summary>セッションがあるペインのカラムだけを表示する（両方空のときは左＝ヒント表示側を残す）。
    /// スプリッターは両ペイン表示時のみ。</summary>
    public void UpdateRightPane()
    {
        bool right = _right.Items.Count > 0;
        bool left = _left.Items.Count > 0 || !right;
        // 表示/非表示が切り替わったときだけ Width を書き換える。無条件に上書きすると
        // GridSplitter でユーザーが調整した Star 比率がタブ開閉のたびに 1:1 へ戻ってしまう。
        if (_leftVisible != left)
        {
            _leftCol.Width = left ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _leftVisible = left;
        }
        if (_rightVisible != right)
        {
            _rightCol.Width = right ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _rightVisible = right;
        }
        bool both = left && right;
        _rightSplitterCol.Width = both ? GridLength.Auto : new GridLength(0);
        _rightSplitter.Visibility = both ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>両ペインともセッションが無いときだけ "no sessions" ヒントを表示する。</summary>
    public void UpdateEmptyHint()
        => _emptyHint.Visibility = _left.Items.Count == 0 && _right.Items.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
}
