using RdpManager.Services;
using Xunit;

namespace RdpManager.Tests;

public class CredentialProtectorTests
{
    [Theory]
    [InlineData("plainPassword123")]
    [InlineData("日本語パスワード!@#$%^&*()")]
    public void Protect_Unprotect_RoundTrips(string plain)
    {
        var protectedValue = CredentialProtector.Protect(plain);
        var result = CredentialProtector.Unprotect(protectedValue);

        Assert.Equal(plain, result);
    }

    [Fact]
    public void Protect_EmptyString_ReturnsEmptyString()
    {
        Assert.Equal("", CredentialProtector.Protect(""));
    }

    [Fact]
    public void Unprotect_EmptyString_ReturnsEmptyString()
    {
        Assert.Equal("", CredentialProtector.Unprotect(""));
    }

    [Fact]
    public void Unprotect_InvalidBase64_ReturnsEmptyStringInsteadOfThrowing()
    {
        Assert.Equal("", CredentialProtector.Unprotect("not-valid-base64!!!"));
    }

    [Fact]
    public void Unprotect_ValidBase64ButNotDpapiCiphertext_ReturnsEmptyStringInsteadOfThrowing()
    {
        var validBase64ButUndecryptable = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });

        Assert.Equal("", CredentialProtector.Unprotect(validBase64ButUndecryptable));
    }
}
