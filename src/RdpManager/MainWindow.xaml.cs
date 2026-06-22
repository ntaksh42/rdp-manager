using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RdpManager.ViewModels;

namespace RdpManager;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        Vm.Error += msg => MessageBox.Show(this, msg, "RdpManager", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => Vm.SelectedNode = e.NewValue as TreeNodeViewModel;

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.SelectedNode?.IsConnection == true)
            Vm.Connect(Vm.SelectedNode);
    }

    private void OnConnectMenu(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedNode?.IsConnection == true)
            Vm.Connect(Vm.SelectedNode);
    }

    // 選択ノードが接続なら、その親フォルダを追加先にする
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
        var node = new TreeNodeViewModel
        {
            Kind = NodeKind.Connection, Name = "新しい接続", CredentialMode = "direct"
        };
        if (new ConnectionEditDialog(node, Vm.CredentialProfiles) { Owner = this }.ShowDialog() == true)
            Vm.AddChild(TargetFolder(), node);
    }

    private void OnEditNode(object sender, RoutedEventArgs e)
    {
        var node = Vm.SelectedNode;
        if (node is null) return;
        if (new ConnectionEditDialog(node, Vm.CredentialProfiles) { Owner = this }.ShowDialog() == true)
            Vm.NotifyEdited(); // 表示はノードの INPC で自動更新
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

    private void OnQuickConnect(object sender, RoutedEventArgs e)
    {
        Vm.ConnectAdHoc(QuickHostBox.Text);
        QuickHostBox.Clear();
    }

    private void OnQuickConnectKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Vm.ConnectAdHoc(QuickHostBox.Text);
            QuickHostBox.Clear();
        }
    }

    private void OnOpenStoreFolder(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(Services.ConnectionStore.Directory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = Services.ConnectionStore.Directory,
            UseShellExecute = true
        });
    }

    private void OnAbout(object sender, RoutedEventArgs e)
        => MessageBox.Show(this,
            "RdpManager\n接続先を整理し、Windows 標準の RDP クライアント（mstsc）で接続します。\n資格情報は DPAPI で暗号化保存されます。",
            "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Information);

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
