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
    public bool AutoReconnect { get; set; } = true;
    public bool EnableLogging { get; set; }
    /// <summary>外部 mstsc 起動時に全モニタへ展開する（use multimon）。</summary>
    public bool UseMultimon { get; set; }
    public List<string> RecentIds { get; set; } = new();
    public List<string> OpenOnExit { get; set; } = new();

    // 前回終了時のウィンドウ位置・サイズ（未保存なら null で既定のまま）
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

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
            AtomicWrite.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失敗は致命的でない */ }
    }
}
