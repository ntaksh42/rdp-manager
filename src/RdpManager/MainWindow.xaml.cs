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
        DarkModeItem.IsChecked = App.Settings.DarkMode;
        RestoreSessionsItem.IsChecked = App.Settings.RestoreSessions;
        FullscreenSpanItem.IsChecked = App.Settings.FullscreenSpan;
        Loaded += OnLoadedRestore;
        Closing += OnClosingSaveSessions;
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
        var ids = SessionTabs.Items.OfType<TabItem>().Concat(SessionTabsRight.Items.OfType<TabItem>())
            .Select(t => (t.Tag as SessionTag)?.NodeId)
            .Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToList();
        App.Settings.OpenOnExit = ids;
        App.Settings.Save();
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

    private const int HotkeyId = 0x9001;
    private const int WmHotkey = 0x0312;
    private const uint VkF11 = 0x7A;

    private IntPtr _hwnd;
    private bool _fullscreen;
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
            _savedBounds = new Rect(Left, Top, Width, Height);

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
            Left = _savedBounds.Left; Top = _savedBounds.Top;
            Width = _savedBounds.Width; Height = _savedBounds.Height;
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

    private void OnConnectRight(object sender, RoutedEventArgs e) => ConnectEmbedded(Vm.SelectedNode, SessionTabsRight);

    private void ConnectEmbedded(TreeNodeViewModel? node, TabControl? target = null)
    {
        var info = Vm.BuildLaunchInfo(node);
        if (info is null) return;
        Services.ExternalTools.Run(node!.PreCommand, info);

        // RDP 以外は対応する外部クライアントで起動
        if (!string.Equals(node.Protocol, "RDP", StringComparison.OrdinalIgnoreCase))
        {
            if (!Services.ProtocolLauncher.Launch(node.Protocol, info.Host, info.Port, info.Username, out var msg)
                && msg != null)
                MessageBox.Show(this, msg, "RdpManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            Vm.RecordRecent(node);
            return;
        }

        OpenSession(info, node.Name, node.Id.ToString(), node.PostCommand, info, target ?? SessionTabs);
        Vm.RecordRecent(node);
    }

    private sealed record SessionTag(string? NodeId, string? PostCommand, LaunchInfo? Info);

    private void UpdateRightPane()
    {
        bool show = SessionTabsRight.Items.Count > 0;
        RightCol.Width = show ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        RightSplitterCol.Width = show ? GridLength.Auto : new GridLength(0);
        RightSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnQuickAccessDouble(object sender, MouseButtonEventArgs e)
    {
        if (QuickList.SelectedItem is TreeNodeViewModel node)
            ConnectEmbedded(node);
    }

    private void OnToggleFavorite(object sender, RoutedEventArgs e) => Vm.ToggleFavorite(Vm.SelectedNode);

    private void OnSendCtrlAltDel(object sender, RoutedEventArgs e)
        => MessageBox.Show(this,
            "With the RDP session focused, press Ctrl+Alt+End to send Ctrl+Alt+Del to the remote session.\n" +
            "(The embedded RDP control does not allow injecting Ctrl+Alt+Del directly for security reasons.)",
            "Send Ctrl+Alt+Del", MessageBoxButton.OK, MessageBoxImage.Information);

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
    private void OpenSession(LaunchInfo info, string title, string? nodeId = null,
                             string? postCommand = null, LaunchInfo? postInfo = null, TabControl? target = null)
    {
        target ??= SessionTabs;
        var session = new RdpSessionControl();
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center, Fill = Brushes.Orange };

        var tab = new TabItem { Content = session, Tag = new SessionTag(nodeId, postCommand, postInfo) };

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

        target.Items.Add(tab);
        target.SelectedItem = tab;
        EmptyHint.Visibility = SessionTabs.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateRightPane();

        session.Start(info);
    }

    private void CloseSession(TabItem tab, RdpSessionControl session)
    {
        session.Cleanup();
        if (tab.Tag is SessionTag { PostCommand: { Length: > 0 } cmd, Info: { } info })
            Services.ExternalTools.Run(cmd, info);
        (tab.Parent as TabControl)?.Items.Remove(tab);
        EmptyHint.Visibility = SessionTabs.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateRightPane();
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
            "RdpManager\nOrganize connections in a tree and display RDP sessions embedded in tabs within this window.\nCredentials are stored encrypted with DPAPI.",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);

    private void OnManageProfiles(object sender, RoutedEventArgs e)
    {
        var dlg = new CredentialProfilesDialog(Vm.CredentialProfiles) { Owner = this };
        dlg.ShowDialog();
        if (dlg.Changed) Vm.NotifyEdited();
    }

    private void OnSessionDashboard(object sender, RoutedEventArgs e)
    {
        var entries = new List<SessionEntry>();
        foreach (var pane in new[] { SessionTabs, SessionTabsRight })
        {
            foreach (var tab in pane.Items.OfType<TabItem>())
            {
                if (tab.Content is not RdpSessionControl s) continue;
                var capturedPane = pane; var capturedTab = tab;
                Brush color = s.VisualState switch
                {
                    Controls.SessionVisualState.Connected => Brushes.LimeGreen,
                    Controls.SessionVisualState.Disconnected => Brushes.Gray,
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
                    Activate = () => { capturedPane.SelectedItem = capturedTab; }
                });
            }
        }
        if (entries.Count == 0)
        {
            MessageBox.Show(this, "There are no active sessions.", "Sessions", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new SessionsDialog(entries) { Owner = this }.ShowDialog();
    }

    private void OnToggleFullscreenMenu(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void OnToggleFullscreenSpan(object sender, RoutedEventArgs e)
    {
        App.Settings.FullscreenSpan = FullscreenSpanItem.IsChecked;
        App.Settings.Save();
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
