using RdpManager.Common;
using Xunit;

namespace RdpManager.Tests;

public class HostAddressTests
{
    [Fact]
    public void Format_DefaultPort_OmitsPort()
    {
        Assert.Equal("example.com", HostAddress.Format("example.com", 3389));
    }

    [Fact]
    public void Format_NonDefaultPort_AppendsPort()
    {
        Assert.Equal("example.com:3390", HostAddress.Format("example.com", 3390));
    }

    [Fact]
    public void Format_IPv6_DefaultPort_WrapsInBrackets()
    {
        Assert.Equal("[fe80::1]", HostAddress.Format("fe80::1", 3389));
    }

    [Fact]
    public void Format_IPv6_NonDefaultPort_WrapsInBracketsAndAppendsPort()
    {
        Assert.Equal("[fe80::1]:3390", HostAddress.Format("fe80::1", 3390));
    }

    [Fact]
    public void Format_CustomDefaultPort_OmitsPortWhenMatches()
    {
        Assert.Equal("example.com", HostAddress.Format("example.com", 22, defaultPort: 22));
    }

    [Fact]
    public void Format_CustomDefaultPort_AppendsWhenDifferent()
    {
        Assert.Equal("example.com:3389", HostAddress.Format("example.com", 3389, defaultPort: 22));
    }

    [Fact]
    public void FormatWithPort_AlwaysAppendsPort_EvenIfDefault()
    {
        Assert.Equal("example.com:3389", HostAddress.FormatWithPort("example.com", 3389));
    }

    [Fact]
    public void FormatWithPort_IPv6_WrapsInBrackets()
    {
        Assert.Equal("[fe80::1]:3389", HostAddress.FormatWithPort("fe80::1", 3389));
    }

    [Fact]
    public void Parse_HostOnly_ReturnsNullPort()
    {
        var (host, port) = HostAddress.Parse("host");
        Assert.Equal("host", host);
        Assert.Null(port);
    }

    [Fact]
    public void Parse_HostWithPort_ReturnsBoth()
    {
        var (host, port) = HostAddress.Parse("host:3390");
        Assert.Equal("host", host);
        Assert.Equal(3390, port);
    }

    [Fact]
    public void Parse_BracketedIPv6WithPort_ReturnsHostAndPort()
    {
        var (host, port) = HostAddress.Parse("[::1]:3390");
        Assert.Equal("::1", host);
        Assert.Equal(3390, port);
    }

    [Fact]
    public void Parse_BracketedIPv6WithoutPort_ReturnsHostOnly()
    {
        var (host, port) = HostAddress.Parse("[fe80::1]");
        Assert.Equal("fe80::1", host);
        Assert.Null(port);
    }

    [Fact]
    public void Parse_BareIPv6_ReturnsHostOnly()
    {
        var (host, port) = HostAddress.Parse("fe80::1");
        Assert.Equal("fe80::1", host);
        Assert.Null(port);
    }

    [Fact]
    public void Parse_HostWithNonNumericPort_ReturnsHostAndNullPort()
    {
        var (host, port) = HostAddress.Parse("host:abc");
        Assert.Equal("host", host);
        Assert.Null(port);
    }

    [Fact]
    public void Parse_TrimsWhitespace()
    {
        var (host, port) = HostAddress.Parse("  host:3390  ");
        Assert.Equal("host", host);
        Assert.Equal(3390, port);
    }

    [Theory]
    [InlineData("fe80::1", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("example.com", false)]
    public void IsIPv6_ClassifiesCorrectly(string host, bool expected)
    {
        Assert.Equal(expected, HostAddress.IsIPv6(host));
    }
}
