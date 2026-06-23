using System.IO;
using System.Text.Json;

namespace RdpManager.Services;

/// <summary>アプリ全体の軽量設定（テーマ等）。connections.json とは別ファイル。</summary>
public sealed class AppSettings
{
    public bool DarkMode { get; set; }
    public bool RestoreSessions { get; set; } = true;
    public bool FullscreenSpan { get; set; }
    public bool PerformanceMode { get; set; } = true;
    public List<string> RecentIds { get; set; } = new();
    public List<string> OpenOnExit { get; set; } = new();

    private static string FilePath =>
        Path.Combine(ConnectionStore.Directory, "appsettings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* 既定値へ */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConnectionStore.Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失敗は致命的でない */ }
    }
}
