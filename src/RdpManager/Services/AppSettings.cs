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
    /// <summary>リモート側から仮想チャネル経由で届く通知をトースト表示する。</summary>
    public bool RemoteNotifications { get; set; } = true;
    /// <summary>外部 mstsc 起動時に全モニタへ展開する（use multimon）。</summary>
    public bool UseMultimon { get; set; }
    /// <summary>Quick Switch を開くグローバルホットキーの修飾キー（MOD_* のビット和）。既定は Ctrl+Alt。</summary>
    public uint QuickSwitchModifiers { get; set; } = 0x1 | 0x2;
    /// <summary>Quick Switch を開くグローバルホットキーの仮想キーコード。既定は VK_HOME。</summary>
    public uint QuickSwitchKey { get; set; } = 0x24;
    /// <summary>全画面トグル用の追加グローバルホットキーの修飾キー（MOD_* のビット和）。既定は未設定（0）。</summary>
    public uint FullscreenModifiers { get; set; }
    /// <summary>全画面トグル用の追加グローバルホットキーの仮想キーコード。既定は未設定（0）。</summary>
    public uint FullscreenKey { get; set; }
    public List<string> OpenOnExit { get; set; } = new();
    /// <summary>終了時に右ペイン（分割ビュー）で開いていた接続。復元時に配置を再現する。</summary>
    public List<string> OpenOnExitRight { get; set; } = new();

    // 前回終了時のウィンドウ位置・サイズ（未保存なら null で既定のまま）
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    private static string FilePath =>
        Path.Combine(ConnectionStore.Directory, "appsettings.json");

    public static AppSettings Load() => LoadFrom(FilePath);

    /// <summary>
    /// 設定を読む。破損していれば .bak へ退避し、AtomicWrite が残す直前版(.prev)からの復旧を試みる
    /// （無通知で既定値に戻すと、直後の保存で破損ファイルごとホットキー等の設定が失われるため）。
    /// </summary>
    internal static AppSettings LoadFrom(string path)
    {
        if (!File.Exists(path)) return new AppSettings();
        if (TryRead(path) is { } settings) return settings;

        Logger.Warn($"App settings could not be read; backing up to {path}.bak");
        try { File.Copy(path, path + ".bak", overwrite: true); }
        catch { /* 退避失敗は致命的でない */ }
        if (TryRead(path + ".prev") is { } prev)
        {
            Logger.Info("App settings restored from the previous version (.prev).");
            return prev;
        }
        return new AppSettings();
    }

    private static AppSettings? TryRead(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) : null; }
        catch { return null; }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConnectionStore.Directory);
            AtomicWrite.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Logger.Warn($"Failed to save app settings: {ex.Message}"); }
    }
}
