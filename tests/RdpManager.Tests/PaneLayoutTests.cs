using System.Windows;
using RdpManager.Common;
using Xunit;

namespace RdpManager.Tests;

public class PaneLayoutTests
{
    [Fact]
    public void NewLayout_HasSinglePaneFillingBounds()
    {
        var layout = new PaneLayout();
        var (panes, splitters) = layout.Compute(new Rect(0, 0, 1000, 600), 4);
        Assert.Single(panes);
        Assert.Empty(splitters);
        Assert.Equal(new Rect(0, 0, 1000, 600), panes[layout.PaneIds[0]]);
    }

    [Fact]
    public void SplitRight_PlacesNewPaneAfterSplitter()
    {
        var layout = new PaneLayout();
        int first = layout.PaneIds[0];
        int second = layout.Split(first, SplitDirection.Horizontal);

        var (panes, splitters) = layout.Compute(new Rect(0, 0, 1004, 600), 4);
        Assert.Equal(new[] { first, second }, layout.PaneIds);
        Assert.Equal(new Rect(0, 0, 500, 600), panes[first]);
        Assert.Equal(new Rect(500, 0, 4, 600), Assert.Single(splitters).Rect);
        Assert.Equal(new Rect(504, 0, 500, 600), panes[second]);
    }

    [Fact]
    public void NestedSplit_DividesOnlyTheTargetPane()
    {
        // 左 | (右上 / 右下)
        var layout = new PaneLayout();
        int left = layout.PaneIds[0];
        int right = layout.Split(left, SplitDirection.Horizontal);
        int bottom = layout.Split(right, SplitDirection.Vertical);

        var (panes, splitters) = layout.Compute(new Rect(0, 0, 1004, 604), 4);
        Assert.Equal(new[] { left, right, bottom }, layout.PaneIds);
        Assert.Equal(2, splitters.Count);
        Assert.Equal(new Rect(0, 0, 500, 604), panes[left]);
        Assert.Equal(new Rect(504, 0, 500, 300), panes[right]);
        Assert.Equal(new Rect(504, 304, 500, 300), panes[bottom]);
    }

    [Fact]
    public void Remove_SiblingTakesOverTheSpace()
    {
        var layout = new PaneLayout();
        int left = layout.PaneIds[0];
        int right = layout.Split(left, SplitDirection.Horizontal);
        int bottom = layout.Split(right, SplitDirection.Vertical);

        Assert.True(layout.Remove(right));
        var (panes, _) = layout.Compute(new Rect(0, 0, 1004, 600), 4);
        Assert.Equal(new[] { left, bottom }, layout.PaneIds);
        Assert.Equal(new Rect(504, 0, 500, 600), panes[bottom]);
    }

    [Fact]
    public void Remove_LastPane_IsRejected()
    {
        var layout = new PaneLayout();
        Assert.False(layout.Remove(layout.PaneIds[0]));
        Assert.Equal(1, layout.PaneCount);
    }

    [Fact]
    public void NextPane_WrapsAround()
    {
        var layout = new PaneLayout();
        int a = layout.PaneIds[0];
        int b = layout.Split(a, SplitDirection.Horizontal);
        int c = layout.Split(b, SplitDirection.Vertical);

        Assert.Equal(b, layout.NextPane(a, 1));
        Assert.Equal(a, layout.NextPane(c, 1));
        Assert.Equal(c, layout.NextPane(a, -1));
    }

    [Fact]
    public void RatioAfterDrag_IsClampedToLimits()
    {
        var layout = new PaneLayout();
        layout.Split(layout.PaneIds[0], SplitDirection.Horizontal);
        var splitter = Assert.Single(layout.Compute(new Rect(0, 0, 1004, 600), 4).Splitters);

        Assert.Equal(0.6, PaneLayout.RatioAfterDrag(splitter, 100, 4), 3);
        Assert.Equal(PaneLayout.MaxRatio, PaneLayout.RatioAfterDrag(splitter, 5000, 4));
        Assert.Equal(PaneLayout.MinRatio, PaneLayout.RatioAfterDrag(splitter, -5000, 4));
    }

    [Fact]
    public void Serialize_RoundTripsStructureAndRatios()
    {
        var layout = new PaneLayout();
        int right = layout.Split(layout.PaneIds[0], SplitDirection.Horizontal);
        layout.Split(right, SplitDirection.Vertical);
        var root = (SplitNode)layout.Root;
        PaneLayout.SetRatio(root, 0.3);

        string text = layout.Serialize();
        Assert.Equal("H0.3(P,V0.5(P,P))", text);
        var parsed = PaneLayout.Parse(text);
        Assert.NotNull(parsed);
        Assert.Equal(text, parsed!.Serialize());
        Assert.Equal(3, parsed.PaneCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("X")]
    [InlineData("H0.5(P,P")]
    [InlineData("H0.5(P,P))")]
    [InlineData("Habc(P,P)")]
    public void Parse_InvalidInput_ReturnsNull(string? text)
    {
        Assert.Null(PaneLayout.Parse(text));
    }

    [Fact]
    public void Parse_ThenSplit_AssignsUniqueIds()
    {
        var layout = PaneLayout.Parse("H0.5(P,P)")!;
        int created = layout.Split(layout.PaneIds[1], SplitDirection.Vertical);
        Assert.Equal(3, layout.PaneIds.Distinct().Count());
        Assert.Contains(created, layout.PaneIds);
    }
}
