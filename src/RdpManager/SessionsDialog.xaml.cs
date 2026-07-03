using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace RdpManager;

public sealed class SessionEntry
{
    public string Title { get; init; } = "";
    public string Host { get; init; } = "";
    public string StateText { get; init; } = "";
    public Brush StateColor { get; init; } = Brushes.Gray;
    public Action Activate { get; init; } = () => { };
}

public partial class SessionsDialog : Window
{
    public SessionsDialog(IEnumerable<SessionEntry> entries)
    {
        InitializeComponent();
        List.ItemsSource = entries.ToList();
    }

    private void OnActivate(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (List.SelectedItem is SessionEntry entry)
        {
            entry.Activate();
            Close();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (List.Items.Count > 0 && List.SelectedItem is null)
            List.SelectedIndex = 0;
        List.Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (List.SelectedItem is SessionEntry entry)
            {
                entry.Activate();
                Close();
            }
            e.Handled = true;
        }
    }
}
