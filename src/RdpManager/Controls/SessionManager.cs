using System.Windows;
using System.Windows.Shapes;
using RdpManager.Common;
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

/// <summary>タブ Tag に持たせる、セッション復元・後処理用の付随情報。</summary>
public sealed record SessionTag(string? NodeId, string? PostCommand, LaunchInfo? Info);

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

    /// <summary>タブの開閉でセッション数が変わったときに通知（ステータスバー表示用）。</summary>
    public event Action? SessionsChanged;

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
    }

    public TabControl DefaultPane => _left;

    /// <summary>左右両ペインの全セッションタブ。</summary>
    public IEnumerable<TabItem> AllTabs
        => _left.Items.OfType<TabItem>().Concat(_right.Items.OfType<TabItem>());

    public int SessionCount => _left.Items.Count + _right.Items.Count;

    /// <summary>ペイン選択時に呼ぶ（ホットキーの対象ペイン追跡）。</summary>
    public void OnPaneActivated(TabControl pane) => _activePane = pane;

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
            Tag = new SessionTag(nodeId, postCommand, info),
            ToolTip = HostAddress.FormatWithPort(info.Host, info.Port)
        };

        var close = new Button
        {
            Content = "✕", FontSize = 10, Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(8, 0, 0, 0), BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Cursor = Cursors.Hand
        };
        close.Click += (_, _) => CloseSession(tab, session);

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(dot);
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
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

        target.Items.Add(tab);
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
