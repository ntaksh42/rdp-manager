using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RdpManager.Services;

// ── 永続化 DTO（JSON 形状）──

public sealed class StoreDocument
{
    public int Version { get; set; } = 1;
    public NodeDto Root { get; set; } = new() { Kind = "folder", Name = "Root" };
    public List<CredentialProfileDto> CredentialProfiles { get; set; } = new();
}

public sealed class NodeDto
{
    public string Kind { get; set; } = "folder"; // folder | connection
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3389;
    public string Comment { get; set; } = "";

    public string CredentialMode { get; set; } = "inheritFromParent"; // direct | profile | inheritFromParent
    public string CredentialProfile { get; set; } = "";
    public string Username { get; set; } = "";
    public string Domain { get; set; } = "";
    public string PasswordEncrypted { get; set; } = ""; // DPAPI(Base64)

    public bool SmartSizing { get; set; } = true;
    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; }
    public bool Fullscreen { get; set; }
    public string ScreenSize { get; set; } = "クライアント領域に合わせる";
    public string Gateway { get; set; } = "";

    public List<NodeDto> Children { get; set; } = new();
}

public sealed class CredentialProfileDto
{
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordEncrypted { get; set; } = "";
}

/// <summary>connections.json の読み書き（%APPDATA%\RdpManager）。</summary>
public static class ConnectionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpManager");

    public static string FilePath => Path.Combine(Directory, "connections.json");

    public static StoreDocument? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<StoreDocument>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(StoreDocument doc)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(doc, Options);
        File.WriteAllText(FilePath, json);
    }
}
