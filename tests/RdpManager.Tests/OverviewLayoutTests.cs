using RdpManager.Common;
using Xunit;

namespace RdpManager.Tests;

public class OverviewLayoutTests
{
    private const double Aspect = 16.0 / 9.0;

    private static OverviewLayout.Result Layout(int count, double w = 1248, double h = 696)
        => OverviewLayout.Compute(count, w, h, Aspect, gap: 16, chromeWidth: 16, chromeHeight: 44, minScreenWidth: 200);

    [Fact]
    public void SingleSession_FillsAvailableHeight()
    {
        var r = Layout(1);
        Assert.Equal(1, r.Columns);
        Assert.Equal(1, r.Rows);
        // 横長領域では高さが制約になる: (696 - 44) * 16/9
        Assert.Equal((696 - 44) * Aspect, r.ScreenWidth, 3);
    }

    [Fact]
    public void SixSessions_OnWideArea_UseThreeByTwo()
    {
        var r = Layout(6);
        Assert.Equal(3, r.Columns);
        Assert.Equal(2, r.Rows);
    }

    [Fact]
    public void ThreeSessions_PrefersTwoByTwo_WhenTilesGetLarger()
    {
        // 3 列 1 行より 2 列 2 行の方が1枚あたり大きくなる領域
        var r = Layout(3, w: 1248, h: 760);
        Assert.Equal(2, r.Columns);
        Assert.Equal(2, r.Rows);
    }

    [Fact]
    public void NarrowTallArea_UsesSingleColumn()
    {
        // 縦長領域に 2 件なら 1 列 2 行の方が大きい
        var r = Layout(2, w: 400, h: 1000);
        Assert.Equal(1, r.Columns);
    }

    [Fact]
    public void ManySessions_FallBackToMinimumWidth_AndScroll()
    {
        var r = Layout(60, w: 1000, h: 400);
        Assert.True(r.ScreenWidth >= 200);
        Assert.True(r.Columns * r.TileWidth + (r.Columns - 1) * 16 <= 1000 + 0.001);
        Assert.Equal((60 + r.Columns - 1) / r.Columns, r.Rows);
    }

    [Fact]
    public void EmptyOrInvalidArea_ReturnsZeroWidth()
    {
        Assert.Equal(0, Layout(0).TileWidth);
        Assert.Equal(0, Layout(3, w: 0).TileWidth);
    }

    [Theory]
    [InlineData(0, "Live")]
    [InlineData(2, "Live")]
    [InlineData(12, "12s ago")]
    [InlineData(185, "3m ago")]
    [InlineData(7300, "2h ago")]
    public void FormatAge_ProducesCompactText(int seconds, string expected)
        => Assert.Equal(expected, OverviewLayout.FormatAge(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("web", true)]
    [InlineData("WEB", true)]
    [InlineData("10.0.1", true)]
    [InlineData("sql", false)]
    public void Matches_ChecksTitleAndHostCaseInsensitively(string? query, bool expected)
        => Assert.Equal(expected, OverviewLayout.Matches(query, "WEB-PROD-01", "10.0.1.21"));
}
