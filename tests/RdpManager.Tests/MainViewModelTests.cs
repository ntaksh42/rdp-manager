using RdpManager.Models;
using RdpManager.ViewModels;
using Xunit;

namespace RdpManager.Tests;

public class MainViewModelTests
{
    // loadAndPersist: false で実ストア（%APPDATA%\rdpmanager）を読み書きせずにテストする
    private static MainViewModel NewVm() => new(loadAndPersist: false);

    private static TreeNodeViewModel Conn(string name, string host = "host1") =>
        new() { Kind = NodeKind.Connection, Name = name, Host = host };

    private static TreeNodeViewModel Folder(string name) =>
        new() { Kind = NodeKind.Folder, Name = name };

    // ── BuildLaunchInfo / ResolveCredentials ──

    [Fact]
    public void BuildLaunchInfo_DirectCredentials_AreUsed()
    {
        var vm = NewVm();
        var node = Conn("c");
        node.CredentialMode = "direct";
        node.Username = "user1";
        node.Domain = "dom";
        node.Password = "pw";
        vm.RootNodes.Add(node);

        var info = vm.BuildLaunchInfo(node);

        Assert.NotNull(info);
        Assert.Equal("user1", info!.Username);
        Assert.Equal("dom", info.Domain);
        Assert.Equal("pw", info.Password);
    }

    [Fact]
    public void BuildLaunchInfo_InheritFromParent_WalksUpToAncestorDirectCredentials()
    {
        var vm = NewVm();
        var root = Folder("root");
        root.CredentialMode = "direct";
        root.Username = "rootUser";
        root.Password = "rootPw";
        var mid = Folder("mid"); // 既定 inheritFromParent のまま
        var node = Conn("c");
        root.Add(mid);
        mid.Add(node);
        vm.RootNodes.Add(root);

        var info = vm.BuildLaunchInfo(node);

        Assert.Equal("rootUser", info!.Username);
        Assert.Equal("rootPw", info.Password);
    }

    [Fact]
    public void BuildLaunchInfo_ProfileCredentials_AreResolvedByName()
    {
        var vm = NewVm();
        vm.CredentialProfiles.Add(new CredentialProfile
        {
            Name = "p1", Username = "pu", Domain = "pd", Password = "ppw"
        });
        var node = Conn("c");
        node.CredentialMode = "profile";
        node.CredentialProfile = "p1";
        vm.RootNodes.Add(node);

        var info = vm.BuildLaunchInfo(node);

        Assert.Equal("pu", info!.Username);
        Assert.Equal("pd", info.Domain);
        Assert.Equal("ppw", info.Password);
    }

    [Fact]
    public void BuildLaunchInfo_MissingProfile_ReportsErrorAndContinuesWithoutCredentials()
    {
        var vm = NewVm();
        string? error = null;
        vm.Error += m => error = m;
        var node = Conn("c");
        node.CredentialMode = "profile";
        node.CredentialProfile = "nope";
        vm.RootNodes.Add(node);

        var info = vm.BuildLaunchInfo(node);

        Assert.NotNull(info);
        Assert.Equal("", info!.Username);
        Assert.NotNull(error);
    }

    [Fact]
    public void BuildLaunchInfo_EmptyHost_ReturnsNullWithError()
    {
        var vm = NewVm();
        string? error = null;
        vm.Error += m => error = m;
        var node = Conn("c", host: "");
        vm.RootNodes.Add(node);

        Assert.Null(vm.BuildLaunchInfo(node));
        Assert.NotNull(error);
    }

    [Fact]
    public void BuildLaunchInfo_InheritSettings_UsesAncestorFolderSettings()
    {
        var vm = NewVm();
        var root = Folder("root");
        root.Gateway = "gw.example.com";
        root.RedirectDrives = true;
        var node = Conn("c");
        node.InheritSettings = true;
        node.Gateway = "ignored";
        root.Add(node);
        vm.RootNodes.Add(root);

        var info = vm.BuildLaunchInfo(node);

        Assert.Equal("gw.example.com", info!.Gateway);
        Assert.True(info.RedirectDrives);
    }

    // ── MoveNode ──

    [Fact]
    public void MoveNode_ReorderWithinSameFolder_AdjustsIndexAfterRemoval()
    {
        var vm = NewVm();
        var f = Folder("f");
        var a = Conn("a");
        var b = Conn("b");
        var c = Conn("c");
        f.Add(a); f.Add(b); f.Add(c);
        vm.RootNodes.Add(f);

        vm.MoveNode(a, f, index: 2); // 削除前の位置指定 → B, A, C になる

        Assert.Equal(new[] { "b", "a", "c" }, f.Children.Select(n => n.Name));
    }

    [Fact]
    public void MoveNode_IntoOwnDescendant_IsRejected()
    {
        var vm = NewVm();
        var f = Folder("f");
        var sub = Folder("sub");
        f.Add(sub);
        vm.RootNodes.Add(f);

        vm.MoveNode(f, sub);

        Assert.Contains(f, vm.RootNodes);
        Assert.DoesNotContain(f, sub.Children);
    }

    [Fact]
    public void MoveNode_ToRoot_ClearsParent()
    {
        var vm = NewVm();
        var f = Folder("f");
        var a = Conn("a");
        f.Add(a);
        vm.RootNodes.Add(f);

        vm.MoveNode(a, null);

        Assert.Null(a.Parent);
        Assert.Contains(a, vm.RootNodes);
        Assert.DoesNotContain(a, f.Children);
    }

    // ── Duplicate（ToDto → FromDto の往復も兼ねて検証）──

    [Fact]
    public void Duplicate_CreatesDeepCopyWithNewIdsAfterOriginal()
    {
        var vm = NewVm();
        var f = Folder("f");
        var child = Conn("child", host: "h1");
        child.Port = 4000;
        child.CredentialMode = "direct";
        child.Username = "u";
        child.Password = "pw";
        child.Comment = "note";
        child.Gateway = "gw";
        f.Add(child);
        vm.RootNodes.Add(f);

        vm.Duplicate(f);

        Assert.Equal(2, vm.RootNodes.Count);
        var clone = vm.RootNodes[1];
        Assert.Equal("f (copy)", clone.Name);
        Assert.NotEqual(f.Id, clone.Id);
        var cloneChild = Assert.Single(clone.Children);
        Assert.NotEqual(child.Id, cloneChild.Id);
        Assert.Equal("child", cloneChild.Name);
        Assert.Equal("h1", cloneChild.Host);
        Assert.Equal(4000, cloneChild.Port);
        Assert.Equal("direct", cloneChild.CredentialMode);
        Assert.Equal("u", cloneChild.Username);
        Assert.Equal("pw", cloneChild.Password);
        Assert.Equal("note", cloneChild.Comment);
        Assert.Equal("gw", cloneChild.Gateway);
        Assert.Same(clone, cloneChild.Parent);
    }

    // ── 検索フィルタ ──

    [Fact]
    public void SearchText_FolderNameMatch_ShowsAllDescendants()
    {
        var vm = NewVm();
        var prod = Folder("prod");
        var web = Conn("web1");
        prod.Add(web);
        var other = Conn("other");
        vm.RootNodes.Add(prod);
        vm.RootNodes.Add(other);

        vm.SearchText = "prod";

        Assert.True(prod.IsVisible);
        Assert.True(web.IsVisible);   // フォルダ名ヒットで配下も可視化（#77）
        Assert.False(other.IsVisible);
    }

    [Fact]
    public void SearchText_Cleared_ShowsEverything()
    {
        var vm = NewVm();
        var prod = Folder("prod");
        var web = Conn("web1");
        prod.Add(web);
        vm.RootNodes.Add(prod);

        vm.SearchText = "zzz";
        Assert.False(web.IsVisible);

        vm.SearchText = "";
        Assert.True(prod.IsVisible);
        Assert.True(web.IsVisible);
    }
}
