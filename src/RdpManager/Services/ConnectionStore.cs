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

    public bool InheritSettings { get; set; }
    public bool SmartSizing { get; set; } = true;
    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; }
    public bool Fullscreen { get; set; }
    public string ScreenSize { get; set; } = "クライアント領域に合わせる";
    public string Gateway { get; set; } = "";
    public bool IsFavorite { get; set; }
    public string PreCommand { get; set; } = "";
    public string PostCommand { get; set; } = "";

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
        if (!File.Exists(FilePath)) return null;
        try
        {
            var json = File.ReadAllText(FilePath);
            var doc = JsonSerializer.Deserialize<StoreDocument>(json, Options);
            if (doc is null) BackupCorrupt();
            return doc;
        }
        catch
        {
            // 破損ファイルを上書きで失わないよう退避してから既定値にフォールバック
            BackupCorrupt();
            return null;
        }
    }

    private static void BackupCorrupt()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Copy(FilePath, FilePath + ".bak", overwrite: true);
        }
        catch { /* 退避失敗は致命的でない */ }
    }

    public static void Save(StoreDocument doc)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(doc, Options);
        File.WriteAllText(FilePath, json);
    }
}
