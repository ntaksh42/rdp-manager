using System.Diagnostics;

namespace RdpManager.Services;

/// <summary>接続前後に実行する外部コマンド。{host} {port} {user} を置換して cmd 経由で実行。</summary>
public static class ExternalTools
{
    public static void Run(string? command, LaunchInfo info)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        var cmd = command
            .Replace("{host}", info.Host)
            .Replace("{port}", info.Port.ToString())
            .Replace("{user}", info.Username);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + cmd,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch { /* 外部ツール失敗は接続を妨げない */ }
    }
}
