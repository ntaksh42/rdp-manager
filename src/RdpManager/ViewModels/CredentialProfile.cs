namespace RdpManager.ViewModels;

/// <summary>資格情報プロファイル（共通アカウントを名前付きで保持）。Password はメモリ上のみ平文。</summary>
public sealed class CredentialProfile
{
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Username { get; set; } = "";

    private string _password = "";
    public string Password
    {
        get => _password;
        set { if (_password != value) { _password = value; CachedPasswordEnc = null; } }
    }

    /// <summary>暗号化済みパスワードのキャッシュ（平文が未変更なら保存時に再暗号化しない）。
    /// 復号に失敗した暗号文もそのまま保存し直すため、復号できる環境に戻ったときに失われない。</summary>
    public string? CachedPasswordEnc { get; set; }

    public override string ToString() => Name;
}
