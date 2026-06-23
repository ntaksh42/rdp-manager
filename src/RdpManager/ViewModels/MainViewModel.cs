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
    public ObservableCollection<TreeNodeViewModel> QuickAccess { get; } = new();

    /// <summary>接続起動などの失敗をビューへ通知する。</summary>
    public event Action<string>? Error;

    public MainViewModel()
    {
        Load();
        RefreshQuickAccess();
    }

    private IEnumerable<TreeNodeViewModel> AllConnections(IEnumerable<TreeNodeViewModel>? nodes = null)
    {
        foreach (var n in nodes ?? RootNodes)
        {
            if (n.IsConnection) yield return n;
            foreach (var c in AllConnections(n.Children)) yield return c;
        }
    }

    /// <summary>お気に入り + 最近使った接続をクイックアクセス一覧に反映。</summary>
    public void RefreshQuickAccess()
    {
        QuickAccess.Clear();
        var all = AllConnections().ToList();
        foreach (var fav in all.Where(c => c.IsFavorite))
            QuickAccess.Add(fav);
        var recentIds = App.Settings.RecentIds;
        foreach (var id in recentIds)
        {
            var node = all.FirstOrDefault(c => c.Id.ToString() == id);
            if (node != null && !node.IsFavorite && !QuickAccess.Contains(node))
                QuickAccess.Add(node);
        }
    }

    public TreeNodeViewModel? FindConnectionById(string id)
        => AllConnections().FirstOrDefault(c => c.Id.ToString() == id);

    public IReadOnlyList<TreeNodeViewModel> GetAllConnections() => AllConnections().ToList();

    /// <summary>全接続の死活状態(TCP到達性)を非同期チェックして更新する。</summary>
    public async Task RefreshStatusesAsync()
    {
        var conns = AllConnections().Where(c => !string.IsNullOrWhiteSpace(c.Host)).ToList();
        await Task.WhenAll(conns.Select(CheckStatusAsync));
    }

    private static async Task CheckStatusAsync(TreeNodeViewModel node)
    {
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            var connect = tcp.ConnectAsync(node.Host, node.Port);
            var done = await Task.WhenAny(connect, Task.Delay(1500));
            node.Status = done == connect && tcp.Connected ? NodeStatus.Up : NodeStatus.Down;
        }
        catch
        {
            node.Status = NodeStatus.Down;
        }
    }

    public void AddImported(IEnumerable<TreeNodeViewModel> nodes, TreeNodeViewModel? parent)
    {
        foreach (var node in nodes)
        {
            if (parent is null) { node.Parent = null; RootNodes.Add(node); }
            else { node.Parent = parent; parent.Children.Add(node); }
        }
        if (parent != null) parent.IsExpanded = true;
        Save();
        RefreshQuickAccess();
    }

    public void ToggleFavorite(TreeNodeViewModel? node)
    {
        if (node is null || !node.IsConnection) return;
        node.IsFavorite = !node.IsFavorite;
        Save();
        RefreshQuickAccess();
    }

    public void RecordRecent(TreeNodeViewModel node)
    {
        if (!node.IsConnection) return;
        var id = node.Id.ToString();
        App.Settings.RecentIds.Remove(id);
        App.Settings.RecentIds.Insert(0, id);
        if (App.Settings.RecentIds.Count > 10)
            App.Settings.RecentIds.RemoveRange(10, App.Settings.RecentIds.Count - 10);
        App.Settings.Save();
        RefreshQuickAccess();
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
        : $"{CountConnections(RootNodes)} connection(s)  |  Store: {ConnectionStore.FilePath}";

    private static int CountConnections(IEnumerable<TreeNodeViewModel> nodes)
        => nodes.Sum(n => (n.IsConnection ? 1 : 0) + CountConnections(n.Children));

    /// <summary>接続ノードから LaunchInfo を生成（資格情報を解決）。無効なら null。</summary>
    public LaunchInfo? BuildLaunchInfo(TreeNodeViewModel? node)
    {
        if (node is null || !node.IsConnection) return null;
        if (string.IsNullOrWhiteSpace(node.Host))
        {
            Error?.Invoke("Host / IP is not set.");
            return null;
        }
        var (user, domain, password) = ResolveCredentials(node);
        var s = ResolveSettingsSource(node);
        return new LaunchInfo
        {
            Host = node.Host,
            Port = node.Port,
            Username = user,
            Domain = domain,
            Password = password,
            Fullscreen = s.Fullscreen,
            SmartSizing = s.SmartSizing,
            RedirectClipboard = s.RedirectClipboard,
            RedirectDrives = s.RedirectDrives,
            Gateway = s.Gateway,
            PerformanceMode = App.Settings.PerformanceMode,
            AuthenticationLevel = s.AuthenticationLevel,
            UseMultimon = App.Settings.UseMultimon
        };
    }

    /// <summary>表示/RDP 設定の解決元。継承指定なら親フォルダを辿る。</summary>
    private static TreeNodeViewModel ResolveSettingsSource(TreeNodeViewModel node)
    {
        var c = node;
        while (c.InheritSettings && c.Parent != null) c = c.Parent;
        return c;
    }

    /// <summary>外部 mstsc.exe で開く（埋め込みのフォールバック）。</summary>
    public void ConnectExternal(TreeNodeViewModel? node)
    {
        var info = BuildLaunchInfo(node);
        if (info is null) return;
        try { RdpLauncher.Launch(info); }
        catch (Exception ex) { Error?.Invoke($"Failed to launch the connection.\n{ex.Message}"); }
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
                case "winCred":
                    var w = CredentialManager.ReadTerminalServer(node.Host);
                    if (w is { } cred)
                    {
                        var bs = cred.user.IndexOf('\\');
                        return bs > 0
                            ? (cred.user[(bs + 1)..], cred.user[..bs], cred.password)
                            : (cred.user, "", cred.password);
                    }
                    Logger.Warn($"Windows credential 'TERMSRV/{node.Host}' not found.");
                    Error?.Invoke($"No saved Windows credential was found for \"{node.Host}\".\n" +
                                  "The connection will continue without credentials.");
                    return ("", "", "");
                case "profile":
                    var p = CredentialProfiles.FirstOrDefault(x => x.Name == cur.CredentialProfile);
                    if (p != null) return (p.Username, p.Domain, p.Password);
                    Logger.Warn($"Credential profile '{cur.CredentialProfile}' not found.");
                    Error?.Invoke($"Credential profile \"{cur.CredentialProfile}\" was not found.\n" +
                                  "The connection will continue without credentials.");
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

        // 削除したノード（子孫含む）の ID が Recent/OpenOnExit に孤児として残らないよう除去
        var removedIds = SelfAndDescendants(node).Select(n => n.Id.ToString()).ToHashSet();
        int before = App.Settings.RecentIds.Count + App.Settings.OpenOnExit.Count;
        App.Settings.RecentIds.RemoveAll(removedIds.Contains);
        App.Settings.OpenOnExit.RemoveAll(removedIds.Contains);
        if (before != App.Settings.RecentIds.Count + App.Settings.OpenOnExit.Count)
            App.Settings.Save();

        Save();
        RefreshQuickAccess();
    }

    private static IEnumerable<TreeNodeViewModel> SelfAndDescendants(TreeNodeViewModel node)
    {
        yield return node;
        foreach (var c in node.Children)
            foreach (var d in SelfAndDescendants(c)) yield return d;
    }

    public void NotifyEdited() => Save();

    /// <summary>ノードを別フォルダ（null=ルート）へ移動。index>=0 ならその位置へ挿入（同一フォルダ内の並べ替え）。循環は禁止。</summary>
    public void MoveNode(TreeNodeViewModel node, TreeNodeViewModel? newParent, int index = -1)
    {
        if (node == newParent) return;
        if (newParent != null && IsSelfOrDescendant(node, newParent)) return;
        if (node.Parent == newParent && index < 0) return;

        var target = newParent?.Children ?? RootNodes;
        var source = node.Parent?.Children ?? RootNodes;

        int oldIndex = source.IndexOf(node);
        source.Remove(node);
        // 同一コレクション内で削除した位置より後ろへ挿入する場合は 1 つ詰める
        if (ReferenceEquals(source, target) && index > oldIndex) index--;

        node.Parent = newParent;
        if (index < 0 || index > target.Count) target.Add(node);
        else target.Insert(index, node);
        if (newParent != null) newParent.IsExpanded = true;
        Save();
    }

    /// <summary>ノード（フォルダなら子孫ごと）を複製し、元の直後に挿入する。</summary>
    public void Duplicate(TreeNodeViewModel node)
    {
        var clone = FromDto(ToDto(node), node.Parent); // ToDto→FromDto で新しい Id を採番しつつ深いコピー
        clone.Name = node.Name + " (copy)";
        var siblings = node.Parent?.Children ?? RootNodes;
        int idx = siblings.IndexOf(node);
        siblings.Insert(idx < 0 ? siblings.Count : idx + 1, clone);
        Save();
        RefreshQuickAccess();
    }

    private static bool IsSelfOrDescendant(TreeNodeViewModel node, TreeNodeViewModel candidate)
    {
        var c = candidate;
        while (c != null) { if (c == node) return true; c = c.Parent; }
        return false;
    }

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
        Protocol = n.Protocol,
        Name = n.Name, Host = n.Host, Port = n.Port, Comment = n.Comment,
        CredentialMode = n.CredentialMode, CredentialProfile = n.CredentialProfile,
        Username = n.Username, Domain = n.Domain,
        PasswordEncrypted = n.CachedPasswordEnc ??= CredentialProtector.Protect(n.Password),
        InheritSettings = n.InheritSettings,
        SmartSizing = n.SmartSizing, RedirectClipboard = n.RedirectClipboard,
        RedirectDrives = n.RedirectDrives, Fullscreen = n.Fullscreen,
        AuthenticationLevel = n.AuthenticationLevel,
        Gateway = n.Gateway, IsFavorite = n.IsFavorite,
        PreCommand = n.PreCommand, PostCommand = n.PostCommand,
        Children = n.Children.Select(ToDto).ToList()
    };

    private static TreeNodeViewModel FromDto(NodeDto d, TreeNodeViewModel? parent)
    {
        var n = new TreeNodeViewModel
        {
            Kind = d.Kind == "connection" ? NodeKind.Connection : NodeKind.Folder,
            Protocol = string.IsNullOrEmpty(d.Protocol) ? "RDP" : d.Protocol,
            Name = d.Name, Host = d.Host, Port = d.Port, Comment = d.Comment,
            CredentialMode = d.CredentialMode, CredentialProfile = d.CredentialProfile,
            Username = d.Username, Domain = d.Domain,
            Password = CredentialProtector.Unprotect(d.PasswordEncrypted),
            InheritSettings = d.InheritSettings,
            SmartSizing = d.SmartSizing, RedirectClipboard = d.RedirectClipboard,
            RedirectDrives = d.RedirectDrives, Fullscreen = d.Fullscreen,
            AuthenticationLevel = d.AuthenticationLevel,
            Gateway = d.Gateway, IsFavorite = d.IsFavorite,
            PreCommand = d.PreCommand, PostCommand = d.PostCommand,
            Parent = parent
        };
        // 読み込んだ暗号化値をそのままキャッシュ → 平文未変更なら保存時に再暗号化しない
        n.CachedPasswordEnc = d.PasswordEncrypted;
        foreach (var c in d.Children) n.Children.Add(FromDto(c, n));
        return n;
    }

    private void SeedDefaults()
    {
        var sample = new TreeNodeViewModel { Kind = NodeKind.Folder, Name = "Sample" };
        sample.Add(new TreeNodeViewModel
        {
            Kind = NodeKind.Connection, Name = "localhost", Host = "127.0.0.1",
            Comment = "Edit me to get started", CredentialMode = "direct"
        });
        RootNodes.Add(sample);
    }
}
