using System.Collections.ObjectModel;
using RdpManager.Common;

namespace RdpManager.ViewModels;

public enum NodeKind { Folder, Connection }

/// <summary>
/// ツリー上のノード（フォルダ または 接続）。モックでは1クラスで両方を表現する。
/// </summary>
public class TreeNodeViewModel : ObservableObject
{
    private string _name = "";
    private bool _isExpanded = true;
    private bool _isSelected;
    private bool _isVisible = true;

    public Guid Id { get; } = Guid.NewGuid();
    public NodeKind Kind { get; init; }
    public TreeNodeViewModel? Parent { get; set; }

    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    // ── 接続専用プロパティ（フォルダでは未使用）──
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3389;
    public string Comment { get; set; } = "";

    /// <summary>資格情報モード: 直接入力 / プロファイル参照 / 親から継承</summary>
    public string CredentialMode { get; set; } = "inheritFromParent";
    public string CredentialProfile { get; set; } = "";
    public string Username { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Password { get; set; } = ""; // メモリ上のみ平文。保存時 DPAPI 暗号化。

    // RDP 設定
    public bool SmartSizing { get; set; } = true;
    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; }
    public bool Fullscreen { get; set; }
    public string ScreenSize { get; set; } = "クライアント領域に合わせる";
    public string Gateway { get; set; } = "";

    public bool IsFolder => Kind == NodeKind.Folder;
    public bool IsConnection => Kind == NodeKind.Connection;

    /// <summary>ツリー表示用アイコン（モックは絵文字で代用）。</summary>
    public string Glyph => IsFolder ? "📁" : "🖥️";

    public string HostDisplay => IsConnection
        ? (Port == 3389 ? Host : $"{Host}:{Port}")
        : "";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>検索フィルタ用の表示状態。</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public void Add(TreeNodeViewModel child)
    {
        child.Parent = this;
        Children.Add(child);
    }
}
