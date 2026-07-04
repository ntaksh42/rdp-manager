namespace RdpManager.Models;

public sealed class NodeDto
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "folder"; // folder | connection
    public string Protocol { get; set; } = "RDP";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3389;
    public string Comment { get; set; } = "";

    public string CredentialMode { get; set; } = "inheritFromParent"; // direct | profile | inheritFromParent
    public string CredentialProfile { get; set; } = "";
    public string Username { get; set; } = "";
    public string Domain { get; set; } = "";
    public string PasswordEncrypted { get; set; } = ""; // DPAPI(Base64)

    public bool InheritSettings { get; set; }
    public bool SmartSizing { get; set; } = true;
    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; }
    public bool Fullscreen { get; set; }
    public string Gateway { get; set; } = "";
    /// <summary>サーバー証明書の検証レベル。0=なし / 1=警告 / 2=必須(既定)。</summary>
    public int AuthenticationLevel { get; set; } = 2;
    public string PreCommand { get; set; } = "";
    public string PostCommand { get; set; } = "";

    public List<NodeDto> Children { get; set; } = new();
}
