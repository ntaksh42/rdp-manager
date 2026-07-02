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
        var total = PatternExpander.Count(text);
        if (total > PatternExpander.MaxResults)
        {
            PreviewText.Text = $"{total} host(s) — exceeds the limit of {PatternExpander.MaxResults}.";
            return;
        }
        var hosts = PatternExpander.Expand(text);
        var sample = string.Join(", ", hosts.Take(4));
        PreviewText.Text = hosts.Count > 4
            ? $"{hosts.Count} host(s): {sample} …"
            : $"{hosts.Count} host(s): {sample}";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var text = PatternBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show(this, "Please enter a host pattern.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(PortBox.Text.Trim(), out var p) || p < 1 || p > 65535)
        {
            MessageBox.Show(this, "Port must be a number between 1 and 65535.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Hosts = PatternExpander.Expand(text);
        }
        catch (PatternTooLargeException ex)
        {
            MessageBox.Show(this, ex.Message, "Too Many Hosts", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Port = p;
        DialogResult = true;
    }
}
