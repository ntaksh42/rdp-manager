using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using RdpManager.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace RdpManager;

public partial class CredentialProfilesDialog : Window
{
    private readonly ObservableCollection<CredentialProfile> _profiles;
    public bool Changed { get; private set; }
    /// <summary>このダイアログ内で行われたプロファイル改名（旧名, 新名）。参照ノードの追従に使う。</summary>
    public List<(string OldName, string NewName)> Renames { get; } = new();

    public CredentialProfilesDialog(ObservableCollection<CredentialProfile> profiles)
    {
        InitializeComponent();
        _profiles = profiles;
        ProfileList.ItemsSource = _profiles;
    }

    private CredentialProfile? Selected => ProfileList.SelectedItem as CredentialProfile;

    private void OnSelect(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is { } p)
        {
            NameBox.Text = p.Name;
            DomainBox.Text = p.Domain;
            UserBox.Text = p.Username;
            PassBox.Password = p.Password;
        }
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        ProfileList.SelectedItem = null;
        NameBox.Clear(); DomainBox.Clear(); UserBox.Clear(); PassBox.Clear();
        NameBox.Focus();
    }

    private void OnSaveProfile(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(this, "Please enter a profile name.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var target = Selected ?? _profiles.FirstOrDefault(x => x.Name == name);
        if (target is null)
        {
            target = new CredentialProfile();
            _profiles.Add(target);
        }
        else if (!string.IsNullOrEmpty(target.Name) && target.Name != name)
        {
            // 既存プロファイルの改名 → 参照しているノードの追従用に記録
            Renames.Add((target.Name, name));
        }
        target.Name = name;
        target.Domain = DomainBox.Text.Trim();
        target.Username = UserBox.Text.Trim();
        target.Password = PassBox.Password;
        Changed = true;

        // 表示更新
        ProfileList.ItemsSource = null;
        ProfileList.ItemsSource = _profiles;
        ProfileList.SelectedItem = target;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is { } p)
        {
            _profiles.Remove(p);
            Changed = true;
            OnNew(sender, e);
        }
    }
}
