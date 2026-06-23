using System.Diagnostics;
using RdpManager.Common;

namespace RdpManager.Services;

/// <summary>接続前後に実行する外部コマンド。{host} {port} {user} を置換して cmd 経由で実行。</summary>
public static class ExternalTools
{
    public static void Run(string? command, LaunchInfo info)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        // 置換値は外部由来になり得るため、cmd のメタ文字を除去してコマンドインジェクションを防ぐ
        var cmd = command
            .Replace("{host}", ShellSafe.Strip(info.Host))
            .Replace("{port}", info.Port.ToString())
            .Replace("{user}", ShellSafe.Strip(info.Username));
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
        catch (Exception ex) { Logger.Warn($"External command failed: {ex.Message}"); }
    }
}
