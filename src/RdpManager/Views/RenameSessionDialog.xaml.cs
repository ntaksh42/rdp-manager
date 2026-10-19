using System.Windows;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace RdpManager.Views;

/// <summary>セッションタブの表示名やレイアウト名を入力するための小さなモーダルダイアログ。</summary>
public partial class RenameSessionDialog : Window
{
    public string NewName { get; private set; } = "";

    /// <summary>title / prompt を渡すと、レイアウト名の入力など別用途の名前入力ダイアログとして使える。</summary>
    public RenameSessionDialog(string currentName, string? title = null, string? prompt = null)
    {
        InitializeComponent();
        if (title != null) Title = title;
        if (prompt != null) PromptText.Text = prompt;
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
