using System.Windows;
using System.Windows.Controls;
using RdpManager.ViewModels;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace RdpManager;

/// <summary>グローバルホットキーで呼び出すコマンドパレット型の接続クイック切替ダイアログ。</summary>
public partial class QuickSwitchDialog : Window
{
    /// <summary>フィルタ・表示用に接続ノードをラップするエントリ。</summary>
    private sealed class Entry
    {
        public required TreeNodeViewModel Node { get; init; }
        public bool IsOpen { get; init; }
        public string Name => Node.Name;
        public string HostDisplay => Node.HostDisplay;
        public string Marker => IsOpen ? "●" : "";
    }

    private readonly List<Entry> _all;
    private bool _closing;

    /// <summary>Enter/ダブルクリックで決定されたノード（未決定のまま閉じた場合は null）。</summary>
    public TreeNodeViewModel? SelectedNode { get; private set; }

    public QuickSwitchDialog(IEnumerable<TreeNodeViewModel> connections, IReadOnlySet<string> openNodeIds)
    {
        InitializeComponent();
        _all = connections
            .Select(n => new Entry { Node = n, IsOpen = openNodeIds.Contains(n.Id.ToString()) })
            .OrderByDescending(e => e.IsOpen)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ResultsList.ItemsSource = _all;
        if (_all.Count > 0) ResultsList.SelectedIndex = 0;
        Deactivated += (_, _) => CloseOnce();
    }

    /// <summary>Close() は Deactivated を同期的に発火させるため、多重呼び出しで
    /// InvalidOperationException(VerifyNotClosing) にならないようガードする。</summary>
    private void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    /// <summary>RDP セッションにフォーカスがあっても手前に出て文字入力を受けられるようにする。</summary>
    public void FocusFilter()
    {
        FilterBox.Focus();
        Keyboard.Focus(FilterBox);
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        var q = FilterBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(en => en.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                en.Node.Host.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        ResultsList.ItemsSource = filtered;
        if (filtered.Count > 0) ResultsList.SelectedIndex = 0;
    }

    /// <summary>フォーカスが FilterBox / ResultsList のどちらにあっても効くよう、
    /// ウィンドウレベルで上下・Enter・Esc を処理する。</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down) { MoveSelection(1); e.Handled = true; }
        else if (e.Key == Key.Up) { MoveSelection(-1); e.Handled = true; }
        else if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseOnce(); e.Handled = true; }
    }

    private void MoveSelection(int delta)
    {
        if (ResultsList.Items.Count == 0) return;
        var idx = Math.Clamp(ResultsList.SelectedIndex + delta, 0, ResultsList.Items.Count - 1);
        ResultsList.SelectedIndex = idx;
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => Commit();

    private void Commit()
    {
        if (ResultsList.SelectedItem is Entry entry)
        {
            SelectedNode = entry.Node;
            CloseOnce();
        }
    }
}
