using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using RdpManager.Controls;
using RdpManager.Services;
using RdpManager.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Key = System.Windows.Input.Key;
using MessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DragDrop = System.Windows.DragDrop;
using DependencyObject = System.Windows.DependencyObject;
using TabControl = System.Windows.Controls.TabControl;
using TabItem = System.Windows.Controls.TabItem;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;

namespace RdpManager.Views;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        Vm.Error += msg => MessageBox.Show(this, msg, "rdpmanager", MessageBoxButton.OK, MessageBoxImage.Warning);
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => UnregisterHotkey();
        DarkModeItem.IsChecked = App.Settings.DarkMode;
        RestoreSessionsItem.IsChecked = App.Settings.RestoreSessions;
        FullscreenSpanItem.IsChecked = App.Settings.FullscreenSpan;
        PerformanceModeItem.IsChecked = App.Settings.PerformanceMode;
        AutoReconnectItem.IsChecked = App.Settings.AutoReconnect;
        UseMultimonItem.IsChecked = App.Settings.UseMultimon;
        EnableLoggingItem.IsChecked = App.Settings.EnableLogging;
        RemoteNotificationsItem.IsChecked = App.Settings.RemoteNotifications;
        Loaded += OnLoadedRestore;
        Closing += OnClosingSaveSessions;

        _sessions = new SessionManager(SessionTabs, SessionTabsRight, SessionHost, SessionHostRight, EmptyHint,
            LeftCol, RightCol, RightSplitterCol, RightSplitter);
        // スプリッター確定時はリサイズデバウンス(400ms)を待たずにリモート解像度を即時反映する
        Splitter.DragCompleted += (_, _) => _sessions.ApplyResizeToAll();
        RightSplitter.DragCompleted += (_, _) => _sessions.ApplyResizeToAll();
        // 最大化/復元（最大化ボタン・Win+↑↓・タイトルバーダブルクリック）はモーダルサイズループを
        // 通らず WM_EXITSIZEMOVE が発生しないため、StateChanged をサイズ確定点として
        // レイアウト確定後にデバウンスを待たず即時反映する
        StateChanged += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(_sessions.ApplyResizeToAll));
        SessionTabs.SelectionChanged += (s, _) => { if (s == SessionTabs) _sessions.OnPaneActivated(SessionTabs); };
        SessionTabsRight.SelectionChanged += (s, _) => { if (s == SessionTabsRight) _sessions.OnPaneActivated(SessionTabsRight); };
        // SelectionChanged は選択が変化した時しか発火しないため、タブヘッダの再クリックも拾う
        SessionTabs.PreviewMouseDown += (_, _) => _sessions.OnPaneActivated(SessionTabs);
        SessionTabsRight.PreviewMouseDown += (_, _) => _sessions.OnPaneActivated(SessionTabsRight);
        _sessions.SessionsChanged += UpdateSessionCount;
        _sessions.SessionNotification += OnSessionNotification;
        _sessions.ClipboardSyncCompleted += OnClipboardSyncCompleted;
        // セッション内の Ctrl+Alt+Break / カスタムキーによる全画面切替要求（COM イベントから届くため UI スレッドへ移す）。
        // 自分で FullScreen プロパティを設定した際にも発火するため、状態が変わる時だけトグルする
        _sessions.FullscreenChangeRequested += on => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (on != _fullscreen) ToggleFullscreen();
        }));
        // トーストクリックは COM 活性化スレッドから届くため UI スレッドへ移す
        ToastService.Activated += key => Dispatcher.BeginInvoke(new Action(() => FocusSessionFromToast(key)));

        RestoreWindowBounds();
    }

    private void UpdateSessionCount()
    {
        int n = _sessions.SessionCount;
        SessionCountText.Text = $"{n} session(s)";
        Title = n == 0 ? "rdpmanager" : $"rdpmanager ({n})";
    }

    /// <summary>前回終了時のウィンドウ位置・サイズを復元（画面外に出る場合は既定のまま）。</summary>
    private void RestoreWindowBounds()
    {
        var s = App.Settings;
        if (s.WindowLeft is { } l && s.WindowTop is { } t &&
            s.WindowWidth is { } w && s.WindowHeight is { } h &&
            w >= 400 && h >= 300)
        {
            double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
            // モニタ構成が変わっていても、一部でも画面内に入るときだけ復元する
            if (l < vl + vw - 100 && l + w > vl + 100 && t < vt + vh - 100 && t + h > vt)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = l; Top = t; Width = w; Height = h;
            }
        }
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindowBounds()
    {
        var s = App.Settings;
        // 全画面中はトグル前の通常時の値を、最大化中は復元境界を保存する
        var bounds = _fullscreen ? _savedBounds
            : WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        s.WindowMaximized = _fullscreen
            ? _savedState == WindowState.Maximized
            : WindowState == WindowState.Maximized;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            s.WindowLeft = bounds.Left; s.WindowTop = bounds.Top;
            s.WindowWidth = bounds.Width; s.WindowHeight = bounds.Height;
        }
    }

    private void OnLoadedRestore(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedRestore;
        if (!App.Settings.RestoreSessions) return;
        foreach (var id in App.Settings.OpenOnExit.ToList())
        {
            var node = Vm.FindConnectionById(id);
            if (node != null) ConnectEmbedded(node);
        }
    }

    private void OnClosingSaveSessions(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var tabs = _sessions.AllTabs.ToList();
        int active = tabs.Count(t => SessionManager.SessionOf(t) is { } s &&
                                     s.VisualState != Controls.SessionVisualState.Disconnected);
        if (active > 0 && MessageBox.Show(this,
                $"{active} session(s) are still connected.\nExit and disconnect them all?",
                "Exit rdpmanager", MessageBoxButton.OKCancel, MessageBoxImage.Question)
            != MessageBoxResult.OK)
        {
            e.Cancel = true;
            return;
        }

        var ids = tabs
            .Select(t => (t.Tag as SessionTag)?.NodeId)
            .Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToList();
        App.Settings.OpenOnExit = ids;
        SaveWindowBounds();
        App.Settings.Save();

        // 各セッションを正規の手順で閉じる（切断 + PostCommand 実行）
        foreach (var tab in tabs)
            if (SessionManager.SessionOf(tab) is { } s)
                _sessions.CloseSession(tab, s);
    }

    private void OnToggleRestoreSessions(object sender, RoutedEventArgs e)
    {
        App.Settings.RestoreSessions = RestoreSessionsItem.IsChecked;
        App.Settings.Save();
    }

    private void OnToggleDarkMode(object sender, RoutedEventArgs e)
    {
        App.Settings.DarkMode = DarkModeItem.IsChecked;
        Services.ThemeManager.Apply(App.Settings.DarkMode);
        App.Settings.Save();
    }

    // ── 全画面トグル（F11 グローバルホットキー）──
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HotkeyF11 = 0x9001;
    private const int HotkeyPause = 0x9002;
    private const int HotkeyBreak = 0x9003;
    private const int HotkeyNextTab = 0x9004;
    private const int HotkeyPrevTab = 0x9005;
    private const int HotkeyQuickSwitch = 0x9006;
    private const int HotkeyFullscreenCustom = 0x9007;
    private const int HotkeyFocusPane = 0x9008;
    private const int HotkeyClipboardToRemote = 0x9009;
    private const int HotkeyClipboardFromRemote = 0x900A;
    private const int HotkeyTab1 = 0x9010; // 0x9010..0x9018 = Ctrl+Alt+1..9
    private const int WmHotkey = 0x0312;
    private const int WmExitSizeMove = 0x0232;
    private const uint VkF11 = 0x7A;
    private const uint VkPause = 0x13;   // Pause
    private const uint VkCancel = 0x03;  // Ctrl+Pause = Break
    private const uint VkPageUp = 0x21;
    private const uint VkPageDown = 0x22;
    private const uint VkF6 = 0x75;
    private const uint VkC = 0x43;
    private const uint VkV = 0x56;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;

    private IntPtr _hwnd;
    private bool _fullscreen;
    private readonly SessionManager _sessions;
    private QuickSwitchDialog? _quickSwitch;
    private WindowStyle _savedStyle;
    private ResizeMode _savedResize;
    private WindowState _savedState;
    private GridLength _savedTreeWidth;
    private Rect _savedBounds;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(_hwnd);
        src?.AddHook(WndProc);
        // RDP セッションにフォーカスがあっても効くようグローバル登録
        TryRegisterHotKey(HotkeyPause, ModControl | ModAlt, VkPause, "Ctrl+Alt+Pause");
        TryRegisterHotKey(HotkeyBreak, ModControl | ModAlt, VkCancel, "Ctrl+Alt+Break");
        // クリップボード同期は全画面の RDP にフォーカスがあっても使える必要があるため常時登録する
        TryRegisterHotKey(HotkeyClipboardToRemote, ModControl | ModAlt | ModShift, VkV, "Ctrl+Alt+Shift+V");
        TryRegisterHotKey(HotkeyClipboardFromRemote, ModControl | ModAlt | ModShift, VkC, "Ctrl+Alt+Shift+C");
        // 全画面中も解除キーとして機能させるため、Pause/Break と同様に RegisterAuxHotkeys には含めない
        if (App.Settings.FullscreenKey != 0)
            TryRegisterHotKey(HotkeyFullscreenCustom, App.Settings.FullscreenModifiers, App.Settings.FullscreenKey,
                HotkeyCaptureDialog.BuildDisplayText(App.Settings.FullscreenModifiers, App.Settings.FullscreenKey));
        RegisterAuxHotkeys();
        QuickSwitchMenuItem.InputGestureText = HotkeyCaptureDialog.BuildDisplayText(App.Settings.QuickSwitchModifiers, App.Settings.QuickSwitchKey);
        UpdateFullscreenMenuGesture();
    }

    // 他アプリ（旧インスタンス含む）がキーを保持していると RegisterHotKey は失敗する。
    // 従来は無警告だったため「F11 が黙って効かない」ように見えた — 失敗をステータスバーに表示する
    private readonly List<string> _failedHotkeys = new();

    private void TryRegisterHotKey(int id, uint fsModifiers, uint vk, string displayName)
    {
        if (RegisterHotKey(_hwnd, id, fsModifiers, vk))
            _failedHotkeys.Remove(displayName);
        else if (!_failedHotkeys.Contains(displayName))
            _failedHotkeys.Add(displayName);
        UpdateHotkeyWarning();
    }

    private void UpdateHotkeyWarning()
    {
        HotkeyWarningItem.Visibility = _failedHotkeys.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HotkeyWarningText.Text = _failedHotkeys.Count == 0 ? ""
            : "⚠ Hotkey(s) in use by another app: " + string.Join(", ", _failedHotkeys);
    }

    /// <summary>View メニューの Toggle Full Screen 項目に、既定キーに加えて設定済みの追加ホットキーを表示する。</summary>
    private void UpdateFullscreenMenuGesture()
    {
        FullscreenMenuItem.InputGestureText = App.Settings.FullscreenKey == 0
            ? "F11 / Ctrl+Alt+Pause"
            : $"F11 / Ctrl+Alt+Pause / {HotkeyCaptureDialog.BuildDisplayText(App.Settings.FullscreenModifiers, App.Settings.FullscreenKey)}";
    }

    // Pause/Break（全画面解除）以外の補助ホットキー一式。全画面中は解除し純正 mstsc 同様にキーをリモートへ流す
    private void RegisterAuxHotkeys()
    {
        TryRegisterHotKey(HotkeyF11, 0, VkF11, "F11");
        TryRegisterHotKey(HotkeyNextTab, ModControl | ModAlt, VkPageDown, "Ctrl+Alt+PageDown");
        TryRegisterHotKey(HotkeyPrevTab, ModControl | ModAlt, VkPageUp, "Ctrl+Alt+PageUp");
        TryRegisterHotKey(HotkeyQuickSwitch, App.Settings.QuickSwitchModifiers, App.Settings.QuickSwitchKey,
            HotkeyCaptureDialog.BuildDisplayText(App.Settings.QuickSwitchModifiers, App.Settings.QuickSwitchKey)); // 設定可能な Quick Switch ホットキー
        TryRegisterHotKey(HotkeyFocusPane, ModControl | ModAlt, VkF6, "Ctrl+Alt+F6"); // 分割ペイン間のフォーカス切替
        for (uint i = 0; i < 9; i++)
            TryRegisterHotKey(HotkeyTab1 + (int)i, ModControl | ModAlt, 0x31 + i, $"Ctrl+Alt+{i + 1}"); // Ctrl+Alt+1..9
    }

    private void UnregisterAuxHotkeys()
    {
        UnregisterHotKey(_hwnd, HotkeyF11);
        UnregisterHotKey(_hwnd, HotkeyNextTab);
        UnregisterHotKey(_hwnd, HotkeyPrevTab);
        UnregisterHotKey(_hwnd, HotkeyQuickSwitch);
        UnregisterHotKey(_hwnd, HotkeyFocusPane);
        for (int i = 0; i < 9; i++) UnregisterHotKey(_hwnd, HotkeyTab1 + i);
    }

    private void UnregisterHotkey()
    {
        if (_hwnd == IntPtr.Zero) return;
        UnregisterHotKey(_hwnd, HotkeyPause);
        UnregisterHotKey(_hwnd, HotkeyBreak);
        UnregisterHotKey(_hwnd, HotkeyFullscreenCustom);
        UnregisterHotKey(_hwnd, HotkeyClipboardToRemote);
        UnregisterHotKey(_hwnd, HotkeyClipboardFromRemote);
        UnregisterAuxHotkeys();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            var id = wParam.ToInt32();
            if (id == HotkeyF11 || id == HotkeyPause || id == HotkeyBreak || id == HotkeyFullscreenCustom)
            {
                ToggleFullscreen();
                handled = true;
            }
            else if (id == HotkeyNextTab) { _sessions.CycleTab(+1); handled = true; }
            else if (id == HotkeyPrevTab) { _sessions.CycleTab(-1); handled = true; }
            else if (id == HotkeyQuickSwitch) { OnQuickSwitch(this, new RoutedEventArgs()); handled = true; }
            else if (id == HotkeyFocusPane) { _sessions.FocusOtherPane(); handled = true; }
            else if (id == HotkeyClipboardToRemote) { _sessions.SyncActiveClipboard(ClipboardSyncDirection.LocalToRemote); handled = true; }
            else if (id == HotkeyClipboardFromRemote) { _sessions.SyncActiveClipboard(ClipboardSyncDirection.RemoteToLocal); handled = true; }
            else if (id >= HotkeyTab1 && id < HotkeyTab1 + 9) { _sessions.JumpToTab(id - HotkeyTab1); handled = true; }
        }
        else if (msg == WmExitSizeMove)
        {
            // ウィンドウのドラッグリサイズ終了時はデバウンス(400ms)を待たず即時にリモート解像度を合わせる
            // （移動のみの場合はサイズ不変のため RdpClientHost 側の同一サイズ再送抑止で無視される）
            _sessions.ApplyResizeToAll();
        }
        return IntPtr.Zero;
    }

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _savedStyle = WindowStyle;
            _savedResize = ResizeMode;
            _savedState = WindowState;
            _savedTreeWidth = TreeColumn.Width;
            // 最大化中は Left/Top/Width/Height が最大化後の値になるため、その場合は RestoreBounds（通常時の境界）を保存する
            _savedBounds = WindowState == WindowState.Normal || RestoreBounds.IsEmpty
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            MainMenu.Visibility = Visibility.Collapsed;
            MainToolBar.Visibility = Visibility.Collapsed;
            MainStatus.Visibility = Visibility.Collapsed;
            TreePane.Visibility = Visibility.Collapsed;
            Splitter.Visibility = Visibility.Collapsed;
            TreeColumn.MinWidth = 0; // MinWidth(200) が残ると左に空白が出るため0に
            TreeColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            if (App.Settings.FullscreenSpan)
            {
                // 全モニタにまたがる（仮想スクリーン全体）
                WindowState = WindowState.Normal;
                Left = SystemParameters.VirtualScreenLeft;
                Top = SystemParameters.VirtualScreenTop;
                Width = SystemParameters.VirtualScreenWidth;
                Height = SystemParameters.VirtualScreenHeight;
            }
            else
            {
                WindowState = WindowState.Normal; // 一旦戻してから最大化しないと境界が残ることがある
                WindowState = WindowState.Maximized;
            }
            // 純正 mstsc 同様、全画面中は Pause/Break 以外のキーをリモートへ流すため補助ホットキーを解除する
            UnregisterAuxHotkeys();
            // KeyboardHookMode=2 と連動: 全画面中のみ Win キー組み合わせがリモートへ送られるようになる。
            // FullScreen 設定が FullscreenChangeRequested を発火させるため、_fullscreen を先に確定させて再入を防ぐ
            _fullscreen = true;
            _sessions.SetAppFullscreen(true);
            // レイアウト確定後にデバウンスを待たず即時にリモート解像度を合わせる
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(_sessions.ApplyResizeToAll));
        }
        else
        {
            MainMenu.Visibility = Visibility.Visible;
            MainToolBar.Visibility = Visibility.Visible;
            MainStatus.Visibility = Visibility.Visible;
            TreePane.Visibility = Visibility.Visible;
            Splitter.Visibility = Visibility.Visible;
            TreeColumn.MinWidth = 200;
            TreeColumn.Width = _savedTreeWidth;
            SplitterColumn.Width = GridLength.Auto;

            WindowStyle = _savedStyle;
            ResizeMode = _savedResize;
            Left = _savedBounds.Left; Top = _savedBounds.Top;
            Width = _savedBounds.Width; Height = _savedBounds.Height;
            WindowState = _savedState;
            RegisterAuxHotkeys();
            _fullscreen = false;
            _sessions.SetAppFullscreen(false);
            // レイアウト確定後にデバウンスを待たず即時にリモート解像度を合わせる
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(_sessions.ApplyResizeToAll));
            // フォーカスが RDP コントロール内に残ると Ctrl+Tab 等のアプリ側ショートカットが
            // リモートへ流れてしまうため、解除直後はアプリ側（ツリー）へキーボードフォーカスを戻す
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => Tree.Focus()));
        }
    }

    // ── ツリー ──
    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => Vm.SelectedNode = e.NewValue as TreeNodeViewModel;

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.SelectedNode?.IsConnection == true)
            ConnectEmbedded(Vm.SelectedNode);
    }

    // ── ドラッグ&ドロップ並べ替え ──
    private Point _dragStart;
    private TreeNodeViewModel? _dragNode;

    private void OnTreeMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        // Vm.SelectedNode ではなく実際に押下したノードを記録する（未選択ノードを掴んだら選択中ノードが動く誤操作を防ぐ）
        _dragNode = FindNode(e.OriginalSource as DependencyObject);
    }

    private void OnTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var diff = _dragStart - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (_dragNode is { } node)
        {
            DragDrop.DoDragDrop(Tree, node, DragDropEffects.Move);
            _dragNode = null;
        }
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TreeNodeViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TreeNodeViewModel)) is not TreeNodeViewModel dragged) return;
        var targetNode = FindNode(e.OriginalSource as DependencyObject);
        if (targetNode is null)
        {
            Vm.MoveNode(dragged, null); // 空き領域 → ルート末尾へ
        }
        else if (targetNode.IsFolder)
        {
            Vm.MoveNode(dragged, targetNode); // フォルダへ移動（末尾）
        }
        else
        {
            // 接続の上にドロップ → その兄弟として同じ位置へ挿入（同一フォルダ内の並べ替え）
            var siblings = targetNode.Parent?.Children ?? Vm.RootNodes;
            Vm.MoveNode(dragged, targetNode.Parent, siblings.IndexOf(targetNode));
        }
        e.Handled = true;
    }

    private void OnDuplicateNode(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedNode is { } node) Vm.Duplicate(node);
    }

    private static TreeNodeViewModel? FindNode(DependencyObject? src)
    {
        while (src != null && src is not TreeViewItem)
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        return (src as TreeViewItem)?.DataContext as TreeNodeViewModel;
    }

    // ── 接続 ──
    private void OnConnectEmbedded(object sender, RoutedEventArgs e) => ConnectEmbedded(Vm.SelectedNode);
    private void OnConnectExternal(object sender, RoutedEventArgs e) => Vm.ConnectExternal(Vm.SelectedNode);

    private void OnConnectRight(object sender, RoutedEventArgs e) => ConnectEmbedded(Vm.SelectedNode, SessionTabsRight);

    private void ConnectEmbedded(TreeNodeViewModel? node, TabControl? target = null)
    {
        // 同じ接続のタブが既にあれば前面に出すだけ（PreCommand の再実行や二重接続を避ける）。
        // 右ペインを明示指定した場合は分割表示用の2セッション目として従来どおり開く。
        if (target is null && node?.IsConnection == true &&
            string.Equals(node.Protocol, "RDP", StringComparison.OrdinalIgnoreCase) &&
            _sessions.TryActivateExisting(node.Id.ToString()))
        {
            return;
        }

        var info = Vm.BuildLaunchInfo(node);
        if (info is null) return;
        Services.ExternalTools.Run(node!.PreCommand, info);

        // RDP 以外は対応する外部クライアントで起動
        if (!string.Equals(node.Protocol, "RDP", StringComparison.OrdinalIgnoreCase))
        {
            if (!Services.ProtocolLauncher.Launch(node.Protocol, info.Host, info.Port, info.Username, out var msg)
                && msg != null)
                MessageBox.Show(this, msg, "rdpmanager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _sessions.OpenSession(info, node.Name, node.Id.ToString(), node.PostCommand, target ?? SessionTabs);
    }

    private static IEnumerable<TreeNodeViewModel> DescendantConnections(TreeNodeViewModel folder)
    {
        foreach (var c in folder.Children)
        {
            if (c.IsConnection) yield return c;
            else foreach (var d in DescendantConnections(c)) yield return d;
        }
    }

    private void OnConnectAll(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedNode is not { IsFolder: true } folder) return;
        var conns = DescendantConnections(folder).ToList();
        if (conns.Count == 0) return;
        if (conns.Count > 8 && MessageBox.Show(this,
                $"Connect to {conns.Count} sessions?", "Connect All",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        foreach (var c in conns) ConnectEmbedded(c);
    }

    private void OnDisconnectAll(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedNode is not { IsFolder: true } folder) return;
        var ids = DescendantConnections(folder).Select(c => c.Id.ToString()).ToHashSet();
        foreach (var tab in _sessions.AllTabs.ToList())
            if ((tab.Tag as SessionTag)?.NodeId is { } id && ids.Contains(id) &&
                SessionManager.SessionOf(tab) is { } s)
                _sessions.CloseSession(tab, s);
    }

    private void OnSendCtrlAltDel(object sender, RoutedEventArgs e)
        => MessageBox.Show(this,
            "With the RDP session focused, press Ctrl+Alt+End to send Ctrl+Alt+Del to the remote session.\n" +
            "(The embedded RDP control does not allow injecting Ctrl+Alt+Del directly for security reasons.)",
            "Send Ctrl+Alt+Del", MessageBoxButton.OK, MessageBoxImage.Information);

    // ── キーボードショートカット ──
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        // 検索ボックス等でのテキスト入力中は F2/Delete をショートカットとして奪わない
        bool inTextInput = Keyboard.FocusedElement is System.Windows.Controls.TextBox or System.Windows.Controls.PasswordBox;

        // F11 のフォールバック: グローバル登録が生きている間は WM_HOTKEY 側で消費されここには届かない。
        // 登録失敗時や全画面中（補助ホットキー解除中）にアプリ側へフォーカスがあれば、ここでトグルできるようにする
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _fullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.N) { OnNewFolder(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.N) { OnNewConnection(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (ctrl && e.Key == Key.D) { OnDuplicateNode(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.M) { _sessions.MoveActiveTabToOtherPane(); e.Handled = true; }
        else if (ctrl && e.Key == Key.W) { _sessions.CloseActiveTab(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Tab) { ShowTabSwitcher(shift); e.Handled = true; }
        else if (e.Key == Key.F6) { _sessions.FocusOtherPane(); e.Handled = true; }
        else if (!inTextInput && e.Key == Key.F2 && Vm.SelectedNode != null) { OnEditNode(this, new RoutedEventArgs()); e.Handled = true; }
        else if (!inTextInput && e.Key == Key.Delete && Vm.SelectedNode != null) { OnDeleteNode(this, new RoutedEventArgs()); e.Handled = true; }
    }

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab: MRU 順のタブスイッチャーを表示する（VS Code / Visual Studio 風）。</summary>
    private void ShowTabSwitcher(bool selectLast)
    {
        var mru = _sessions.GetMruTabs();
        if (mru.Count == 0) return;
        if (mru.Count == 1) { _sessions.ActivateTab(mru[0]); return; }

        int initialIndex = selectLast ? mru.Count - 1 : 1;
        var dlg = new TabSwitcherWindow(mru, initialIndex) { Owner = this };
        dlg.ShowDialog();
        if (dlg.SelectedTab is { } tab) _sessions.ActivateTab(tab);
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        // F2/Delete はウィンドウの PreviewKeyDown 側で処理される（ここには届かない）
        if (e.Key == Key.Enter && Vm.SelectedNode?.IsConnection == true)
        {
            // Shift+Enter は右ペインに開く（分割表示をキーボードだけで開始できるように）
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            ConnectEmbedded(Vm.SelectedNode, shift ? SessionTabsRight : null);
            e.Handled = true;
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Clear();
            Tree.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // 選択中の接続、無ければ検索結果の先頭の接続へ（キーボードだけで検索→接続を完結させる）
            var node = Vm.SelectedNode?.IsConnection == true ? Vm.SelectedNode : FirstVisibleConnection(Vm.RootNodes);
            if (node != null) ConnectEmbedded(node);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            Tree.Focus();
            e.Handled = true;
        }
    }

    private static TreeNodeViewModel? FirstVisibleConnection(IEnumerable<TreeNodeViewModel> nodes)
    {
        foreach (var n in nodes)
        {
            if (!n.IsVisible) continue;
            if (n.IsConnection) return n;
            var found = FirstVisibleConnection(n.Children);
            if (found != null) return found;
        }
        return null;
    }

    private void OnFocusSearch(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnCloseCurrentTab(object sender, RoutedEventArgs e) => _sessions.CloseActiveTab();

    private void OnMoveTabOtherPane(object sender, RoutedEventArgs e) => _sessions.MoveActiveTabToOtherPane();

    private void OnFocusOtherPane(object sender, RoutedEventArgs e) => _sessions.FocusOtherPane();

    private void OnClipboardToRemote(object sender, RoutedEventArgs e)
        => _sessions.SyncActiveClipboard(ClipboardSyncDirection.LocalToRemote);

    private void OnClipboardFromRemote(object sender, RoutedEventArgs e)
        => _sessions.SyncActiveClipboard(ClipboardSyncDirection.RemoteToLocal);

    private void OnClipboardSyncCompleted(bool success, string message)
    {
        if (!success)
            MessageBox.Show(this, message, "Clipboard Sharing", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnCloseAllTabs(object sender, RoutedEventArgs e)
    {
        int n = _sessions.SessionCount;
        if (n >= 2 && MessageBox.Show(this,
                $"Close all {n} tabs?", "Close All Tabs",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        _sessions.CloseAll();
    }

    private void OnSortChildren(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedNode is { IsFolder: true } folder) Vm.SortChildren(folder);
    }

    // ── CRUD ──
    private TreeNodeViewModel? TargetFolder()
    {
        var n = Vm.SelectedNode;
        if (n is null) return null;
        return n.IsFolder ? n : n.Parent;
    }

    private void OnNewFolder(object sender, RoutedEventArgs e)
    {
        var node = new TreeNodeViewModel { Kind = NodeKind.Folder, Name = "New Folder" };
        if (new ConnectionEditDialog(node, Vm.CredentialProfiles) { Owner = this }.ShowDialog() == true)
            Vm.AddChild(TargetFolder(), node);
    }

    private void OnNewConnection(object sender, RoutedEventArgs e)
    {
        var node = new TreeNodeViewModel { Kind = NodeKind.Connection, Name = "New Connection", CredentialMode = "direct" };
        if (new ConnectionEditDialog(node, Vm.CredentialProfiles) { Owner = this }.ShowDialog() == true)
            Vm.AddChild(TargetFolder(), node);
    }

    private void OnPatternAdd(object sender, RoutedEventArgs e)
    {
        var dlg = new PatternAddDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var parent = TargetFolder();
        foreach (var host in dlg.Hosts)
        {
            var node = new TreeNodeViewModel
            {
                Kind = NodeKind.Connection, Name = host, Host = host,
                Port = dlg.Port, CredentialMode = "inheritFromParent"
            };
            if (parent is null) { Vm.RootNodes.Add(node); node.Parent = null; }
            else { node.Parent = parent; parent.Children.Add(node); }
        }
        if (parent != null) parent.IsExpanded = true;
        Vm.NotifyEdited();
    }

    private void OnEditNode(object sender, RoutedEventArgs e)
    {
        var node = Vm.SelectedNode;
        if (node is null) return;
        if (new ConnectionEditDialog(node, Vm.CredentialProfiles) { Owner = this }.ShowDialog() == true)
            Vm.NotifyEdited();
    }

    private void OnDeleteNode(object sender, RoutedEventArgs e)
    {
        var node = Vm.SelectedNode;
        if (node is null) return;
        var kind = node.IsFolder ? "folder" : "connection";
        var extra = node.IsFolder && node.Children.Count > 0 ? "\n(All items inside will also be deleted.)" : "";
        if (MessageBox.Show(this, $"Delete {kind} \"{node.Name}\"?{extra}", "Confirm Delete",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
            Vm.Remove(node);
    }

    // ── その他 ──
    private void OnImportCsv(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "CSV (*.csv)|*.csv|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        var rows = Services.ImportExport.FromCsv(System.IO.File.ReadAllText(dlg.FileName));
        var nodes = rows.Select(r => new TreeNodeViewModel
        {
            Kind = NodeKind.Connection, Name = r.Name, Host = r.Host, Port = r.Port,
            Domain = r.Domain, Username = r.Username, Comment = r.Comment,
            CredentialMode = string.IsNullOrEmpty(r.Username) ? "inheritFromParent" : "direct"
        }).ToList();
        Vm.AddImported(nodes, TargetFolder());
        MessageBox.Show(this, $"Imported {nodes.Count} connection(s).", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnImportRdp(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "RDP (*.rdp)|*.rdp", Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        var nodes = new List<TreeNodeViewModel>();
        foreach (var file in dlg.FileNames)
        {
            var conn = Services.ImportExport.FromRdp(System.IO.File.ReadAllText(file),
                System.IO.Path.GetFileNameWithoutExtension(file));
            if (conn is null) continue;
            nodes.Add(new TreeNodeViewModel
            {
                Kind = NodeKind.Connection, Name = conn.Name, Host = conn.Host, Port = conn.Port,
                Domain = conn.Domain, Username = conn.Username,
                CredentialMode = string.IsNullOrEmpty(conn.Username) ? "inheritFromParent" : "direct"
            });
        }
        Vm.AddImported(nodes, TargetFolder());
        MessageBox.Show(this, $"Imported {nodes.Count} connection(s).", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnExportCsv(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "rdpmanager-connections.csv" };
        if (dlg.ShowDialog(this) != true) return;
        var rows = Vm.GetAllConnections().Select(c =>
            new Services.ImportedConn(c.Name, c.Host, c.Port, c.Domain, c.Username, c.Comment));
        System.IO.File.WriteAllText(dlg.FileName, Services.ImportExport.ToCsv(rows), new System.Text.UTF8Encoding(true));
        MessageBox.Show(this, "Export complete.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnOpenStoreFolder(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(ConnectionStore.Directory);
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = ConnectionStore.Directory, UseShellExecute = true });
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        var r = await Services.UpdateChecker.CheckAsync();
        if (r is null)
        {
            MessageBox.Show(this, "Could not retrieve update information.", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (r.IsNewer)
        {
            if (MessageBox.Show(this, $"A new version {r.LatestTag} is available (current v{r.Current}).\nOpen the download page?",
                    "Check for Updates", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(Services.UpdateChecker.ReleasesUrl) { UseShellExecute = true });
        }
        else
        {
            MessageBox.Show(this, $"You are using the latest version (v{r.Current}).", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnAbout(object sender, RoutedEventArgs e)
        => MessageBox.Show(this,
            "rdpmanager\nOrganize connections in a tree and display RDP sessions embedded in tabs within this window.\nCredentials are stored encrypted with DPAPI.",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);

    private void OnManageProfiles(object sender, RoutedEventArgs e)
    {
        var dlg = new CredentialProfilesDialog(Vm.CredentialProfiles) { Owner = this };
        dlg.ShowDialog();
        if (dlg.Renames.Count > 0) Vm.ApplyProfileRenames(dlg.Renames);
        if (dlg.Changed) Vm.NotifyEdited();
    }

    private void OnSessionDashboard(object sender, RoutedEventArgs e)
    {
        var entries = new List<SessionEntry>();
        foreach (var tab in _sessions.AllTabs)
        {
            if (SessionManager.SessionOf(tab) is not { } s) continue;
            var capturedTab = tab;
            Brush color = s.VisualState switch
            {
                Controls.SessionVisualState.Connected => Brushes.LimeGreen,
                Controls.SessionVisualState.Disconnected => Brushes.Gray,
                Controls.SessionVisualState.Reconnecting => Brushes.Gold,
                _ => Brushes.Orange
            };
            var info = (tab.Tag as SessionTag)?.Info;
            entries.Add(new SessionEntry
            {
                Title = (tab.Header as System.Windows.Controls.StackPanel)?.Children
                    .OfType<System.Windows.Controls.TextBlock>().FirstOrDefault()?.Text ?? "Session",
                Host = info?.Host ?? "",
                StateText = s.VisualState.ToString(),
                StateColor = color,
                Activate = () => { if (capturedTab.Parent is TabControl tc) tc.SelectedItem = capturedTab; }
            });
        }
        if (entries.Count == 0)
        {
            MessageBox.Show(this, "There are no active sessions.", "Sessions", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new SessionsDialog(entries) { Owner = this }.ShowDialog();
    }

    // ── クイック切替（Ctrl+Alt+Home グローバルホットキー）──
    private void OnQuickSwitch(object sender, RoutedEventArgs e)
    {
        if (_quickSwitch != null) return; // 二重表示防止

        var openIds = _sessions.AllTabs
            .Select(t => (t.Tag as SessionTag)?.NodeId)
            .Where(id => !string.IsNullOrEmpty(id)).Cast<string>().ToHashSet();
        var dlg = new QuickSwitchDialog(Vm.GetAllConnections(), openIds) { Owner = this };
        _quickSwitch = dlg;
        dlg.Closed += (_, _) =>
        {
            _quickSwitch = null;
            if (dlg.SelectedNode is { } node && !_sessions.TryActivateExisting(node.Id.ToString()))
                ConnectEmbedded(node);
        };
        dlg.Show();
        dlg.Activate();
        dlg.FocusFilter();
    }

    private void OnSetQuickSwitchHotkey(object sender, RoutedEventArgs e)
    {
        var dlg = new HotkeyCaptureDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var oldModifiers = App.Settings.QuickSwitchModifiers;
        var oldKey = App.Settings.QuickSwitchKey;
        UnregisterHotKey(_hwnd, HotkeyQuickSwitch);
        if (!RegisterHotKey(_hwnd, HotkeyQuickSwitch, dlg.Modifiers, dlg.Key))
        {
            // 他アプリと衝突している場合は旧設定に戻す
            RegisterHotKey(_hwnd, HotkeyQuickSwitch, oldModifiers, oldKey);
            MessageBox.Show(this, "This hotkey is already in use by another application.",
                "Set Quick Switch Hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        App.Settings.QuickSwitchModifiers = dlg.Modifiers;
        App.Settings.QuickSwitchKey = dlg.Key;
        App.Settings.Save();
        QuickSwitchMenuItem.InputGestureText = dlg.DisplayText;
    }

    private void OnSetFullscreenHotkey(object sender, RoutedEventArgs e)
    {
        var dlg = new HotkeyCaptureDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var oldModifiers = App.Settings.FullscreenModifiers;
        var oldKey = App.Settings.FullscreenKey;
        if (oldKey != 0) UnregisterHotKey(_hwnd, HotkeyFullscreenCustom);
        if (!RegisterHotKey(_hwnd, HotkeyFullscreenCustom, dlg.Modifiers, dlg.Key))
        {
            // 他アプリと衝突している場合は旧設定に戻す
            if (oldKey != 0) RegisterHotKey(_hwnd, HotkeyFullscreenCustom, oldModifiers, oldKey);
            MessageBox.Show(this, "This hotkey is already in use by another application.",
                "Set Fullscreen Hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        App.Settings.FullscreenModifiers = dlg.Modifiers;
        App.Settings.FullscreenKey = dlg.Key;
        App.Settings.Save();
        UpdateFullscreenMenuGesture();
    }

    private async void OnRefreshStatus(object sender, RoutedEventArgs e) => await Vm.RefreshStatusesAsync();

    private void OnToggleFullscreenMenu(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void OnToggleFullscreenSpan(object sender, RoutedEventArgs e)
    {
        App.Settings.FullscreenSpan = FullscreenSpanItem.IsChecked;
        App.Settings.Save();
    }

    private void OnTogglePerformanceMode(object sender, RoutedEventArgs e)
    {
        App.Settings.PerformanceMode = PerformanceModeItem.IsChecked;
        App.Settings.Save(); // 次回接続から反映
    }

    private void OnToggleAutoReconnect(object sender, RoutedEventArgs e)
    {
        App.Settings.AutoReconnect = AutoReconnectItem.IsChecked;
        App.Settings.Save();
    }

    private void OnToggleUseMultimon(object sender, RoutedEventArgs e)
    {
        App.Settings.UseMultimon = UseMultimonItem.IsChecked;
        App.Settings.Save(); // 次回の外部 mstsc 起動から反映
    }

    // ── リモート通知（仮想チャネル → トースト） ──
    private void OnSessionNotification(TabItem tab, string title, RemoteNotification n)
    {
        if (!App.Settings.RemoteNotifications) return;
        // 通知元が分かるよう、タブ名にホストを併記する（例: "DevPC1 (192.168.3.4)"）
        var tag = tab.Tag as SessionTag;
        var host = tag?.Info?.Host;
        if (!string.IsNullOrEmpty(host) && host != title) title = $"{title} ({host})";
        ToastService.Show(tag?.SessionKey, title, n);
    }

    /// <summary>トーストクリック: ウィンドウを前面化して通知元セッションへ移動する。</summary>
    private void FocusSessionFromToast(string key)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        _sessions.ActivateBySessionKey(key);
    }

    private void OnToggleRemoteNotifications(object sender, RoutedEventArgs e)
    {
        App.Settings.RemoteNotifications = RemoteNotificationsItem.IsChecked;
        App.Settings.Save();
    }

    private void OnExportNotifyScript(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a folder to save the remote notification script."
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try
        {
            var files = RemoteNotifyScript.Export(dlg.SelectedPath);
            MessageBox.Show(this,
                "Exported:\n" + string.Join("\n", files.Select(System.IO.Path.GetFileName)) +
                "\n\nCopy both files to the remote machine, then merge the hook sample into" +
                " ~/.claude/settings.json there (fix the script path in the sample).",
                "Export Remote Notification Script", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export failed: " + ex.Message,
                "Export Remote Notification Script", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnToggleLogging(object sender, RoutedEventArgs e)
    {
        App.Settings.EnableLogging = EnableLoggingItem.IsChecked;
        Services.Logger.Enabled = App.Settings.EnableLogging;
        App.Settings.Save();
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
