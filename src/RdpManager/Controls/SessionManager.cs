using System.Windows;
using System.Windows.Shapes;
using RdpManager.Common;
using RdpManager.Models;
using RdpManager.Services;
using RdpManager.Views;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using MouseButton = System.Windows.Input.MouseButton;
using Orientation = System.Windows.Controls.Orientation;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using TabControl = System.Windows.Controls.TabControl;
using TabItem = System.Windows.Controls.TabItem;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using Grid = System.Windows.Controls.Grid;
using RowDefinition = System.Windows.Controls.RowDefinition;
using Border = System.Windows.Controls.Border;
using Canvas = System.Windows.Controls.Canvas;
using Point = System.Windows.Point;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DispatcherPriority = System.Windows.Threading.DispatcherPriority;

namespace RdpManager.Controls;

/// <summary>タブ Tag に持たせる、セッション復元・後処理用の付随情報。SessionKey はトースト活性化用の一意キー。
/// Session は常駐ホスト方式のため TabItem.Content ではなくここで持つ。Title はユーザーが Rename で
/// 変更できるよう可変（初期値はツリーノード名）。</summary>
public sealed record SessionTag(string? NodeId, string? PostCommand, LaunchInfo? Info, string SessionKey, RdpSessionControl Session)
{
    public string Title { get; set; } = "";
    /// <summary>手動再接続時に最新の接続設定で置き換わるため可変。</summary>
    public LaunchInfo? Info { get; set; } = Info;
}

/// <summary>
/// RDP セッションタブのライフサイクル（生成/クローズ/巡回）と自由分割ペインの配置を担う。
/// MVVM 非適用は意図的（埋め込み ActiveX をデータテンプレート化すると接続が切れるため）。
/// セッション本体は TabItem.Content ではなく各ペインのホスト Grid に常駐させ、タブ切替は
/// Visibility の切替だけで行う（Content 方式は切替ごとに Unloaded/Loaded でホスト HWND の
/// 破棄・再作成と全面再描画が走り遅いため）。
/// ペインは1枚の Canvas に平置きし、入れ子の分割構成（<see cref="PaneLayout"/>）から計算した矩形で
/// 配置する。分割・結合でペインの親要素が変わらないため、HWND の再作成はタブのペイン間移動時だけで済む。
/// MainWindow から責務を分離するためのコードビハインド側ヘルパー。
/// </summary>
public sealed class SessionManager
{
    /// <summary>ペイン境界（スプリッター）の太さ。</summary>
    private const double SplitterThickness = 4;

    /// <summary>1つのペイン（タブ列 + セッションのホスト）。Frame が Canvas 上に配置される要素。</summary>
    private sealed class Pane
    {
        public required int Id { get; init; }
        public required Border Frame { get; init; }
        public required TabControl Tabs { get; init; }
        public required Grid Host { get; init; }
        public required TextBlock Hint { get; init; }
    }

    private readonly Canvas _canvas;
    private PaneLayout _layout = new();
    private readonly Dictionary<int, Pane> _panes = new();
    // スプリッターは分割構成が変わるたびに作り直さず使い回す（Compute の順序はツリーが同じなら安定）
    private readonly List<Border> _splitters = new();
    private List<PaneLayout.SplitterRect> _splitterRects = new();
    private Pane _activePane;
    // Ctrl+Tab タブスイッチャー用の MRU（最近アクティブ化順）リスト。先頭が最新。
    private readonly List<TabItem> _mru = new();
    // 並べ替え・ペイン間移動中（タブを外すと一時的に選択が隣のタブへ移る）は MRU・表示の追従を止める。
    // 止めないと隣のタブが MRU の2番手に割り込み、Ctrl+Tab の戻り先が変わる上、セッションが一瞬隠れて再表示される
    private bool _reordering;
    // スプリッタードラッグ中の状態（開始位置と開始時の配置）
    private Border? _dragging;
    private Point _dragStart;
    private PaneLayout.SplitterRect _dragRect;

    /// <summary>タブの開閉でセッション数が変わったときに通知（ステータスバー表示用）。</summary>
    public event Action? SessionsChanged;

    /// <summary>セッションからリモート通知を受信したとき（通知元タブ, タブタイトル, 通知内容）。</summary>
    public event Action<TabItem, string, Services.RemoteNotification>? SessionNotification;

    /// <summary>セッション内のキー操作（Ctrl+Alt+Break / カスタムキー）による全画面切替要求（true=全画面化）。</summary>
    public event Action<bool>? FullscreenChangeRequested;

    /// <summary>クリップボード同期の結果（成功可否, ユーザー向けメッセージ）。</summary>
    public event Action<bool, string>? ClipboardSyncCompleted;

    /// <summary>ノード ID から最新の接続情報を解決する（手動再接続で編集後の設定を使うため）。解決できなければ null。</summary>
    public Func<string, LaunchInfo?>? InfoResolver { get; set; }

    public SessionManager(Canvas canvas)
    {
        _canvas = canvas;
        _activePane = CreatePane(_layout.PaneIds[0]);
        _canvas.SizeChanged += (_, _) => Relayout();
        Relayout();
    }

    /// <summary>タブに対応するセッション本体（常駐ホスト方式のため Content ではなく Tag から引く）。</summary>
    public static RdpSessionControl? SessionOf(TabItem tab) => (tab.Tag as SessionTag)?.Session;

    private static Pane? PaneOf(TabItem tab) => (tab.Parent as TabControl)?.Tag as Pane;

    private IEnumerable<Pane> PanesInOrder => _layout.PaneIds.Select(id => _panes[id]);

    private Pane CreatePane(int id)
    {
        var tabs = new TabControl();
        var host = new Grid();
        var hint = new TextBlock
        {
            Text = "Double-click a connection in the tree to open it here.\n" +
                   "(Ctrl+Alt+F7 / Ctrl+Alt+F8 splits this pane right / down)",
            FontSize = 14, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SubtleFg");
        // TabControl はタブストリップ専用（Content は使わない）。セッション本体は下のホスト Grid に常駐させる
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(host, 1);
        Grid.SetRow(hint, 1);
        grid.Children.Add(tabs);
        grid.Children.Add(host);
        grid.Children.Add(hint);
        var frame = new Border { Child = grid, BorderBrush = Brushes.Transparent };
        var pane = new Pane { Id = id, Frame = frame, Tabs = tabs, Host = host, Hint = hint };
        tabs.Tag = pane;

        // SelectionChanged はネストしたコントロールからバブリングすることもあるため、
        // ペイン自身が発火元のときだけ MRU・表示セッションを更新する
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != tabs || _reordering) return;
            ActivatePane(pane);
            TrackMruSelection(pane);
            SyncSessionVisibility(pane);
        };
        // 空ペインのクリックやタブストリップのクリックでも、そのペインを操作対象にする
        frame.PreviewMouseDown += (_, _) => ActivatePane(pane);

        _panes[id] = pane;
        _canvas.Children.Add(frame);
        UpdatePaneChrome(pane);
        return pane;
    }

    private void RemovePane(Pane pane)
    {
        if (_layout.PaneCount < 2) return;
        // 操作対象だったペインを消すときは、隣（ペイン順で直前）のペインを操作対象にする
        var fallback = _panes[NeighborPaneId(pane.Id)];
        _layout.Remove(pane.Id);
        _panes.Remove(pane.Id);
        _canvas.Children.Remove(pane.Frame);
        if (_activePane == pane) ActivatePane(fallback);
    }

    /// <summary>操作対象ペインを切り替え、枠のハイライトを更新する。</summary>
    private void ActivatePane(Pane pane)
    {
        if (_activePane == pane) return;
        var previous = _activePane;
        _activePane = pane;
        UpdatePaneChrome(previous);
        UpdatePaneChrome(pane);
    }

    /// <summary>ペインの空表示ヒント・タブストリップ・アクティブ枠を現在の状態に合わせる。</summary>
    private void UpdatePaneChrome(Pane pane)
    {
        bool empty = pane.Tabs.Items.Count == 0;
        pane.Hint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        pane.Tabs.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        // 分割中だけ枠を出し、操作対象（ツリーから開く先・ホットキーの対象）のペインを強調する
        bool split = _layout.PaneCount > 1;
        pane.Frame.BorderThickness = new Thickness(split ? 1 : 0);
        if (split && pane == _activePane) pane.Frame.SetResourceReference(Border.BorderBrushProperty, "AccentBg");
        else pane.Frame.BorderBrush = Brushes.Transparent;
    }

    /// <summary>レイアウトツリーから各ペインとスプリッターの位置を計算して Canvas 上に配置する。</summary>
    private void Relayout()
    {
        var bounds = new Rect(0, 0, Math.Max(0, _canvas.ActualWidth), Math.Max(0, _canvas.ActualHeight));
        var (rects, splitters) = _layout.Compute(bounds, SplitterThickness);
        foreach (var (id, r) in rects)
        {
            var frame = _panes[id].Frame;
            Canvas.SetLeft(frame, r.X);
            Canvas.SetTop(frame, r.Y);
            frame.Width = r.Width;
            frame.Height = r.Height;
        }

        while (_splitters.Count < splitters.Count) _splitters.Add(CreateSplitter());
        for (int i = 0; i < _splitters.Count; i++)
        {
            var bar = _splitters[i];
            if (i >= splitters.Count) { bar.Visibility = Visibility.Collapsed; continue; }
            var r = splitters[i].Rect;
            Canvas.SetLeft(bar, r.X);
            Canvas.SetTop(bar, r.Y);
            bar.Width = r.Width;
            bar.Height = r.Height;
            bar.Cursor = splitters[i].Split.Direction == SplitDirection.Horizontal ? Cursors.SizeWE : Cursors.SizeNS;
            bar.Visibility = Visibility.Visible;
        }
        _splitterRects = splitters;
    }

    private Border CreateSplitter()
    {
        var bar = new Border();
        bar.SetResourceReference(Border.BackgroundProperty, "SplitterBg");
        bar.MouseLeftButtonDown += OnSplitterDown;
        bar.MouseMove += OnSplitterMove;
        bar.MouseLeftButtonUp += OnSplitterUp;
        bar.LostMouseCapture += (_, _) => EndSplitterDrag();
        _canvas.Children.Add(bar);
        return bar;
    }

    private void OnSplitterDown(object sender, MouseButtonEventArgs e)
    {
        var bar = (Border)sender;
        int idx = _splitters.IndexOf(bar);
        if (idx < 0 || idx >= _splitterRects.Count) return;
        _dragging = bar;
        _dragStart = e.GetPosition(_canvas);
        _dragRect = _splitterRects[idx];
        bar.CaptureMouse();
        e.Handled = true;
    }

    private void OnSplitterMove(object sender, MouseEventArgs e)
    {
        if (_dragging != sender) return;
        var pos = e.GetPosition(_canvas);
        double delta = _dragRect.Split.Direction == SplitDirection.Horizontal ? pos.X - _dragStart.X : pos.Y - _dragStart.Y;
        PaneLayout.SetRatio(_dragRect.Split, PaneLayout.RatioAfterDrag(_dragRect, delta, SplitterThickness));
        Relayout();
    }

    private void OnSplitterUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border bar || _dragging != bar) return;
        bar.ReleaseMouseCapture(); // LostMouseCapture → EndSplitterDrag
        e.Handled = true;
    }

    private void EndSplitterDrag()
    {
        if (_dragging is null) return;
        _dragging = null;
        // ドラッグ確定時はリサイズデバウンス(400ms)を待たずにリモート解像度を即時反映する
        ApplyResizeToAll();
    }

    /// <summary>分割構成が変わった後に呼ぶ。配置・枠を更新し、レイアウト確定後に全セッションの解像度を即時反映する。</summary>
    private void OnLayoutChanged()
    {
        Relayout();
        foreach (var pane in _panes.Values) UpdatePaneChrome(pane);
        _canvas.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyResizeToAll));
    }

    /// <summary>選択中タブのセッションだけを表示する。Hidden（Collapsed でなく）はレイアウトサイズを
    /// 保つため、背面のセッションもウィンドウサイズに追従し続け、タブ切替時のリサイズが発生しない。</summary>
    private static void SyncSessionVisibility(Pane pane)
    {
        var selected = pane.Tabs.SelectedItem is TabItem tab ? SessionOf(tab) : null;
        foreach (UIElement child in pane.Host.Children)
        {
            bool show = child == selected;
            // 背面に回るセッションは隠す直前の画面をセッション一覧用に保存する
            // （非表示中の RDP コントロールは描画内容を取得できないことがあるため）
            if (!show && child.Visibility == Visibility.Visible && child is RdpSessionControl leaving)
                leaving.TryUpdateSnapshot(allowScreenCopy: false, deferConversion: true);
            child.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        }
    }

    private void TrackMruSelection(Pane pane)
    {
        if (pane.Tabs.SelectedItem is TabItem tab) TouchMru(tab);
    }

    /// <summary>タブを MRU の先頭へ移す。選択が変わらないペイン間のフォーカス移動（F6・画面クリック）でも呼ぶ。</summary>
    private void TouchMru(TabItem tab)
    {
        _mru.Remove(tab);
        _mru.Insert(0, tab);
    }

    /// <summary>セッション状態の表示色（タブのドット・セッション一覧で共通）。</summary>
    public static Brush StateBrush(SessionVisualState state) => state switch
    {
        SessionVisualState.Connected => Brushes.LimeGreen,
        SessionVisualState.Disconnected => Brushes.Gray,
        SessionVisualState.Reconnecting => Brushes.Gold,
        _ => Brushes.Orange
    };

    /// <summary>
    /// セッション一覧の画面取得用に、指定セッションだけをそのペインで表示する（同じペインの他は Hidden）。
    /// 非表示の子ウィンドウは PrintWindow で取得できず、重なった別セッションの画面が返るため、
    /// 一覧ウィンドウがメインウィンドウを覆っている間だけ一時的に表示して撮る。閉じたら RestoreVisibility で戻す。
    /// </summary>
    public void ShowForCapture(RdpSessionControl session)
    {
        if (session.Parent is not Grid host) return;
        foreach (UIElement child in host.Children)
            child.Visibility = child == session ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>ShowForCapture で変えた表示を、各ペインの選択中タブだけが見える通常状態に戻す。</summary>
    public void RestoreVisibility()
    {
        foreach (var pane in _panes.Values) SyncSessionVisibility(pane);
    }

    /// <summary>分割中ならタブのあるペインの表示名（"Pane 2" など、セッション一覧用）。分割していなければ null。</summary>
    public string? PaneLabelOf(TabItem tab)
    {
        if (_layout.PaneCount < 2 || PaneOf(tab) is not { } pane) return null;
        return $"Pane {_layout.PaneIds.ToList().IndexOf(pane.Id) + 1}";
    }

    /// <summary>全ペインの全セッションタブ（ペイン順）。</summary>
    public IEnumerable<TabItem> AllTabs => PanesInOrder.SelectMany(p => p.Tabs.Items.OfType<TabItem>()).ToList();

    public int SessionCount => _panes.Values.Sum(p => p.Tabs.Items.Count);

    // アプリウィンドウの全画面状態。全セッションの KeyboardHookMode=2（全画面時のみ Win キーをリモートへ）と連動させる
    private bool _appFullscreen;

    /// <summary>全画面中に接続が成立したセッションへ全画面状態を反映した後に発生する（コントロールが新しいキーフックを入れるタイミング）。</summary>
    public event Action? FullscreenStateSynced;

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
        if (PaneOf(tab) is { } pane)
        {
            ActivatePane(pane);
            pane.Tabs.SelectedItem = tab;
            FocusSelected(pane);
        }
        if (SessionOf(tab) is { VisualState: SessionVisualState.Disconnected } s)
            s.Reconnect();
        return true;
    }

    /// <summary>セッションキー（トースト活性化用）でタブを特定し、前面に出す。</summary>
    public bool ActivateBySessionKey(string key)
    {
        var tab = AllTabs.FirstOrDefault(t => (t.Tag as SessionTag)?.SessionKey == key);
        if (tab is null || PaneOf(tab) is not { } pane) return false;
        ActivatePane(pane);
        pane.Tabs.SelectedItem = tab;
        FocusSelected(pane);
        return true;
    }

    /// <summary>指定タブを前面に出す（Ctrl+Tab タブスイッチャーからの確定用）。</summary>
    public void ActivateTab(TabItem tab)
    {
        if (PaneOf(tab) is not { } pane) return;
        ActivatePane(pane);
        pane.Tabs.SelectedItem = tab;
        TouchMru(tab); // 既に選択中のタブ（他ペインから戻る場合）は SelectionChanged が来ない
        FocusSelected(pane);
    }

    /// <summary>アクティブペインの選択中タブ（セッションが無ければ null）。</summary>
    public TabItem? ActiveTab => ResolveActivePane().Tabs.SelectedItem as TabItem;

    /// <summary>開いている全タブを MRU（最近アクティブ化）順で返す。MRU に無いタブは末尾に補完する。</summary>
    public IReadOnlyList<TabItem> GetMruTabs()
        => _mru.Concat(AllTabs.Where(t => !_mru.Contains(t))).ToList();

    /// <summary>セッションを開く。toSide なら隣のペイン（無ければアクティブペインを右に分割した新ペイン）に、
    /// paneIndex 指定時はそのペイン（復元用。ペイン順の位置）に、それ以外はアクティブペインに開く。</summary>
    public void OpenSession(LaunchInfo info, string title, string? nodeId = null,
                            string? postCommand = null, bool toSide = false, int? paneIndex = null)
    {
        Pane target;
        if (paneIndex is int pi && pi >= 0 && pi < _layout.PaneCount)
            target = _panes[_layout.PaneIds[pi]];
        else if (toSide)
            target = _layout.PaneCount > 1
                ? _panes[_layout.NextPane(ResolveActivePane().Id, 1)]
                : SplitPane(ResolveActivePane(), SplitDirection.Horizontal, moveTab: null);
        else
            target = ResolveActivePane();
        ActivatePane(target);

        var session = new RdpSessionControl();
        var dot = new Ellipse
        {
            Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center, Fill = Brushes.Orange
        };

        var tag = new SessionTag(nodeId, postCommand, info, Guid.NewGuid().ToString("N"), session) { Title = title };
        var tab = new TabItem
        {
            Tag = tag,
            ToolTip = HostAddress.FormatWithPort(info.Host, info.Port)
        };
        // 開いた後に接続が編集されていても、手動再接続では最新のホスト・資格情報を使う
        if (nodeId != null)
            session.RefreshInfo = () =>
            {
                if (InfoResolver?.Invoke(nodeId) is not { } latest) return null;
                tag.Info = latest;
                tab.ToolTip = HostAddress.FormatWithPort(latest.Host, latest.Port);
                return latest;
            };
        session.NotificationReceived += (_, n) => SessionNotification?.Invoke(tab, tag.Title, n);
        session.FullScreenRequested += on => FullscreenChangeRequested?.Invoke(on);
        session.CloseRequested += (_, _) => CloseSession(tab, session);
        // SelectionChanged は選択が「変化」した時しか発火しないため、選択中タブの再クリックや
        // RDP 画面内クリックでのペイン移動はこちらで補足する
        // 選択は変わらないため SelectionChanged では MRU が更新されない。ここで先頭へ移さないと、
        // ペイン間を移った直後の Ctrl+Tab が現在のタブを選んでしまう
        session.SessionFocused += (_, _) =>
        {
            if (PaneOf(tab) is not { } pane) return;
            ActivatePane(pane);
            TouchMru(tab);
        };

        var close = new Button
        {
            Content = "✕", FontSize = 10, Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(8, 0, 0, 0), BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Cursor = Cursors.Hand,
            ToolTip = "Close tab"
        };
        close.Click += (_, _) => CloseSession(tab, session);

        var titleText = new TextBlock
        {
            Text = title, VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = title
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(dot);
        header.Children.Add(titleText);
        header.Children.Add(close);
        tab.Header = header;

        // 中クリックで閉じる（一般的なタブ UI の慣習に合わせる）
        tab.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle) { CloseSession(tab, session); e.Handled = true; }
        };
        tab.ContextMenu = BuildTabMenu(tab, session, titleText);

        session.StateChanged += (_, _) => dot.Fill = StateBrush(session.VisualState);
        // 全画面中に接続が成立した（開いた/再接続した）セッションにも全画面状態を反映する
        session.StateChanged += (_, _) =>
        {
            if (_appFullscreen && session.VisualState == SessionVisualState.Connected)
            {
                session.SyncFullScreenState(true);
                FullscreenStateSynced?.Invoke();
            }
        };

        // セッション本体はホスト Grid に常駐させる（SelectedItem 設定 → SyncSessionVisibility で表示される）
        session.Visibility = Visibility.Hidden;
        target.Host.Children.Add(session);
        target.Tabs.Items.Add(tab);
        _mru.Insert(0, tab);
        target.Tabs.SelectedItem = tab;
        SyncSessionVisibility(target); // 最初のタブは Items.Add 時点で自動選択され SelectionChanged が来ないため明示同期
        UpdatePaneChrome(target);
        SessionsChanged?.Invoke();

        session.Start(info);
    }

    private ContextMenu BuildTabMenu(TabItem tab, RdpSessionControl session, TextBlock titleText)
    {
        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) => RenameTab(tab, titleText);
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
        var move = new MenuItem { Header = "Move to Next Pane", InputGestureText = "Ctrl+Shift+M" };
        move.Click += (_, _) => MoveToNextPane(tab);
        var splitRight = new MenuItem { Header = "Move to New Pane on Right" };
        splitRight.Click += (_, _) => MoveToNewPane(tab, SplitDirection.Horizontal);
        var splitDown = new MenuItem { Header = "Move to New Pane Below" };
        splitDown.Click += (_, _) => MoveToNewPane(tab, SplitDirection.Vertical);
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
            // ペインに1タブしか無いと、新ペインへ移すと元ペインが消えて配置が変わらないため無効
            splitRight.IsEnabled = splitDown.IsEnabled = tab.Parent is TabControl { Items.Count: > 1 };
            move.IsEnabled = _layout.PaneCount > 1 || splitRight.IsEnabled;
        };
        menu.Items.Add(rename);
        menu.Items.Add(reconnect);
        menu.Items.Add(new Separator());
        menu.Items.Add(clipboardToRemote);
        menu.Items.Add(clipboardFromRemote);
        menu.Items.Add(new Separator());
        menu.Items.Add(move);
        menu.Items.Add(splitRight);
        menu.Items.Add(splitDown);
        menu.Items.Add(moveLeft);
        menu.Items.Add(moveRight);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        menu.Items.Add(closeOthers);
        menu.Items.Add(closeAll);
        return menu;
    }

    /// <summary>タブの表示名をユーザー入力でリネームする（右クリックメニュー「Rename…」から）。</summary>
    private void RenameTab(TabItem tab, TextBlock titleText)
    {
        if (tab.Tag is not SessionTag tag) return;
        var owner = Window.GetWindow(tab);
        var dlg = new RenameSessionDialog(tag.Title) { Owner = owner };
        if (dlg.ShowDialog() != true) return;
        tag.Title = dlg.NewName;
        titleText.Text = dlg.NewName;
        titleText.ToolTip = dlg.NewName;
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
        if (ResolveActivePane().Tabs.SelectedItem is TabItem tab && SessionOf(tab) is { } session)
            SyncClipboard(session, direction);
        else
            ClipboardSyncCompleted?.Invoke(false, "There is no active session.");
    }

    public void CloseSession(TabItem tab, RdpSessionControl session)
    {
        session.Cleanup();
        if (tab.Tag is SessionTag { PostCommand: { Length: > 0 } cmd, Info: { } info })
            ExternalTools.Run(cmd, info);
        if (PaneOf(tab) is { } pane)
        {
            bool wasActive = pane == _activePane && pane.Tabs.SelectedItem == tab;
            pane.Tabs.Items.Remove(tab);
            pane.Host.Children.Remove(session);
            // 最後のタブが閉じたペインは消し、隣接ペインがその場所を引き継ぐ（最後の1ペインは残す）
            if (pane.Tabs.Items.Count == 0 && _layout.PaneCount > 1)
            {
                RemovePane(pane);
                OnLayoutChanged();
            }
            else UpdatePaneChrome(pane);
            // 閉じたセッションがフォーカスを持っていたため、次に選ばれたセッションへ移す。移さないとフォーカスが
            // WPF 側に残り、全画面中にリモート向けに押した Esc で全画面が解除される
            if (wasActive)
            {
                var next = ResolveActivePane();
                if (next.Tabs.SelectedItem is TabItem t && next != pane) TouchMru(t);
                FocusSelected(next);
            }
        }
        _mru.Remove(tab);
        SessionsChanged?.Invoke();
    }

    /// <summary>アクティブペインの選択中タブを閉じる（Ctrl+W）。</summary>
    public void CloseActiveTab()
    {
        if (ResolveActivePane().Tabs.SelectedItem is TabItem tab && SessionOf(tab) is { } s)
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
        int n = pane.Tabs.Items.Count;
        if (n == 0) return;
        int idx = pane.Tabs.SelectedIndex < 0 ? 0 : pane.Tabs.SelectedIndex;
        idx = (idx + delta + n) % n;
        pane.Tabs.SelectedIndex = idx;
        FocusSelected(pane);
    }

    /// <summary>アクティブペインの index 番目（0始まり）のタブへ移動。</summary>
    public void JumpToTab(int index)
    {
        var pane = ResolveActivePane();
        if (index < 0 || index >= pane.Tabs.Items.Count) return;
        pane.Tabs.SelectedIndex = index;
        FocusSelected(pane);
    }

    // ── ペインの分割・結合・移動 ──

    /// <summary>ペインを direction 方向に分割して後ろ側（右/下）に新ペインを作り、操作対象にする。
    /// moveTab を指定するとそのタブを新ペインへ移す（null なら新ペインは空で、ツリーから開く先になる）。</summary>
    private Pane SplitPane(Pane pane, SplitDirection direction, TabItem? moveTab)
    {
        var created = CreatePane(_layout.Split(pane.Id, direction));
        if (moveTab != null) MoveTabToPane(moveTab, created);
        ActivatePane(created);
        OnLayoutChanged();
        return created;
    }

    /// <summary>アクティブペインを分割する（Ctrl+Alt+F7 = 右 / Ctrl+Alt+F8 = 下）。
    /// ペインに2タブ以上あれば選択中タブを新ペインへ移し、1タブ以下なら空のペインを作る。</summary>
    public void SplitActivePane(SplitDirection direction)
    {
        var pane = ResolveActivePane();
        var move = pane.Tabs.Items.Count > 1 ? pane.Tabs.SelectedItem as TabItem : null;
        SplitPane(pane, direction, move);
        if (move is null) RestoreFocusToWindow();
        else FocusSelected(_activePane);
    }

    /// <summary>タブをそのペインの右/下に作った新ペインへ移す（タブの右クリックメニュー）。</summary>
    public void MoveToNewPane(TabItem tab, SplitDirection direction)
    {
        if (PaneOf(tab) is not { } pane || pane.Tabs.Items.Count < 2) return;
        SplitPane(pane, direction, tab);
    }

    /// <summary>アクティブペインを閉じ、そのタブを隣のペインへまとめる（Ctrl+Alt+Shift+F7）。
    /// 分割していなければ何もしない。</summary>
    public void CloseActivePane()
    {
        var pane = ResolveActivePane();
        if (_layout.PaneCount < 2) return;
        var target = _panes[NeighborPaneId(pane.Id)];
        foreach (var tab in pane.Tabs.Items.OfType<TabItem>().ToList())
            MoveTabToPane(tab, target);
        RemovePane(pane);
        ActivatePane(target);
        OnLayoutChanged();
        FocusSelected(target);
    }

    private int NeighborPaneId(int paneId)
    {
        var ids = _layout.PaneIds.ToList();
        int idx = ids.IndexOf(paneId);
        return idx > 0 ? ids[idx - 1] : ids[1];
    }

    /// <summary>タブを次のペインへ移動する（Ctrl+Shift+M / Ctrl+Alt+Shift+F6）。分割していなければ
    /// 右に分割して移す（ペインに1タブしか無ければ何もしない）。移動元が空になればそのペインは閉じる。</summary>
    public void MoveToNextPane(TabItem tab)
    {
        if (PaneOf(tab) is not { } source) return;
        if (_layout.PaneCount < 2)
        {
            if (source.Tabs.Items.Count > 1) SplitPane(source, SplitDirection.Horizontal, tab);
            return;
        }
        var target = _panes[_layout.NextPane(source.Id, 1)];
        MoveTabToPane(tab, target);
        ActivatePane(target);
        if (source.Tabs.Items.Count == 0) RemovePane(source);
        OnLayoutChanged();
        FocusSelected(target);
    }

    /// <summary>アクティブペインの選択中タブを次のペインへ移動する。</summary>
    public void MoveActiveTabToNextPane()
    {
        if (ResolveActivePane().Tabs.SelectedItem is TabItem tab) MoveToNextPane(tab);
    }

    /// <summary>タブとセッション本体を別ペインへ付け替える。一度だけ WindowsFormsHost の HWND 再作成が
    /// 走るが接続は維持される（タブ切替の常時コストとは異なり明示操作の一回きりのため許容）。</summary>
    private void MoveTabToPane(TabItem tab, Pane target)
    {
        if (PaneOf(tab) is not { } source || source == target || SessionOf(tab) is not { } session) return;
        // 移動元から外すと隣のタブが自動選択されて MRU の2番手に割り込み、Ctrl+Tab の戻り先が変わるため、
        // 付け替え中は MRU・表示の追従を止め、移動したタブだけを MRU 先頭へ記録する（表示は下で明示同期）
        _reordering = true;
        try
        {
            source.Tabs.Items.Remove(tab);
            source.Host.Children.Remove(session);
            target.Host.Children.Add(session);
            target.Tabs.Items.Add(tab);
            target.Tabs.SelectedItem = tab;
        }
        finally { _reordering = false; }
        TrackMruSelection(target);
        SyncSessionVisibility(source);
        SyncSessionVisibility(target);
        UpdatePaneChrome(source);
        UpdatePaneChrome(target);
    }

    /// <summary>同じペイン内でタブを左右へ移動する。</summary>
    private void MoveTab(TabItem tab, int delta)
    {
        if (PaneOf(tab) is not { } pane) return;
        var items = pane.Tabs.Items;
        int oldIndex = items.IndexOf(tab);
        int newIndex = Math.Clamp(oldIndex + delta, 0, items.Count - 1);
        if (oldIndex == newIndex) return;
        _reordering = true;
        try
        {
            items.RemoveAt(oldIndex);
            items.Insert(newIndex, tab);
            pane.Tabs.SelectedItem = tab;
        }
        finally { _reordering = false; }
        ActivatePane(pane);
        FocusSelected(pane);
    }

    /// <summary>アクティブタブを同じペイン内で左右へ移動する。</summary>
    public void MoveActiveTab(int delta)
    {
        if (ResolveActivePane().Tabs.SelectedItem is TabItem tab) MoveTab(tab, delta);
    }

    /// <summary>次のペインへフォーカスを移す（F6 / Ctrl+Alt+F6）。空のペインも対象にする
    /// （空ペインを操作対象にしてからツリーで接続を開けるように）。</summary>
    public void FocusNextPane()
    {
        if (_layout.PaneCount < 2) return;
        var next = _panes[_layout.NextPane(ResolveActivePane().Id, 1)];
        ActivatePane(next);
        if (next.Tabs.Items.Count == 0) { RestoreFocusToWindow(); return; }
        if (next.Tabs.SelectedItem is null) next.Tabs.SelectedIndex = 0;
        if (next.Tabs.SelectedItem is TabItem tab) TouchMru(tab);
        FocusSelected(next);
    }

    /// <summary>空ペインを操作対象にしたとき、RDP セッションにキーボードフォーカスが残っていると
    /// 以降のキー（Alt+N でツリーへ等）がリモートへ流れるため、メインウィンドウ側へ戻す。</summary>
    private void RestoreFocusToWindow()
    {
        if (Window.GetWindow(_canvas) is { } w)
            _canvas.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => w.Focus()));
    }

    private Pane ResolveActivePane()
        => _panes.ContainsValue(_activePane) ? _activePane : _panes[_layout.PaneIds[0]];

    private static void FocusSelected(Pane pane)
    {
        if (pane.Tabs.SelectedItem is TabItem ti && SessionOf(ti) is { } s)
            pane.Tabs.Dispatcher.BeginInvoke(new Action(s.FocusSession), DispatcherPriority.Input);
    }

    /// <summary>全セッションのリモート解像度を現在サイズへ即時反映する
    /// （ウィンドウドラッグ終了・全画面切替・スプリッター確定などサイズ確定時用）。</summary>
    public void ApplyResizeToAll()
    {
        foreach (var tab in AllTabs)
            SessionOf(tab)?.ApplyResizeNow();
    }

    // ── レイアウトの保存・復元 ──

    /// <summary>現在の分割構成（<see cref="PaneLayout.Serialize"/> 形式）と、ペイン順に各ペインで開いているノード ID。</summary>
    public (string Layout, List<List<string>> PaneNodeIds) SaveLayout()
    {
        var ids = PanesInOrder
            .Select(p => p.Tabs.Items.OfType<TabItem>()
                .Select(t => (t.Tag as SessionTag)?.NodeId)
                .Where(id => !string.IsNullOrEmpty(id)).Cast<string>().ToList())
            .ToList();
        return (_layout.Serialize(), ids);
    }

    /// <summary>
    /// 起動時の復元用に分割構成を再現する（セッションが無い状態で呼ぶ）。ペインは空で作られ、
    /// 呼び出し側が OpenSession(paneIndex: …) で中身を開いた後に <see cref="PruneEmptyPanes"/> を呼ぶ。
    /// 壊れた文字列なら何もしない（1ペインのまま）。
    /// </summary>
    public void RestoreLayout(string? layout)
    {
        if (SessionCount > 0 || PaneLayout.Parse(layout) is not { } parsed) return;
        foreach (var pane in _panes.Values.ToList())
            _canvas.Children.Remove(pane.Frame);
        _panes.Clear();
        _layout = parsed;
        foreach (var id in _layout.PaneIds) CreatePane(id);
        _activePane = _panes[_layout.PaneIds[0]];
        OnLayoutChanged();
    }

    /// <summary>セッションが1つも開けなかったペイン（接続が削除された等）を閉じる。</summary>
    public void PruneEmptyPanes()
    {
        bool changed = false;
        foreach (var pane in PanesInOrder.ToList())
            if (pane.Tabs.Items.Count == 0 && _layout.PaneCount > 1) { RemovePane(pane); changed = true; }
        if (changed) OnLayoutChanged();
    }
}
