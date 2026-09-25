using System.Windows;
using System.Windows.Threading;
using RdpManager.Common;
using RdpManager.Controls;
using Brush = System.Windows.Media.Brush;
using FrameworkElement = System.Windows.FrameworkElement;
using ImageSource = System.Windows.Media.ImageSource;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using TabItem = System.Windows.Controls.TabItem;

namespace RdpManager.Views;

/// <summary>セッション一覧の1タイル分の表示モデル。スナップショットと状態はタイマーで随時更新する。</summary>
public sealed class OverviewTile : ObservableObject
{
    private ImageSource? _image;
    private string _meta = "";
    private int _number;
    private bool _isSelected;
    private Brush _stateColor;
    private SessionVisualState _state;

    public OverviewTile(TabItem tab, RdpSessionControl session, string title, bool rightPane)
    {
        Tab = tab;
        Session = session;
        Title = title;
        Host = session.HostDisplay;
        RightPane = rightPane;
        _state = session.VisualState;
        _stateColor = SessionManager.StateBrush(_state);
        Refresh();
    }

    public TabItem Tab { get; }
    public RdpSessionControl Session { get; }
    public string Title { get; }
    public string Host { get; }
    public bool RightPane { get; }

    public ImageSource? Image { get => _image; private set { if (SetField(ref _image, value)) OnPropertyChanged(nameof(ShowPlaceholder)); } }
    public bool ShowPlaceholder => _image is null;

    /// <summary>スナップショットの鮮度（"Live" / "12s ago"）。</summary>
    public string Meta { get => _meta; private set => SetField(ref _meta, value); }

    /// <summary>1〜9 のジャンプ番号（0 は番号なし）。</summary>
    public int Number
    {
        get => _number;
        set { if (SetField(ref _number, value)) OnPropertyChanged(nameof(HasNumber)); }
    }
    public bool HasNumber => _number > 0;

    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

    public Brush StateColor { get => _stateColor; private set => SetField(ref _stateColor, value); }
    public bool IsDisconnected => _state == SessionVisualState.Disconnected;
    /// <summary>接続中/再接続中のバッジを出すか。</summary>
    public bool IsBusy => _state is SessionVisualState.Connecting or SessionVisualState.Reconnecting;
    public string StateText => _state switch
    {
        SessionVisualState.Connected => "Connected",
        SessionVisualState.Disconnected => "Disconnected",
        SessionVisualState.Reconnecting => "Reconnecting…",
        _ => "Connecting…"
    };

    /// <summary>スポットライト表示の詳細行。</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string> { Host, StateText };
            if (RightPane) parts.Add("Right pane");
            if (!string.IsNullOrEmpty(Meta) && Meta != "Live") parts.Add($"Snapshot {Meta}");
            return string.Join(" · ", parts.Where(p => p.Length > 0));
        }
    }

    /// <summary>セッションの最新スナップショット・状態を反映する。</summary>
    public void Refresh()
    {
        Image = Session.Snapshot;
        Meta = Session.Snapshot is null ? "" : OverviewLayout.FormatAge(DateTime.UtcNow - Session.SnapshotAt);
        if (_state != Session.VisualState)
        {
            _state = Session.VisualState;
            StateColor = SessionManager.StateBrush(_state);
            OnPropertyChanged(nameof(IsDisconnected));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(StateText));
        }
        OnPropertyChanged(nameof(Detail));
    }
}

/// <summary>
/// 開いているセッションをサムネイルで一覧するオーバーレイ（Ctrl+Alt+0）。メインウィンドウのクライアント領域に
/// 重ねるオーナー付きウィンドウとして表示する（埋め込み RDP の HWND より上に WPF を描けない airspace 制約を避けるため）。
/// 小さくて読めない問題には、件数に応じたタイル最大化・絞り込みでの拡大・Space のスポットライト表示で対処する。
/// </summary>
public partial class SessionOverviewWindow : Window
{
    // タイル枠（Padding 6×2 + Border 2×2）とラベル行（高さ 22 + 余白 6）の寸法。XAML と合わせること
    private const double TileChromeWidth = 16;
    private const double TileChromeHeight = 44;
    private const double TileGap = 16;          // タイルの Margin 8 × 2
    private const double GridPadding = 8;       // ScrollViewer の Padding
    private const double MinScreenWidth = 240;  // これ未満に縮めるくらいならスクロールさせる

    public static readonly DependencyProperty GridColumnsProperty =
        DependencyProperty.Register(nameof(GridColumns), typeof(int), typeof(SessionOverviewWindow), new PropertyMetadata(1));
    public static readonly DependencyProperty TileWidthProperty =
        DependencyProperty.Register(nameof(TileWidth), typeof(double), typeof(SessionOverviewWindow), new PropertyMetadata(320.0));
    public static readonly DependencyProperty ScreenHeightProperty =
        DependencyProperty.Register(nameof(ScreenHeight), typeof(double), typeof(SessionOverviewWindow), new PropertyMetadata(180.0));
    public static readonly DependencyProperty TitleMaxWidthProperty =
        DependencyProperty.Register(nameof(TitleMaxWidth), typeof(double), typeof(SessionOverviewWindow), new PropertyMetadata(200.0));

    public int GridColumns { get => (int)GetValue(GridColumnsProperty); set => SetValue(GridColumnsProperty, value); }
    public double TileWidth { get => (double)GetValue(TileWidthProperty); set => SetValue(TileWidthProperty, value); }
    public double ScreenHeight { get => (double)GetValue(ScreenHeightProperty); set => SetValue(ScreenHeightProperty, value); }
    public double TitleMaxWidth { get => (double)GetValue(TitleMaxWidthProperty); set => SetValue(TitleMaxWidthProperty, value); }

    private readonly List<OverviewTile> _all;
    private readonly double _aspect;
    private List<OverviewTile> _visible = new();
    private OverviewTile? _selected;
    private bool _spotlight;
    private bool _closing;
    // 表示中（前面タブ）のセッションだけを定期的に撮り直す。一覧を開いている間だけ動かす
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(2) };

    /// <summary>確定されたタイル（Esc やフォーカス喪失で閉じた場合は null）。</summary>
    public OverviewTile? Result { get; private set; }

    /// <param name="tiles">表示するセッション（タブ順）。</param>
    /// <param name="initial">最初に選択するタイル（通常はアクティブなセッション）。</param>
    /// <param name="bounds">重ねる領域（DIP・スクリーン座標）。</param>
    /// <param name="aspect">リモート画面の縦横比（幅/高さ）。</param>
    public SessionOverviewWindow(IReadOnlyList<OverviewTile> tiles, OverviewTile? initial, Rect bounds, double aspect)
    {
        InitializeComponent();
        _all = tiles.ToList();
        _aspect = aspect > 0 ? aspect : 16.0 / 9.0;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        _selected = initial ?? _all.FirstOrDefault();
        _refresh.Tick += (_, _) => RefreshSnapshots(visibleOnly: true);
        Closed += (_, _) => _refresh.Stop();
        ApplyFilter();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FilterBox.Focus();
        _refresh.Start();
        // 背面タブは取得できる環境なら最新化を試みる（ウィンドウ表示を待たせないようアイドル時に1件ずつ）
        foreach (var tile in _all.Where(t => !t.Session.IsVisible))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_closing) return;
                if (tile.Session.TryUpdateSnapshot(allowScreenCopy: false)) tile.Refresh();
            }));
        }
    }

    private void RefreshSnapshots(bool visibleOnly)
    {
        foreach (var tile in _all)
        {
            if (!visibleOnly || tile.Session.IsVisible)
                tile.Session.TryUpdateSnapshot(allowScreenCopy: false);
            tile.Refresh();
        }
    }

    private void OnFilterChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        _visible = _all.Where(t => OverviewLayout.Matches(FilterBox.Text, t.Title, t.Host)).ToList();
        for (int i = 0; i < _all.Count; i++) _all[i].Number = 0;
        for (int i = 0; i < _visible.Count && i < 9; i++) _visible[i].Number = i + 1;
        if (_selected is null || !_visible.Contains(_selected)) _selected = _visible.FirstOrDefault();

        GridItems.ItemsSource = _visible;
        SpotlightItems.ItemsSource = _visible;
        CountText.Text = _visible.Count == _all.Count ? $"{_all.Count}" : $"{_visible.Count} / {_all.Count}";
        NoMatchText.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateLayoutMetrics();
        Select(_selected);
    }

    private void OnGridAreaSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutMetrics();

    /// <summary>件数と領域からタイル寸法・列数を決める（絞り込みで件数が減るほどタイルが大きくなる）。</summary>
    private void UpdateLayoutMetrics()
    {
        double width = GridScroll.ActualWidth - GridPadding * 2 - TileGap - 1;
        double height = GridScroll.ActualHeight - GridPadding * 2 - TileGap - 1;
        if (_visible.Count == 0 || width <= 0 || height <= 0) return;

        var r = OverviewLayout.Compute(_visible.Count, width, height, _aspect, TileGap,
            TileChromeWidth, TileChromeHeight, MinScreenWidth);
        // 収まりきらず縦スクロールになる場合はスクロールバーの幅を除いて計算し直す
        double totalHeight = r.Rows * (r.ScreenWidth / _aspect + TileChromeHeight + TileGap);
        if (totalHeight > height + TileGap)
            r = OverviewLayout.Compute(_visible.Count, width - SystemParameters.VerticalScrollBarWidth, height, _aspect,
                TileGap, TileChromeWidth, TileChromeHeight, MinScreenWidth);

        GridColumns = r.Columns;
        TileWidth = Math.Floor(r.TileWidth);
        ScreenHeight = Math.Floor(r.ScreenWidth / _aspect);
        TitleMaxWidth = Math.Max(80, r.ScreenWidth * 0.6);
    }

    private void Select(OverviewTile? tile)
    {
        foreach (var t in _all) t.IsSelected = t == tile;
        _selected = tile;
        SpotlightMain.DataContext = tile;
        if (tile is null) return;
        // 選択タイルが見えるようにスクロールする（レイアウト確定後）
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var items = _spotlight ? SpotlightItems : GridItems;
            (items.ItemContainerGenerator.ContainerFromItem(tile) as FrameworkElement)?.BringIntoView();
        }));
    }

    /// <summary>選択を移動する。グリッド表示の上下は1行分（列数）移動する。</summary>
    private void MoveSelection(int dx, int dy)
    {
        if (_visible.Count == 0) return;
        int idx = _selected is null ? -1 : _visible.IndexOf(_selected);
        if (idx < 0) { Select(_visible[0]); return; }
        int step = _spotlight ? dx + dy : dx + dy * Math.Max(1, GridColumns);
        int next = idx + step;
        // 上下で範囲外になる場合は動かさない（左右は端で折り返さずに止める）
        if (next < 0 || next >= _visible.Count)
        {
            if (dy != 0 && !_spotlight) return;
            next = Math.Clamp(next, 0, _visible.Count - 1);
        }
        Select(_visible[next]);
    }

    private void ToggleSpotlight()
    {
        _spotlight = !_spotlight;
        SpotlightPanel.Visibility = _spotlight ? Visibility.Visible : Visibility.Collapsed;
        GridScroll.Visibility = _spotlight ? Visibility.Collapsed : Visibility.Visible;
        SpaceHintText.Text = _spotlight ? "Back to grid" : "Enlarge";
        Select(_selected);
    }

    private void Commit(OverviewTile? tile)
    {
        if (tile is null) return;
        Result = tile;
        CloseOnce();
    }

    /// <summary>Close() は Deactivated を同期的に発火させるため、多重呼び出しを防ぐ。</summary>
    private void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    /// <summary>外部（Ctrl+Alt+0 の再押下）から閉じる。</summary>
    public void Dismiss() => CloseOnce();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool noModifiers = Keyboard.Modifiers == ModifierKeys.None;
        switch (e.Key)
        {
            case Key.Escape:
                // 絞り込み中はまず解除、2回目で閉じる
                if (FilterBox.Text.Length > 0) FilterBox.Clear();
                else CloseOnce();
                e.Handled = true;
                break;
            case Key.Enter:
                Commit(_selected);
                e.Handled = true;
                break;
            case Key.Space:
                ToggleSpotlight();
                e.Handled = true;
                break;
            case Key.Left: MoveSelection(-1, 0); e.Handled = true; break;
            case Key.Right: MoveSelection(1, 0); e.Handled = true; break;
            case Key.Up: MoveSelection(0, -1); e.Handled = true; break;
            case Key.Down: MoveSelection(0, 1); e.Handled = true; break;
            case Key.Tab: MoveSelection(shift ? -1 : 1, 0); e.Handled = true; break;
            default:
                // 数字キーは絞り込み文字が空のときだけジャンプとして扱う（入力後は絞り込み文字になる）
                if (noModifiers && FilterBox.Text.Length == 0 && DigitOf(e.Key) is int n && n >= 1 && n <= _visible.Count)
                {
                    Commit(_visible[n - 1]);
                    e.Handled = true;
                }
                break;
        }
    }

    private static int? DigitOf(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
        _ => null
    };

    private void OnTileClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OverviewTile tile)
        {
            Commit(tile);
            e.Handled = true;
        }
    }

    private void OnDeactivated(object sender, EventArgs e) => CloseOnce();
}
