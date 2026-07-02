using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using RdpManager.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace RdpManager;

public partial class ConnectionEditDialog : Window
{
    private readonly TreeNodeViewModel _node;

    public ConnectionEditDialog(TreeNodeViewModel node, ObservableCollection<CredentialProfile> profiles)
    {
        InitializeComponent();
        _node = node;

        Title = node.IsFolder ? "Edit Folder" : "Edit Connection";
        ConnPanel.Visibility = node.IsFolder ? Visibility.Collapsed : Visibility.Visible;
        // フォルダは表示設定の「既定値」を定義する側なので継承チェックは出さない
        InheritSettingsChk.Visibility = node.IsFolder ? Visibility.Collapsed : Visibility.Visible;

        NameBox.Text = node.Name;
        ProfileBox.ItemsSource = profiles;

        // 表示/RDP 設定はフォルダ・接続ともに編集可
        GatewayBox.Text = node.Gateway;
        AuthLevelBox.SelectedIndex = node.AuthenticationLevel switch { 0 => 2, 1 => 1, _ => 0 };
        FullscreenChk.IsChecked = node.Fullscreen;
        SmartSizingChk.IsChecked = node.SmartSizing;
        ClipboardChk.IsChecked = node.RedirectClipboard;
        DrivesChk.IsChecked = node.RedirectDrives;
        InheritSettingsChk.IsChecked = node.InheritSettings;

        if (node.IsConnection)
        {
            HostBox.Text = node.Host;
            PortBox.Text = node.Port.ToString();
            CommentBox.Text = node.Comment;
            ProtocolBox.SelectedIndex = node.Protocol switch { "SSH" => 1, "Telnet" => 2, "VNC" => 3, _ => 0 };
            DomainBox.Text = node.Domain;
            UserBox.Text = node.Username;
            PassBox.Password = node.Password;
            PreCmdBox.Text = node.PreCommand;
            PostCmdBox.Text = node.PostCommand;
            ProfileBox.SelectedItem = profiles.FirstOrDefault(p => p.Name == node.CredentialProfile);

            CredModeBox.SelectedIndex = node.CredentialMode switch
            {
                "direct" => 0,
                "profile" => 1,
                "winCred" => 2,
                _ => 3
            };
            UpdateCredPanels();
        }

        UpdateSettingsEnabled();
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnInheritSettingsChanged(object sender, RoutedEventArgs e) => UpdateSettingsEnabled();

    private void UpdateSettingsEnabled()
    {
        if (SettingsDetail is null) return;
        SettingsDetail.IsEnabled = !(InheritSettingsChk.IsChecked == true);
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
            MessageBox.Show(this, "Please enter a display name.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _node.Name = NameBox.Text.Trim();

        // 表示/RDP 設定（フォルダ・接続共通）
        _node.Gateway = GatewayBox.Text.Trim();
        _node.AuthenticationLevel = int.TryParse((AuthLevelBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var al) ? al : 2;
        _node.Fullscreen = FullscreenChk.IsChecked == true;
        _node.SmartSizing = SmartSizingChk.IsChecked == true;
        _node.RedirectClipboard = ClipboardChk.IsChecked == true;
        _node.RedirectDrives = DrivesChk.IsChecked == true;
        _node.InheritSettings = _node.IsConnection && InheritSettingsChk.IsChecked == true;

        if (_node.IsConnection)
        {
            if (string.IsNullOrWhiteSpace(HostBox.Text))
            {
                MessageBox.Show(this, "Please enter a host / IP.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 1 || port > 65535)
            {
                MessageBox.Show(this, "Port must be a number between 1 and 65535.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _node.Host = HostBox.Text.Trim();
            _node.Port = port;
            _node.Comment = CommentBox.Text.Trim();
            _node.Protocol = (ProtocolBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "RDP";
            _node.CredentialMode = SelectedCredMode;
            _node.Domain = DomainBox.Text.Trim();
            _node.Username = UserBox.Text.Trim();
            _node.Password = PassBox.Password;
            _node.CredentialProfile = (ProfileBox.SelectedItem as CredentialProfile)?.Name ?? "";
            _node.PreCommand = PreCmdBox.Text.Trim();
            _node.PostCommand = PostCmdBox.Text.Trim();
        }

        DialogResult = true;
    }
}
