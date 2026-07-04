using RdpManager.Services;
using Xunit;

namespace RdpManager.Tests;

public class ImportExportTests
{
    [Fact]
    public void ToCsv_FromCsv_RoundTrips_CommaQuoteNewlineAndJapanese()
    {
        var conn = new ImportedConn(
            "Name,with,commas",
            "host.example.com",
            3390,
            "DOMAIN",
            "user\"quoted\"",
            "コメント\n改行と\"引用符\"を含む");

        var csv = ImportExport.ToCsv(new[] { conn });
        var parsed = ImportExport.FromCsv(csv);

        var result = Assert.Single(parsed);
        Assert.Equal(conn, result);
    }

    [Fact]
    public void FromCsv_SkipsHeaderRow()
    {
        var text = "Name,Host,Port,Domain,Username,Comment\nfoo,bar,3389,,,\n";
        var parsed = ImportExport.FromCsv(text);

        var result = Assert.Single(parsed);
        Assert.Equal("foo", result.Name);
        Assert.Equal("bar", result.Host);
    }

    [Fact]
    public void FromCsv_EmptyHost_IsFilledFromName()
    {
        var parsed = ImportExport.FromCsv("foo,,,,,\n");

        var result = Assert.Single(parsed);
        Assert.Equal("foo", result.Name);
        Assert.Equal("foo", result.Host);
    }

    [Fact]
    public void FromCsv_EmptyName_IsFilledFromHost()
    {
        var parsed = ImportExport.FromCsv(",bar,,,,\n");

        var result = Assert.Single(parsed);
        Assert.Equal("bar", result.Name);
        Assert.Equal("bar", result.Host);
    }

    [Fact]
    public void FromCsv_MissingPort_DefaultsTo3389()
    {
        var parsed = ImportExport.FromCsv("foo,bar,,,,\n");

        var result = Assert.Single(parsed);
        Assert.Equal(3389, result.Port);
    }

    [Fact]
    public void FromCsv_BlankLine_IsIgnored()
    {
        var text = "Name,Host,Port,Domain,Username,Comment\n\nfoo,bar,3389,,,\n";
        var parsed = ImportExport.FromCsv(text);

        Assert.Single(parsed);
    }

    [Theory]
    [InlineData("=SUM(A1)")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1)")]
    public void ToCsv_EscapesFormulaInjection_AndFromCsv_RoundTrips(string dangerousName)
    {
        var conn = new ImportedConn(dangerousName, "host", 3389, "", "", "");
        var csv = ImportExport.ToCsv(new[] { conn });

        Assert.Contains("'" + dangerousName, csv);

        var parsed = ImportExport.FromCsv(csv);
        var result = Assert.Single(parsed);
        Assert.Equal(dangerousName, result.Name);
    }

    [Fact]
    public void FromRdp_FullAddressOnly_DefaultsPortTo3389()
    {
        var result = ImportExport.FromRdp("full address:s:myhost", "fallback");

        Assert.NotNull(result);
        Assert.Equal("myhost", result!.Host);
        Assert.Equal(3389, result.Port);
    }

    [Fact]
    public void FromRdp_FullAddressWithPort_UsesThatPort()
    {
        var result = ImportExport.FromRdp("full address:s:myhost:3390", "fallback");

        Assert.NotNull(result);
        Assert.Equal("myhost", result!.Host);
        Assert.Equal(3390, result.Port);
    }

    [Fact]
    public void FromRdp_ServerPortOnly_IsUsedWhenFullAddressHasNoPort()
    {
        var text = "full address:s:myhost\nserver port:i:3391\n";
        var result = ImportExport.FromRdp(text, "fallback");

        Assert.NotNull(result);
        Assert.Equal(3391, result!.Port);
    }

    [Fact]
    public void FromRdp_FullAddressPortWinsOverServerPort_FullAddressFirst()
    {
        var text = "full address:s:myhost:3390\nserver port:i:3391\n";
        var result = ImportExport.FromRdp(text, "fallback");

        Assert.NotNull(result);
        Assert.Equal(3390, result!.Port);
    }

    [Fact]
    public void FromRdp_FullAddressPortWinsOverServerPort_ServerPortFirst()
    {
        var text = "server port:i:3391\nfull address:s:myhost:3390\n";
        var result = ImportExport.FromRdp(text, "fallback");

        Assert.NotNull(result);
        Assert.Equal(3390, result!.Port);
    }

    [Fact]
    public void FromRdp_ServerPortOutOfRange_IsIgnored()
    {
        var text = "full address:s:myhost\nserver port:i:99999\n";
        var result = ImportExport.FromRdp(text, "fallback");

        Assert.NotNull(result);
        Assert.Equal(3389, result!.Port);
    }

    [Fact]
    public void FromRdp_UsernameWithDomain_SplitsDomainAndUsername()
    {
        var text = "full address:s:myhost\n" + "username:s:" + "DOM" + '\\' + "user\n";
        var result = ImportExport.FromRdp(text, "fallback");

        Assert.NotNull(result);
        Assert.Equal("DOM", result!.Domain);
        Assert.Equal("user", result.Username);
    }

    [Fact]
    public void FromRdp_UsernameWithoutDomain_SetsUsernameOnly()
    {
        var text = "full address:s:myhost\nusername:s:user\n";
        var result = ImportExport.FromRdp(text, "fallback");

        Assert.NotNull(result);
        Assert.Equal("", result!.Domain);
        Assert.Equal("user", result.Username);
    }

    [Fact]
    public void FromRdp_NoFullAddress_ReturnsNull()
    {
        var result = ImportExport.FromRdp("username:s:user\n", "fallback");

        Assert.Null(result);
    }
}
