using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RdpManager.Controls;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using DependencyObject = System.Windows.DependencyObject;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using TabItem = System.Windows.Controls.TabItem;

namespace RdpManager;

/// <summary>Ctrl+Tab タブスイッチャーの一覧表示用エントリ。</summary>
public sealed class TabSwitchEntry
{
    public required TabItem Tab { get; init; }
    public string Title { get; init; } = "";
    public string Host { get; init; } = "";
    public Brush StateColor { get; init; } = Brushes.Gray;
}

/// <summary>
/// Ctrl+Tab / Ctrl+Shift+Tab で MRU 順にタブを切り替える VS Code / Visual Studio 風のポップアップ。
/// Ctrl キーを離すと現在の選択で確定する。
/// </summary>
public partial class TabSwitcherWindow : Window
{
    private readonly List<TabSwitchEntry> _entries;
    private bool _closing;

    /// <summary>確定されたタブ（Esc やフォーカス喪失でキャンセルした場合は null）。</summary>
    public TabItem? SelectedTab { get; private set; }

    public TabSwitcherWindow(IReadOnlyList<TabItem> mruTabs, int initialIndex)
    {
        InitializeComponent();
        _entries = mruTabs.Select(BuildEntry).ToList();
        ResultsList.ItemsSource = _entries;
        if (_entries.Count > 0)
            ResultsList.SelectedIndex = Math.Clamp(initialIndex, 0, _entries.Count - 1);
    }

    private static TabSwitchEntry BuildEntry(TabItem tab)
    {
        Brush color = tab.Content is RdpSessionControl s
            ? s.VisualState switch
            {
                SessionVisualState.Connected => Brushes.LimeGreen,
                SessionVisualState.Disconnected => Brushes.Gray,
                SessionVisualState.Reconnecting => Brushes.Gold,
                _ => Brushes.Orange
            }
            : Brushes.Orange;
        return new TabSwitchEntry
        {
            Tab = tab,
            Title = (tab.Header as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "Session",
            Host = tab.ToolTip as string ?? "",
            StateColor = color
        };
    }

    private void MoveSelection(int delta)
    {
        if (_entries.Count == 0) return;
        int idx = (ResultsList.SelectedIndex + delta + _entries.Count) % _entries.Count;
        ResultsList.SelectedIndex = idx;
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void Commit()
    {
        if (ResultsList.SelectedItem is TabSwitchEntry entry)
            SelectedTab = entry.Tab;
        CloseOnce();
    }

    /// <summary>Close() は Deactivated を同期的に発火させるため、多重呼び出しで
    /// InvalidOperationException(VerifyNotClosing) にならないようガードする。</summary>
    private void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            MoveSelection(shift ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down) { MoveSelection(1); e.Handled = true; }
        else if (e.Key == Key.Up) { MoveSelection(-1); e.Handled = true; }
        else if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseOnce(); e.Handled = true; }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        // Ctrl+Tab を押し続けている間だけ候補を巡回し、Ctrl を離した時点で確定する
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            Commit();
            e.Handled = true;
        }
    }

    private void OnListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestorListBoxItem(e.OriginalSource as DependencyObject) != null)
            Commit();
    }

    private static ListBoxItem? FindAncestorListBoxItem(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is ListBoxItem item) return item;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private void OnDeactivated(object sender, EventArgs e) => CloseOnce();
}
