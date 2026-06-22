using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using RdpManager.ViewModels;

namespace RdpManager;

public partial class ConnectionEditDialog : Window
{
    private readonly TreeNodeViewModel _node;

    public ConnectionEditDialog(TreeNodeViewModel node, ObservableCollection<CredentialProfile> profiles)
    {
        InitializeComponent();
        _node = node;

        Title = node.IsFolder ? "フォルダの編集" : "接続の編集";
        ConnPanel.Visibility = node.IsFolder ? Visibility.Collapsed : Visibility.Visible;

        NameBox.Text = node.Name;
        ProfileBox.ItemsSource = profiles;

        if (node.IsConnection)
        {
            HostBox.Text = node.Host;
            PortBox.Text = node.Port.ToString();
            CommentBox.Text = node.Comment;
            DomainBox.Text = node.Domain;
            UserBox.Text = node.Username;
            PassBox.Password = node.Password;
            GatewayBox.Text = node.Gateway;
            FullscreenChk.IsChecked = node.Fullscreen;
            SmartSizingChk.IsChecked = node.SmartSizing;
            ClipboardChk.IsChecked = node.RedirectClipboard;
            DrivesChk.IsChecked = node.RedirectDrives;
            ProfileBox.SelectedItem = profiles.FirstOrDefault(p => p.Name == node.CredentialProfile);

            CredModeBox.SelectedIndex = node.CredentialMode switch
            {
                "direct" => 0,
                "profile" => 1,
                _ => 2
            };
            UpdateCredPanels();
        }

        NameBox.Focus();
        NameBox.SelectAll();
    }

    private string SelectedCredMode =>
        (CredModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "inheritFromParent";

    private void OnCredModeChanged(object sender, SelectionChangedEventArgs e) => UpdateCredPanels();

    private void UpdateCredPanels()
    {
        if (DirectCredPanel is null) return; // 初期化途中のガード
        var mode = SelectedCredMode;
        DirectCredPanel.Visibility = mode == "direct" ? Visibility.Visible : Visibility.Collapsed;
        ProfileCredPanel.Visibility = mode == "profile" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "表示名を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _node.Name = NameBox.Text.Trim();

        if (_node.IsConnection)
        {
            if (string.IsNullOrWhiteSpace(HostBox.Text))
            {
                MessageBox.Show(this, "ホスト名 / IP を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _node.Host = HostBox.Text.Trim();
            _node.Port = int.TryParse(PortBox.Text.Trim(), out var port) ? port : 3389;
            _node.Comment = CommentBox.Text.Trim();
            _node.CredentialMode = SelectedCredMode;
            _node.Domain = DomainBox.Text.Trim();
            _node.Username = UserBox.Text.Trim();
            _node.Password = PassBox.Password;
            _node.CredentialProfile = (ProfileBox.SelectedItem as CredentialProfile)?.Name ?? "";
            _node.Gateway = GatewayBox.Text.Trim();
            _node.Fullscreen = FullscreenChk.IsChecked == true;
            _node.SmartSizing = SmartSizingChk.IsChecked == true;
            _node.RedirectClipboard = ClipboardChk.IsChecked == true;
            _node.RedirectDrives = DrivesChk.IsChecked == true;
        }

        DialogResult = true;
    }
}
