using RdpManager.ViewModels;
using Xunit;

namespace RdpManager.Tests;

public class TreeNodeViewModelTests
{
    [Fact]
    public void Password_ChangingValue_ClearsCachedPasswordEnc()
    {
        var node = new TreeNodeViewModel { Kind = NodeKind.Connection, Password = "initial" };
        node.CachedPasswordEnc = "encrypted-blob";

        node.Password = "changed";

        Assert.Null(node.CachedPasswordEnc);
    }

    [Fact]
    public void Password_ReassigningSameValue_KeepsCachedPasswordEnc()
    {
        var node = new TreeNodeViewModel { Kind = NodeKind.Connection, Password = "initial" };
        node.CachedPasswordEnc = "encrypted-blob";

        node.Password = "initial";

        Assert.Equal("encrypted-blob", node.CachedPasswordEnc);
    }

    [Fact]
    public void Add_SetsChildParent()
    {
        var parent = new TreeNodeViewModel { Kind = NodeKind.Folder, Name = "Parent" };
        var child = new TreeNodeViewModel { Kind = NodeKind.Connection, Name = "Child" };

        parent.Add(child);

        Assert.Same(parent, child.Parent);
        Assert.Contains(child, parent.Children);
    }

    [Fact]
    public void HostDisplay_ConnectionNode_FormatsHostAndPort()
    {
        var node = new TreeNodeViewModel { Kind = NodeKind.Connection, Host = "example.com", Port = 3390 };

        Assert.Equal("example.com:3390", node.HostDisplay);
    }

    [Fact]
    public void HostDisplay_FolderNode_IsEmpty()
    {
        var node = new TreeNodeViewModel { Kind = NodeKind.Folder, Host = "example.com", Port = 3390 };

        Assert.Equal("", node.HostDisplay);
    }
}
