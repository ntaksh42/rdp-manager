using System.Windows;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace RdpManager.Views;

/// <summary>セッションタブの表示名をリネームするための小さなモーダルダイアログ。</summary>
public partial class RenameSessionDialog : Window
{
    public string NewName { get; private set; } = "";

    public RenameSessionDialog(string currentName)
    {
        InitializeComponent();
        NameBox.Text = currentName;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnNameBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnOk(sender, e);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        NewName = name;
        DialogResult = true;
    }
}
