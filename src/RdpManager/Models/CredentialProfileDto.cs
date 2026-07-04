namespace RdpManager.Models;

public sealed class CredentialProfileDto
{
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordEncrypted { get; set; } = "";
}
