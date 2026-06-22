using System.Security.Cryptography;
using System.Text;

namespace RdpManager.Services;

/// <summary>
/// パスワードを DPAPI（CurrentUser スコープ）で暗号化/復号する。
/// 暗号化結果は同一 Windows ユーザー・同一マシンでのみ復号可能。平文保存はしない。
/// </summary>
public static class CredentialProtector
{
    // 改ざん検知・他用途流用防止のためのエントロピー
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RdpManager.v1");

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = Encoding.UTF8.GetBytes(plain);
        var enc = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(enc);
    }

    public static string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return "";
        try
        {
            var enc = Convert.FromBase64String(protectedBase64);
            var bytes = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // 別ユーザー/別マシンへ移行したケースなど。復号不能時は空扱い。
            return "";
        }
    }
}
