using System.Collections.ObjectModel;
using RdpManager.Common;
using RdpManager.Services;

namespace RdpManager.ViewModels;

public class MainViewModel : ObservableObject
{
    private string _searchText = "";
    private TreeNodeViewModel? _selectedNode;

    public ObservableCollection<TreeNodeViewModel> RootNodes { get; } = new();
    public ObservableCollection<CredentialProfile> CredentialProfiles { get; } = new();

    /// <summary>接続起動などの失敗をビューへ通知する。</summary>
    public event Action<string>? Error;

    public MainViewModel()
    {
        Load();
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetField(ref _searchText, value)) ApplyFilter(); }
    }

    public TreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetField(ref _selectedNode, value))
            {
                OnPropertyChanged(nameof(HasConnectionSelected));
                OnPropertyChanged(nameof(NoConnectionSelected));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool HasConnectionSelected => SelectedNode?.IsConnection == true;
    public bool NoConnectionSelected => !HasConnectionSelected;

    public string StatusText => SelectedNode?.IsConnection == true
        ? $"{SelectedNode.Name}  |  {SelectedNode.HostDisplay}"
        : $"接続 {CountConnections(RootNodes)} 件 / 保存先: {ConnectionStore.FilePath}";

    private static int CountConnections(IEnumerable<TreeNodeViewModel> nodes)
        => nodes.Sum(n => (n.IsConnection ? 1 : 0) + CountConnections(n.Children));

    /// <summary>接続ノードから LaunchInfo を生成（資格情報を解決）。無効なら null。</summary>
    public LaunchInfo? BuildLaunchInfo(TreeNodeViewModel? node)
    {
        if (node is null || !node.IsConnection) return null;
        if (string.IsNullOrWhiteSpace(node.Host))
        {
            Error?.Invoke("ホスト名 / IP が設定されていません。");
            return null;
        }
        var (user, domain, password) = ResolveCredentials(node);
        return new LaunchInfo
        {
            Host = node.Host,
            Port = node.Port,
            Username = user,
            Domain = domain,
            Password = password,
            Fullscreen = node.Fullscreen,
            SmartSizing = node.SmartSizing,
            RedirectClipboard = node.RedirectClipboard,
            RedirectDrives = node.RedirectDrives,
            Gateway = node.Gateway
        };
    }

    /// <summary>外部 mstsc.exe で開く（埋め込みのフォールバック）。</summary>
    public void ConnectExternal(TreeNodeViewModel? node)
    {
        var info = BuildLaunchInfo(node);
        if (info is null) return;
        try { RdpLauncher.Launch(info); }
        catch (Exception ex) { Error?.Invoke($"接続の起動に失敗しました。\n{ex.Message}"); }
    }

    /// <summary>資格情報を解決（direct / profile / 親から継承）。</summary>
    private (string user, string domain, string password) ResolveCredentials(TreeNodeViewModel node)
    {
        var cur = node;
        while (cur != null)
        {
            switch (cur.CredentialMode)
            {
                case "direct":
                    return (cur.Username, cur.Domain, cur.Password);
                case "profile":
                    var p = CredentialProfiles.FirstOrDefault(x => x.Name == cur.CredentialProfile);
                    if (p != null) return (p.Username, p.Domain, p.Password);
                    return ("", "", "");
                default: // inheritFromParent
                    cur = cur.Parent;
                    break;
            }
        }
        return ("", "", "");
    }

    // ── ツリー編集 ──
    public void AddChild(TreeNodeViewModel? parent, TreeNodeViewModel node)
    {
        if (parent is null) RootNodes.Add(node);
        else { parent.Add(node); parent.IsExpanded = true; }
        Save();
    }

    public void Remove(TreeNodeViewModel node)
    {
        if (node.Parent is { } p) p.Children.Remove(node);
        else RootNodes.Remove(node);
        Save();
    }

    public void NotifyEdited() => Save();

    // ── 検索フィルタ ──
    private void ApplyFilter()
    {
        foreach (var root in RootNodes) FilterNode(root, SearchText.Trim());
    }

    private static bool FilterNode(TreeNodeViewModel node, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            node.IsVisible = true;
            foreach (var c in node.Children) FilterNode(c, query);
            return true;
        }
        bool selfMatch = node.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || node.Host.Contains(query, StringComparison.OrdinalIgnoreCase);
        bool childMatch = false;
        foreach (var c in node.Children) childMatch |= FilterNode(c, query);
        node.IsVisible = selfMatch || childMatch;
        if (childMatch) node.IsExpanded = true;
        return node.IsVisible;
    }

    // ── 永続化 ──
    public void Save()
    {
        var doc = new StoreDocument
        {
            Root = new NodeDto { Kind = "folder", Name = "Root", Children = RootNodes.Select(ToDto).ToList() },
            CredentialProfiles = CredentialProfiles.Select(p => new CredentialProfileDto
            {
                Name = p.Name,
                Domain = p.Domain,
                Username = p.Username,
                PasswordEncrypted = CredentialProtector.Protect(p.Password)
            }).ToList()
        };
        try { ConnectionStore.Save(doc); } catch { /* 保存失敗は致命的でない */ }
        OnPropertyChanged(nameof(StatusText));
    }

    private void Load()
    {
        var doc = ConnectionStore.Load();
        if (doc is null) { SeedDefaults(); Save(); return; }

        foreach (var p in doc.CredentialProfiles)
            CredentialProfiles.Add(new CredentialProfile
            {
                Name = p.Name, Domain = p.Domain, Username = p.Username,
                Password = CredentialProtector.Unprotect(p.PasswordEncrypted)
            });

        foreach (var child in doc.Root.Children)
            RootNodes.Add(FromDto(child, null));
    }

    private static NodeDto ToDto(TreeNodeViewModel n) => new()
    {
        Kind = n.IsFolder ? "folder" : "connection",
        Name = n.Name, Host = n.Host, Port = n.Port, Comment = n.Comment,
        CredentialMode = n.CredentialMode, CredentialProfile = n.CredentialProfile,
        Username = n.Username, Domain = n.Domain,
        PasswordEncrypted = CredentialProtector.Protect(n.Password),
        SmartSizing = n.SmartSizing, RedirectClipboard = n.RedirectClipboard,
        RedirectDrives = n.RedirectDrives, Fullscreen = n.Fullscreen,
        ScreenSize = n.ScreenSize, Gateway = n.Gateway,
        Children = n.Children.Select(ToDto).ToList()
    };

    private static TreeNodeViewModel FromDto(NodeDto d, TreeNodeViewModel? parent)
    {
        var n = new TreeNodeViewModel
        {
            Kind = d.Kind == "connection" ? NodeKind.Connection : NodeKind.Folder,
            Name = d.Name, Host = d.Host, Port = d.Port, Comment = d.Comment,
            CredentialMode = d.CredentialMode, CredentialProfile = d.CredentialProfile,
            Username = d.Username, Domain = d.Domain,
            Password = CredentialProtector.Unprotect(d.PasswordEncrypted),
            SmartSizing = d.SmartSizing, RedirectClipboard = d.RedirectClipboard,
            RedirectDrives = d.RedirectDrives, Fullscreen = d.Fullscreen,
            ScreenSize = d.ScreenSize, Gateway = d.Gateway,
            Parent = parent
        };
        foreach (var c in d.Children) n.Children.Add(FromDto(c, n));
        return n;
    }

    private void SeedDefaults()
    {
        var sample = new TreeNodeViewModel { Kind = NodeKind.Folder, Name = "サンプル" };
        sample.Add(new TreeNodeViewModel
        {
            Kind = NodeKind.Connection, Name = "localhost", Host = "127.0.0.1",
            Comment = "編集して使ってください", CredentialMode = "direct"
        });
        RootNodes.Add(sample);
    }
}
