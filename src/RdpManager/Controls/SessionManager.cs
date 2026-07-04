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
using GridSplitter = System.Windows.Controls.GridSplitter;
using DispatcherPriority = System.Windows.Threading.DispatcherPriority;

namespace RdpManager.Controls;

/// <summary>タブ Tag に持たせる、セッション復元・後処理用の付随情報。SessionKey はトースト活性化用の一意キー。</summary>
public sealed record SessionTag(string? NodeId, string? PostCommand, LaunchInfo? Info, string SessionKey);

/// <summary>
/// 左右2ペインの RDP セッションタブのライフサイクル（生成/クローズ/巡回/分割表示）を担う。
/// MVVM 非適用は意図的（埋め込み ActiveX をデータテンプレート化すると接続が切れるため）。
/// MainWindow から責務を分離するためのコードビハインド側ヘルパー。
/// </summary>
public sealed class SessionManager
{
    private readonly TabControl _left;
    private readonly TabControl _right;
    private readonly TextBlock _emptyHint;
    private readonly ColumnDefinition _rightCol;
    private readonly ColumnDefinition _rightSplitterCol;
    private readonly GridSplitter _rightSplitter;
    private TabControl _activePane;
    // Ctrl+Tab タブスイッチャー用の MRU（最近アクティブ化順）リスト。先頭が最新。
    private readonly List<TabItem> _mru = new();

    /// <summary>タブの開閉でセッション数が変わったときに通知（ステータスバー表示用）。</summary>
    public event Action? SessionsChanged;

    /// <summary>セッションからリモート通知を受信したとき（通知元タブ, タブタイトル, 通知内容）。</summary>
    public event Action<TabItem, string, Services.RemoteNotification>? SessionNotification;

    /// <summary>セッション内のキー操作（Ctrl+Alt+Break / カスタムキー）による全画面切替要求（true=全画面化）。</summary>
    public event Action<bool>? FullscreenChangeRequested;

    public SessionManager(TabControl left, TabControl right, TextBlock emptyHint,
        ColumnDefinition rightCol, ColumnDefinition rightSplitterCol, GridSplitter rightSplitter)
    {
        _left = left;
        _right = right;
        _emptyHint = emptyHint;
        _rightCol = rightCol;
        _rightSplitterCol = rightSplitterCol;
        _rightSplitter = rightSplitter;
        _activePane = left;

        // SelectionChanged はネストしたコントロールからバブリングすることもあるため、
        // ペイン自身が発火元のときだけ MRU を更新する
        _left.SelectionChanged += (_, e) => { if (e.Source == _left) TrackMruSelection(_left); };
        _right.SelectionChanged += (_, e) => { if (e.Source == _right) TrackMruSelection(_right); };
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
            if (tab.Content is RdpSessionControl s)
                s.SyncFullScreenState(fullscreen);
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
        if (tab.Content is RdpSessionControl s && s.VisualState == SessionVisualState.Disconnected)
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
            Content = session,
            Tag = new SessionTag(nodeId, postCommand, info, Guid.NewGuid().ToString("N")),
            ToolTip = HostAddress.FormatWithPort(info.Host, info.Port)
        };
        session.NotificationReceived += (_, n) => SessionNotification?.Invoke(tab, title, n);
        session.FullScreenRequested += on => FullscreenChangeRequested?.Invoke(on);
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

        target.Items.Add(tab);
        _mru.Insert(0, tab);
        target.SelectedItem = tab;
        UpdateEmptyHint();
        UpdateRightPane();
        SessionsChanged?.Invoke();

        session.Start(info);
    }

    private ContextMenu BuildTabMenu(TabItem tab, RdpSessionControl session)
    {
        var reconnect = new MenuItem { Header = "Reconnect" };
        reconnect.Click += (_, _) => { if (session.VisualState == SessionVisualState.Disconnected) session.Reconnect(); };
        var close = new MenuItem { Header = "Close", InputGestureText = "Ctrl+W" };
        close.Click += (_, _) => CloseSession(tab, session);
        var closeOthers = new MenuItem { Header = "Close Other Tabs" };
        closeOthers.Click += (_, _) => CloseOthers(tab);
        var closeAll = new MenuItem { Header = "Close All Tabs" };
        closeAll.Click += (_, _) => CloseAll();

        var menu = new ContextMenu();
        // 接続中の Reconnect は ActiveX が例外を投げるため、切断時のみ有効化
        menu.Opened += (_, _) => reconnect.IsEnabled = session.VisualState == SessionVisualState.Disconnected;
        menu.Items.Add(reconnect);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        menu.Items.Add(closeOthers);
        menu.Items.Add(closeAll);
        return menu;
    }

    public void CloseSession(TabItem tab, RdpSessionControl session)
    {
        session.Cleanup();
        if (tab.Tag is SessionTag { PostCommand: { Length: > 0 } cmd, Info: { } info })
            ExternalTools.Run(cmd, info);
        (tab.Parent as TabControl)?.Items.Remove(tab);
        _mru.Remove(tab);
        UpdateEmptyHint();
        UpdateRightPane();
        SessionsChanged?.Invoke();
    }

    /// <summary>アクティブペインの選択中タブを閉じる（Ctrl+W）。</summary>
    public void CloseActiveTab()
    {
        var pane = ResolveActivePane();
        if (pane.SelectedItem is TabItem tab && tab.Content is RdpSessionControl s)
            CloseSession(tab, s);
    }

    public void CloseOthers(TabItem keep)
    {
        foreach (var tab in AllTabs.Where(t => t != keep).ToList())
            if (tab.Content is RdpSessionControl s)
                CloseSession(tab, s);
    }

    public void CloseAll()
    {
        foreach (var tab in AllTabs.ToList())
            if (tab.Content is RdpSessionControl s)
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

    private TabControl ResolveActivePane()
        => _activePane is { Items.Count: > 0 } p ? p
            : (_left.Items.Count > 0 ? _left : _right);

    private static void FocusSelected(TabControl pane)
    {
        if (pane.SelectedItem is TabItem ti && ti.Content is RdpSessionControl s)
            pane.Dispatcher.BeginInvoke(new Action(s.FocusSession), DispatcherPriority.Input);
    }

    /// <summary>右ペインにセッションがあるときだけ右カラム/スプリッターを表示する。</summary>
    public void UpdateRightPane()
    {
        bool show = _right.Items.Count > 0;
        _rightCol.Width = show ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _rightSplitterCol.Width = show ? GridLength.Auto : new GridLength(0);
        _rightSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>両ペインともセッションが無いときだけ "no sessions" ヒントを表示する。</summary>
    public void UpdateEmptyHint()
        => _emptyHint.Visibility = _left.Items.Count == 0 && _right.Items.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
}
