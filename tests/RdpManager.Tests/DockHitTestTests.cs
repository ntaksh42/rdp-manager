using System.Windows;
using Point = System.Windows.Point;
using RdpManager.Common;
using Xunit;

namespace RdpManager.Tests;

public class DockHitTestTests
{
    private static readonly Rect Pane = new(100, 50, 400, 200);

    [Theory]
    [InlineData(300, 150, DockZone.Center)]
    [InlineData(110, 150, DockZone.Left)]
    [InlineData(490, 150, DockZone.Right)]
    [InlineData(300, 55, DockZone.Top)]
    [InlineData(300, 245, DockZone.Bottom)]
    public void ZoneAt_PicksNearestEdgeOrCenter(double x, double y, DockZone expected)
        => Assert.Equal(expected, DockHitTest.ZoneAt(Pane, new Point(x, y)));

    [Fact]
    public void ZoneAt_ComparesEdgesRelativeToPaneSize()
    {
        // 横長ペインでも、左端から10%・上端から20%の点は左ゾーン（割合で比べる）
        Assert.Equal(DockZone.Left, DockHitTest.ZoneAt(Pane, new Point(140, 90)));
    }

    [Fact]
    public void ZoneAt_OutsideOrEmptyPane_IsNull()
    {
        Assert.Null(DockHitTest.ZoneAt(Pane, new Point(50, 150)));
        Assert.Null(DockHitTest.ZoneAt(new Rect(0, 0, 0, 100), new Point(0, 50)));
    }

    [Fact]
    public void PreviewRect_CoversHalfOnTheDropSide()
    {
        Assert.Equal(new Rect(100, 50, 200, 200), DockHitTest.PreviewRect(Pane, DockZone.Left));
        Assert.Equal(new Rect(300, 50, 200, 200), DockHitTest.PreviewRect(Pane, DockZone.Right));
        Assert.Equal(new Rect(100, 50, 400, 100), DockHitTest.PreviewRect(Pane, DockZone.Top));
        Assert.Equal(new Rect(100, 150, 400, 100), DockHitTest.PreviewRect(Pane, DockZone.Bottom));
        Assert.Equal(Pane, DockHitTest.PreviewRect(Pane, DockZone.Center));
    }

    [Fact]
    public void SplitOf_MapsZonesToDirectionAndSide()
    {
        Assert.Equal((SplitDirection.Horizontal, true), DockHitTest.SplitOf(DockZone.Left));
        Assert.Equal((SplitDirection.Horizontal, false), DockHitTest.SplitOf(DockZone.Right));
        Assert.Equal((SplitDirection.Vertical, true), DockHitTest.SplitOf(DockZone.Top));
        Assert.Equal((SplitDirection.Vertical, false), DockHitTest.SplitOf(DockZone.Bottom));
        Assert.Null(DockHitTest.SplitOf(DockZone.Center));
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(60, 1)]
    [InlineData(500, 3)]
    public void InsertIndex_CountsTabsLeftOfPointer(double x, int expected)
        => Assert.Equal(expected, DockHitTest.InsertIndex(new double[] { 50, 150, 250 }, x));
}
