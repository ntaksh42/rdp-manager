using RdpManager.Common;
using Xunit;

namespace RdpManager.Tests;

public class ReconnectPolicyTests
{
    [Fact]
    public void NextDelay_UsesOneTwoFiveSecondBackoff()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), ReconnectPolicy.NextDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(2), ReconnectPolicy.NextDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(5), ReconnectPolicy.NextDelay(2));
        Assert.Null(ReconnectPolicy.NextDelay(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(264)]
    [InlineData(516)]
    [InlineData(772)]
    [InlineData(1028)]
    [InlineData(2308)]
    public void IsTransientDisconnect_AcceptsNetworkFailures(int disconnectReason)
        => Assert.True(ReconnectPolicy.IsTransientDisconnect(disconnectReason, 0));

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(256)]
    public void IsTransientDisconnect_RejectsExtendedReasons(int extendedReason)
        => Assert.False(ReconnectPolicy.IsTransientDisconnect(2308, extendedReason));

    [Fact]
    public void IsTransientDisconnect_RejectsAuthenticationFailure()
        => Assert.False(ReconnectPolicy.IsTransientDisconnect(2055, 0));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(11)]
    [InlineData(12)]
    public void IsRemoteSessionEnded_AcceptsDeliberateRemoteDisconnects(int extendedReason)
        => Assert.True(ReconnectPolicy.IsRemoteSessionEnded(extendedReason));

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(2308)]
    public void IsRemoteSessionEnded_RejectsOtherDisconnects(int extendedReason)
        => Assert.False(ReconnectPolicy.IsRemoteSessionEnded(extendedReason));
}
