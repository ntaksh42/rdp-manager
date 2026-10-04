using RdpManager.Common;
using Xunit;

namespace RdpManager.Tests;

public class ModifierTrackerTests
{
    private const uint LShift = 0xA0, RShift = 0xA1, LCtrl = 0xA2, RCtrl = 0xA3, LAlt = 0xA4, LWin = 0x5B;

    [Fact]
    public void CtrlAlt_DownTracked()
    {
        var t = new ModifierTracker();
        Assert.True(t.Update(LCtrl, true));
        Assert.True(t.Update(LAlt, true));
        Assert.Equal(0x3u, t.Current);
    }

    [Fact]
    public void KeyUp_ClearsModifier()
    {
        var t = new ModifierTracker();
        t.Update(LCtrl, true);
        t.Update(LAlt, true);
        t.Update(LAlt, false);
        Assert.Equal(0x2u, t.Current);
    }

    [Fact]
    public void LeftAndRight_TrackedSeparately()
    {
        var t = new ModifierTracker();
        t.Update(LShift, true);
        t.Update(RShift, true);
        t.Update(LShift, false);
        Assert.Equal(0x4u, t.Current); // 右 Shift がまだ押されている
        t.Update(RShift, false);
        Assert.Equal(0u, t.Current);
    }

    [Fact]
    public void NonModifier_ReturnsFalseAndKeepsState()
    {
        var t = new ModifierTracker();
        t.Update(RCtrl, true);
        Assert.False(t.Update(0x30, true)); // '0'
        Assert.Equal(0x2u, t.Current);
    }

    [Fact]
    public void UnsidedVk_TreatedAsLeft()
    {
        var t = new ModifierTracker();
        t.Update(0x11, true); // VK_CONTROL
        Assert.Equal(0x2u, t.Current);
        t.Update(LCtrl, false);
        Assert.Equal(0u, t.Current);
    }

    [Fact]
    public void Reset_SeedsFromCurrentState()
    {
        var t = new ModifierTracker();
        t.Update(LShift, true);
        t.Reset(vk => vk is LCtrl or LWin);
        Assert.Equal(0x2u | 0x8u, t.Current);
    }
}
