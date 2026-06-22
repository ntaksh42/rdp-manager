namespace RdpManager.ViewModels;

/// <summary>資格情報プロファイル（共通アカウントを名前付きで保持）。Password はメモリ上のみ平文。</summary>
public sealed class CredentialProfile
{
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public override string ToString() => Name;
}
