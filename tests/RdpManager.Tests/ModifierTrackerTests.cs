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
        Assert.True(t.Update(LCtrl, 0, true));
        Assert.True(t.Update(LAlt, 0, true));
        Assert.Equal(0x3u, t.Current);
    }

    [Fact]
    public void KeyUp_ClearsModifier()
    {
        var t = new ModifierTracker();
        t.Update(LCtrl, 0, true);
        t.Update(LAlt, 0, true);
        t.Update(LAlt, 0, false);
        Assert.Equal(0x2u, t.Current);
    }

    [Fact]
    public void LeftAndRight_TrackedSeparately()
    {
        var t = new ModifierTracker();
        t.Update(LShift, 0, true);
        t.Update(RShift, 0, true);
        t.Update(LShift, 0, false);
        Assert.Equal(0x4u, t.Current); // 右 Shift がまだ押されている
        t.Update(RShift, 0, false);
        Assert.Equal(0u, t.Current);
    }

    [Fact]
    public void NonModifier_ReturnsFalseAndKeepsState()
    {
        var t = new ModifierTracker();
        t.Update(RCtrl, 0, true);
        Assert.False(t.Update(0x30, 0, true)); // '0'
        Assert.Equal(0x2u, t.Current);
    }

    [Fact]
    public void UnsidedVk_TreatedAsLeft()
    {
        var t = new ModifierTracker();
        t.Update(0x11, 0, true); // VK_CONTROL
        Assert.Equal(0x2u, t.Current);
        t.Update(LCtrl, 0, false);
        Assert.Equal(0u, t.Current);
    }

    [Fact]
    public void Reset_SeedsFromCurrentState()
    {
        var t = new ModifierTracker();
        t.Update(LShift, 0, true);
        t.Reset(vk => vk is LCtrl or LWin);
        Assert.Equal(0x2u | 0x8u, t.Current);
    }

    [Fact]
    public void AltGrFakeCtrl_NotCountedAsCtrl()
    {
        var t = new ModifierTracker();
        Assert.True(t.Update(LCtrl, 0x21D, true)); // AltGr が合成する左 Ctrl
        t.Update(0xA5, 0x38, true);                // 右 Alt
        Assert.Equal(0x1u, t.Current);             // Alt のみ（Ctrl+Alt+数字に一致させない）
        t.Update(LCtrl, 0x21D, false);
        t.Update(0xA5, 0x38, false);
        Assert.Equal(0u, t.Current);
    }

    [Fact]
    public void RealCtrl_WithRightAlt_CountsAsCtrlAlt()
    {
        var t = new ModifierTracker();
        t.Update(LCtrl, 0x1D, true);
        t.Update(0xA5, 0x38, true);
        Assert.Equal(0x3u, t.Current);
    }
}
