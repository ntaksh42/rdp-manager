using System.Collections.ObjectModel;
using RdpManager.Common;

namespace RdpManager.ViewModels;

public enum NodeKind { Folder, Connection }

public enum NodeStatus { Unknown, Up, Down }

/// <summary>
/// ツリー上のノード（フォルダ または 接続）。1クラスで両方を表現する。
/// 画面に表示するプロパティは編集後に反映されるよう INPC 対応。
/// </summary>
public class TreeNodeViewModel : ObservableObject
{
    private string _name = "";
    private string _host = "";
    private int _port = 3389;
    private string _comment = "";
    private string _credentialMode = "inheritFromParent";
    private string _domain = "";
    private string _username = "";
    private string _gateway = "";
    private bool _isExpanded = true;
    private bool _isSelected;
    private bool _isVisible = true;
    private NodeStatus _status = NodeStatus.Unknown;

    public Guid Id { get; } = Guid.NewGuid();
    public NodeKind Kind { get; init; }
    public TreeNodeViewModel? Parent { get; set; }

    /// <summary>接続プロトコル: RDP / SSH / Telnet / VNC</summary>
    public string Protocol { get; set; } = "RDP";

    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    // ── 接続専用プロパティ（フォルダでは未使用）──
    public string Host
    {
        get => _host;
        set { if (SetField(ref _host, value)) OnPropertyChanged(nameof(HostDisplay)); }
    }

    public int Port
    {
        get => _port;
        set { if (SetField(ref _port, value)) OnPropertyChanged(nameof(HostDisplay)); }
    }

    public string Comment
    {
        get => _comment;
        set => SetField(ref _comment, value);
    }

    /// <summary>資格情報モード: 直接入力 / プロファイル参照 / 親から継承</summary>
    public string CredentialMode
    {
        get => _credentialMode;
        set => SetField(ref _credentialMode, value);
    }

    public string CredentialProfile { get; set; } = "";

    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }

    public string Domain
    {
        get => _domain;
        set => SetField(ref _domain, value);
    }

    private string _password = "";
    /// <summary>メモリ上のみ平文。保存時 DPAPI 暗号化。</summary>
    public string Password
    {
        get => _password;
        set { if (_password != value) { _password = value; CachedPasswordEnc = null; } }
    }
    /// <summary>暗号化済みパスワードのキャッシュ（平文が未変更なら保存時に再暗号化しない）。</summary>
    public string? CachedPasswordEnc { get; set; }
    public bool IsFavorite { get; set; }

    // RDP 設定
    public bool InheritSettings { get; set; } // true なら親フォルダの設定を使用
    public bool SmartSizing { get; set; } = true;
    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; }
    public bool Fullscreen { get; set; }
    /// <summary>サーバー証明書の検証レベル。0=なし / 1=警告 / 2=必須(既定)。</summary>
    public int AuthenticationLevel { get; set; } = 2;

    public string Gateway
    {
        get => _gateway;
        set => SetField(ref _gateway, value);
    }

    // 接続前後に実行する外部コマンド（{host} {port} {user} を置換）
    public string PreCommand { get; set; } = "";
    public string PostCommand { get; set; } = "";

    /// <summary>死活状態（TCP ポート到達性）。</summary>
    public NodeStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public bool IsFolder => Kind == NodeKind.Folder;
    public bool IsConnection => Kind == NodeKind.Connection;

    /// <summary>ツリー表示用アイコン。</summary>
    public string Glyph => IsFolder ? "📁" : "🖥️";

    public string HostDisplay => IsConnection
        ? RdpManager.Common.HostAddress.Format(Host, Port)
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
