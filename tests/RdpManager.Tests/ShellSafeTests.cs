using RdpManager.Common;
using Xunit;

namespace RdpManager.Tests;

public class ShellSafeTests
{
    [Theory]
    [InlineData("a&b")]
    [InlineData("a|b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a%b%")]
    [InlineData("a;b")]
    [InlineData("a\"b")]
    public void HasMeta_DetectsShellMetaCharacters(string value)
    {
        Assert.True(ShellSafe.HasMeta(value));
    }

    [Fact]
    public void HasMeta_NormalHostname_ReturnsFalse()
    {
        Assert.False(ShellSafe.HasMeta("server01.example.com"));
    }

    [Fact]
    public void HasMeta_ControlCharacter_ReturnsTrue()
    {
        Assert.True(ShellSafe.HasMeta("abc\u0001def"));
    }

    [Fact]
    public void Strip_RemovesMetaAndControlCharacters_KeepsNormalText()
    {
        Assert.Equal("abcdef", ShellSafe.Strip("a&b|cd\te%f"));
    }
}
