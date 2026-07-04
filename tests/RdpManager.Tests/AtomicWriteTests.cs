using System.IO;
using RdpManager.Services;
using Xunit;

namespace RdpManager.Tests;

/// <summary>各テストで固有の一時ディレクトリを使い、実行後に削除する。</summary>
public class AtomicWriteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public AtomicWriteTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void WriteAllText_NewPath_CreatesFileWithContents()
    {
        var path = Path.Combine(_dir, "new.txt");

        AtomicWrite.WriteAllText(path, "hello world");

        Assert.True(File.Exists(path));
        Assert.Equal("hello world", File.ReadAllText(path));
    }

    [Fact]
    public void WriteAllText_ExistingFile_ReplacesContentsAndKeepsPreviousInPrevFile()
    {
        var path = Path.Combine(_dir, "existing.txt");
        File.WriteAllText(path, "old content");

        AtomicWrite.WriteAllText(path, "new content");

        Assert.Equal("new content", File.ReadAllText(path));
        var prevPath = path + ".prev";
        Assert.True(File.Exists(prevPath));
        Assert.Equal("old content", File.ReadAllText(prevPath));
    }
}
