using System.Windows;
using System.Windows.Controls;
using RdpManager.Common;
using MessageBox = System.Windows.MessageBox;

namespace RdpManager;

public partial class PatternAddDialog : Window
{
    public List<string> Hosts { get; private set; } = new();
    public int Port { get; private set; } = 3389;

    public PatternAddDialog()
    {
        InitializeComponent();
        PatternBox.Focus();
    }

    private void OnPatternChanged(object sender, TextChangedEventArgs e)
    {
        var text = PatternBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) { PreviewText.Text = ""; return; }
        var hosts = PatternExpander.Expand(text);
        var sample = string.Join(", ", hosts.Take(4));
        PreviewText.Text = hosts.Count > 4
            ? $"{hosts.Count} 件: {sample} …"
            : $"{hosts.Count} 件: {sample}";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var text = PatternBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show(this, "ホストパターンを入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Hosts = PatternExpander.Expand(text);
        Port = int.TryParse(PortBox.Text.Trim(), out var p) ? p : 3389;
        DialogResult = true;
    }
}
