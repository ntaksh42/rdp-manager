using System.Text;
using RdpManager.Services;
using Xunit;

namespace RdpManager.Tests;

public class RemoteNotificationTests
{
    [Fact]
    public void TryParse_Base64EncodedJson_Succeeds()
    {
        var json = "{\"title\":\"Build\",\"message\":\"Done\",\"level\":\"warn\"}";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var ok = RemoteNotification.TryParse(base64, out var notification);

        Assert.True(ok);
        Assert.Equal("Build", notification.Title);
        Assert.Equal("Done", notification.Message);
        Assert.Equal("warn", notification.Level);
    }

    [Fact]
    public void TryParse_PlainJson_Succeeds()
    {
        var json = "{\"title\":\"Build\",\"message\":\"Done\"}";

        var ok = RemoteNotification.TryParse(json, out var notification);

        Assert.True(ok);
        Assert.Equal("Build", notification.Title);
        Assert.Equal("Done", notification.Message);
    }

    [Fact]
    public void TryParse_MissingMessage_Fails()
    {
        var json = "{\"title\":\"Build\"}";

        Assert.False(RemoteNotification.TryParse(json, out _));
    }

    [Fact]
    public void TryParse_EmptyMessage_Fails()
    {
        var json = "{\"title\":\"Build\",\"message\":\"\"}";

        Assert.False(RemoteNotification.TryParse(json, out _));
    }

    [Fact]
    public void TryParse_LevelWarn_IsPreserved()
    {
        var json = "{\"message\":\"Done\",\"level\":\"warn\"}";

        RemoteNotification.TryParse(json, out var notification);

        Assert.Equal("warn", notification.Level);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("Warn")]
    [InlineData("")]
    public void TryParse_LevelOtherThanWarn_DefaultsToInfo(string level)
    {
        var json = "{\"message\":\"Done\",\"level\":\"" + level + "\"}";

        RemoteNotification.TryParse(json, out var notification);

        Assert.Equal("info", notification.Level);
    }

    [Fact]
    public void TryParse_MissingLevel_DefaultsToInfo()
    {
        var json = "{\"message\":\"Done\"}";

        RemoteNotification.TryParse(json, out var notification);

        Assert.Equal("info", notification.Level);
    }

    [Fact]
    public void TryParse_LongTitle_IsTruncatedTo100Characters()
    {
        var longTitle = new string('a', 150);
        var json = "{\"title\":\"" + longTitle + "\",\"message\":\"Done\"}";

        RemoteNotification.TryParse(json, out var notification);

        Assert.Equal(100, notification.Title.Length);
        Assert.Equal(longTitle[..100], notification.Title);
    }

    [Fact]
    public void TryParse_LongMessage_IsTruncatedTo500Characters()
    {
        var longMessage = new string('b', 800);
        var json = "{\"message\":\"" + longMessage + "\"}";

        RemoteNotification.TryParse(json, out var notification);

        Assert.Equal(500, notification.Message.Length);
        Assert.Equal(longMessage[..500], notification.Message);
    }

    [Fact]
    public void TryParse_PayloadOver4096Characters_Fails()
    {
        var tooLong = new string('a', 4097);

        Assert.False(RemoteNotification.TryParse(tooLong, out _));
    }

    [Fact]
    public void TryParse_NonJsonString_Fails()
    {
        Assert.False(RemoteNotification.TryParse("not json at all", out _));
    }

    [Fact]
    public void TryParse_JsonArray_Fails()
    {
        Assert.False(RemoteNotification.TryParse("[1,2,3]", out _));
    }
}
