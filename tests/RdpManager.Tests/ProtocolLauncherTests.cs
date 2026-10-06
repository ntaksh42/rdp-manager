using RdpManager.Services;
using Xunit;

namespace RdpManager.Tests;

public class ProtocolLauncherTests
{
    [Theory]
    [InlineData("RDP", 3389, 3389)]
    [InlineData("SSH", 3389, 22)]
    [InlineData("Telnet", 3389, 23)]
    [InlineData("VNC", 3389, 5900)]
    [InlineData("SSH", 2222, 2222)]
    [InlineData("VNC", 5901, 5901)]
    public void EffectivePort_MapsDefaultRdpPortPerProtocol(string protocol, int port, int expected)
        => Assert.Equal(expected, ProtocolLauncher.EffectivePort(protocol, port));
}
