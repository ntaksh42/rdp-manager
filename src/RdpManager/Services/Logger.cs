using System.IO;

namespace RdpManager.Services;

/// <summary>
/// 任意 ON のファイルログ。接続失敗や握り潰した例外の原因追跡用。
/// 既定は無効。AppSettings.EnableLogging で切替。ログは %APPDATA%\rdpmanager\logs に追記する。
/// </summary>
public static class Logger
{
    private static readonly object Gate = new();

    /// <summary>有効化フラグ。App 起動時に AppSettings から設定する。</summary>
    public static bool Enabled { get; set; }

    /// <summary>ログファイルの上限サイズ。超過したら .old にローテーションする（世代は1つのみ保持）。</summary>
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5MB

    private static string LogPath
    {
        get
        {
            var dir = Path.Combine(ConnectionStore.Directory, "logs");
            return Path.Combine(dir, "rdpmanager.log");
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex)
        => Write("ERROR", $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                var dir = Path.Combine(ConnectionStore.Directory, "logs");
                Directory.CreateDirectory(dir);
                RotateIfNeeded();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line);
            }
        }
        catch { /* ログ自体の失敗はアプリ動作を妨げない */ }
    }

    /// <summary>ログが上限サイズを超えていたら .old へリネームする（既存の .old は削除して上書き）。</summary>
    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath)) return;
        if (new FileInfo(LogPath).Length < MaxLogSizeBytes) return;
        var oldPath = LogPath + ".old";
        if (File.Exists(oldPath)) File.Delete(oldPath);
        File.Move(LogPath, oldPath);
    }
}
