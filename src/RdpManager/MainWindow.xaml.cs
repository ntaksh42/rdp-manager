using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using RdpManager.Controls;
using RdpManager.Services;
using RdpManager.ViewModels;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Key = System.Windows.Input.Key;
using Orientation = System.Windows.Controls.Orientation;
using MessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DragDrop = System.Windows.DragDrop;
using DependencyObject = System.Windows.DependencyObject;

namespace RdpManager;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        Vm.Error += msg => MessageBox.Show(this, msg, "RdpManager", MessageBoxButton.OK, MessageBoxImage.Warning);
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => UnregisterHotkey();
    }

    // ── 全画面トグル（F11 グローバルホットキー）──
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HotkeyId = 0x9001;
    private const int WmHotkey = 0x0312;
    private const uint VkF11 = 0x7A;

    private IntPtr _hwnd;
    private bool _fullscreen;
    private WindowStyle _savedStyle;
    private ResizeMode _savedResize;
    private WindowState _savedState;
    private GridLength _savedTreeWidth;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(_hwnd);
        src?.AddHook(WndProc);
        // 修飾なし F11 をグローバル登録（RDP セッションにフォーカスがあっても効かせるため）
        RegisterHotKey(_hwnd, HotkeyId, 0, VkF11);
    }

    private void UnregisterHotkey()
    {
        if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, HotkeyId);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleFullscreen();
            handled = true;
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

            MainMenu.Visibility = Visibility.Collapsed;
            MainToolBar.Visibility = Visibility.Collapsed;
            MainStatus.Visibility = Visibility.Collapsed;
            TreePane.Visibility = Visibility.Collapsed;
            Splitter.Visibility = Visibility.Collapsed;
            QuickBar.Visibility = Visibility.Collapsed;
            TreeColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal; // 一旦戻してから最大化しないと境界が残ることがある
            WindowState = WindowState.Maximized;
            _fullscreen = true;
        }
        else
        {
            MainMenu.Visibility = Visibility.Visible;
            MainToolBar.Visibility = Visibility.Visible;
            MainStatus.Visibility = Visibility.Visible;
            TreePane.Visibility = Visibility.Visible;
            Splitter.Visibility = Visibility.Visible;
            QuickBar.Visibility = Visibility.Visible;
            TreeColumn.Width = _savedTreeWidth;
            SplitterColumn.Width = GridLength.Auto;

            WindowStyle = _savedStyle;
            ResizeMode = _savedResize;
            WindowState = _savedState;
            _fullscreen = false;
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

    private void OnTreeMouseDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(null);

    private void OnTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var diff = _dragStart - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (Vm.SelectedNode is { } node)
            DragDrop.DoDragDrop(Tree, node, DragDropEffects.Move);
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
        TreeNodeViewModel? newParent = targetNode is null ? null
            : (targetNode.IsFolder ? targetNode : targetNode.Parent);
        Vm.MoveNode(dragged, newParent);
        e.Handled = true;
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

    private void ConnectEmbedded(TreeNodeViewModel? node)
    {
        var info = Vm.BuildLaunchInfo(node);
        if (info is null) return;
        OpenSession(info, node!.Name);
    }

    private void OnQuickConnect(object sender, RoutedEventArgs e) => QuickConnect();
    private void OnQuickConnectKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) QuickConnect();
    }

    private void QuickConnect()
    {
        var host = QuickHostBox.Text.Trim();
        if (string.IsNullOrEmpty(host)) return;
        OpenSession(new LaunchInfo { Host = host }, host);
        QuickHostBox.Clear();
    }

    // ── セッションタブ管理 ──
    private void OpenSession(LaunchInfo info, string title)
    {
        var session = new RdpSessionControl();
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center, Fill = Brushes.Orange };

        var tab = new TabItem { Content = session };

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

        session.StateChanged += (_, _) => dot.Fill = session.VisualState switch
        {
            SessionVisualState.Connected => Brushes.LimeGreen,
            SessionVisualState.Disconnected => Brushes.Gray,
            _ => Brushes.Orange
        };

        SessionTabs.Items.Add(tab);
        SessionTabs.SelectedItem = tab;
        EmptyHint.Visibility = Visibility.Collapsed;

        session.Start(info);
    }

    private void CloseSession(TabItem tab, RdpSessionControl session)
    {
        session.Cleanup();
        SessionTabs.Items.Remove(tab);
        if (SessionTabs.Items.Count == 0) EmptyHint.Visibility = Visibility.Visible;
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
        var node = new TreeNodeViewModel { Kind = NodeKind.Folder, Name = "新しいフォルダ" };
        if (new ConnectionEditDialog(node, Vm.CredentialProfiles) { Owner = this }.ShowDialog() == true)
            Vm.AddChild(TargetFolder(), node);
    }

    private void OnNewConnection(object sender, RoutedEventArgs e)
    {
        var node = new TreeNodeViewModel { Kind = NodeKind.Connection, Name = "新しい接続", CredentialMode = "direct" };
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
        var kind = node.IsFolder ? "フォルダ" : "接続";
        var extra = node.IsFolder && node.Children.Count > 0 ? "\n（中の項目もすべて削除されます）" : "";
        if (MessageBox.Show(this, $"{kind}「{node.Name}」を削除しますか？{extra}", "削除の確認",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
            Vm.Remove(node);
    }

    // ── その他 ──
    private void OnOpenStoreFolder(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(ConnectionStore.Directory);
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = ConnectionStore.Directory, UseShellExecute = true });
    }

    private void OnAbout(object sender, RoutedEventArgs e)
        => MessageBox.Show(this,
            "RdpManager\n接続先をツリーで整理し、このウィンドウ内のタブに RDP 画面を埋め込んで表示します。\n資格情報は DPAPI で暗号化保存されます。",
            "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Information);

    private void OnManageProfiles(object sender, RoutedEventArgs e)
    {
        var dlg = new CredentialProfilesDialog(Vm.CredentialProfiles) { Owner = this };
        dlg.ShowDialog();
        if (dlg.Changed) Vm.NotifyEdited();
    }

    private void OnToggleFullscreenMenu(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
