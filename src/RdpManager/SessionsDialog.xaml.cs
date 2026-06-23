using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

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
}
