using RdpManager.ViewModels;
using Xunit;

namespace RdpManager.Tests;

public class CredentialProfileTests
{
    [Fact]
    public void Password_ChangingValue_ClearsCachedPasswordEnc()
    {
        var profile = new CredentialProfile { Password = "initial", CachedPasswordEnc = "encrypted-blob" };

        profile.Password = "changed";

        Assert.Null(profile.CachedPasswordEnc);
    }

    [Fact]
    public void Password_ReassigningSameValue_KeepsCachedPasswordEnc()
    {
        // 復号に失敗して空になったパスワードを、そのまま保存し直しても暗号文を失わない
        var profile = new CredentialProfile { Password = "", CachedPasswordEnc = "undecryptable-blob" };

        profile.Password = "";

        Assert.Equal("undecryptable-blob", profile.CachedPasswordEnc);
    }
}
