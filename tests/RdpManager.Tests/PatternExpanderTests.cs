using RdpManager.Common;
using Xunit;

namespace RdpManager.Tests;

public class PatternExpanderTests
{
    [Fact]
    public void Expand_LiteralOnly_ReturnsSingleUnchangedResult()
    {
        Assert.Equal(new[] { "abc" }, PatternExpander.Expand("abc"));
    }

    [Fact]
    public void Expand_NumericRange_ZeroPadsToLowerBoundWidth()
    {
        Assert.Equal(new[] { "server01", "server02", "server03" }, PatternExpander.Expand("server[01-03]"));
    }

    [Fact]
    public void Expand_ReversedRange_IsCorrectedToAscending()
    {
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, PatternExpander.Expand("[5-1]"));
    }

    [Fact]
    public void Expand_BraceListAndRange_ProducesCartesianProductInOrder()
    {
        Assert.Equal(
            new[] { "dcarack1", "dcarack2", "dcbrack1", "dcbrack2" },
            PatternExpander.Expand("{dca,dcb}rack[1-2]"));
    }

    [Fact]
    public void Expand_TrailingLiteral_IsAppendedToEachResult()
    {
        Assert.Equal(new[] { "1x", "2x" }, PatternExpander.Expand("[1-2]x"));
    }

    [Fact]
    public void Expand_EmptyBraces_DoesNotDropSurroundingLiterals()
    {
        // 回帰テスト: "{}" の展開結果が空でも直前・直後のリテラル("abc"/"def")を失わない
        Assert.Equal(new[] { "abcdef" }, PatternExpander.Expand("abc{}def"));
    }

    [Fact]
    public void Count_MatchesExpandResultCount()
    {
        var expanded = PatternExpander.Expand("{dca,dcb}rack[1-2]");
        Assert.Equal(expanded.Count, PatternExpander.Count("{dca,dcb}rack[1-2]"));
    }

    [Fact]
    public void Expand_OverLimit_ThrowsPatternTooLargeException()
    {
        var ex = Assert.Throws<PatternTooLargeException>(() => PatternExpander.Expand("[1-9999]"));
        Assert.Equal(9999, ex.Count);
    }

    [Fact]
    public void Count_OverLimit_ReturnsCountWithoutThrowing()
    {
        Assert.Equal(9999, PatternExpander.Count("[1-9999]"));
    }
}
