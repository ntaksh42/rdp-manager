using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RdpManager.Models;

namespace RdpManager.Services;

// ── 永続化 DTO（JSON 形状）──

public sealed class StoreDocument
{
    public int Version { get; set; } = 1;
    public NodeDto Root { get; set; } = new() { Kind = "folder", Name = "Root" };
    public List<CredentialProfileDto> CredentialProfiles { get; set; } = new();
}

/// <summary>connections.json の読み書き（%APPDATA%\rdpmanager）。</summary>
public static class ConnectionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "rdpmanager");

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
        AtomicWrite.WriteAllText(FilePath, json);
    }
}
