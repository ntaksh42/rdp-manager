using System.Diagnostics;
using System.IO;
using System.Text;

namespace RdpManager.Services;

public sealed class LaunchInfo
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 3389;
    public string Username { get; init; } = "";
    public string Domain { get; init; } = "";
    public string Password { get; init; } = "";
    public bool Fullscreen { get; init; }
    public bool SmartSizing { get; init; } = true;
    public bool RedirectClipboard { get; init; } = true;
    public bool RedirectDrives { get; init; }
    public string Gateway { get; init; } = "";
    /// <summary>描画パフォーマンス最適化（壁紙/アニメ/テーマ等を無効化）。</summary>
    public bool PerformanceMode { get; init; } = true;
}

/// <summary>
/// Windows 標準 RDP クライアント（mstsc.exe）を起動して接続する。
/// 資格情報は CredWrite API で Windows 資格情報マネージャーへ登録してから .rdp で接続するため、
/// パスワードを平文の .rdp に書かず、コマンドライン引数にも露出させない。
/// </summary>
public static class RdpLauncher
{
    public static Process Launch(LaunchInfo info)
    {
        // 1) 資格情報を登録（パスワードがある場合）。CredWrite で直接書き込み。
        if (!string.IsNullOrEmpty(info.Username) && !string.IsNullOrEmpty(info.Password))
        {
            var user = string.IsNullOrEmpty(info.Domain) ? info.Username : $"{info.Domain}\\{info.Username}";
            CredentialManager.WriteTerminalServer(info.Host, user, info.Password);
        }

        // 2) .rdp ファイルを生成
        var rdpPath = WriteRdpFile(info);

        // 3) mstsc 起動（ArgumentList でパスのクオートを安全に処理）
        var psi = new ProcessStartInfo { FileName = "mstsc.exe", UseShellExecute = true };
        psi.ArgumentList.Add(rdpPath);
        return Process.Start(psi)!;
    }

    private static string WriteRdpFile(LaunchInfo info)
    {
        var address = info.Port == 3389 ? info.Host : $"{info.Host}:{info.Port}";
        var sb = new StringBuilder();
        sb.AppendLine($"full address:s:{address}");
        if (!string.IsNullOrEmpty(info.Username))
        {
            var user = string.IsNullOrEmpty(info.Domain) ? info.Username : $"{info.Domain}\\{info.Username}";
            sb.AppendLine($"username:s:{user}");
        }
        sb.AppendLine($"screen mode id:i:{(info.Fullscreen ? 2 : 1)}");
        sb.AppendLine($"smart sizing:i:{(info.SmartSizing ? 1 : 0)}");
        sb.AppendLine($"redirectclipboard:i:{(info.RedirectClipboard ? 1 : 0)}");
        sb.AppendLine($"drivestoredirect:s:{(info.RedirectDrives ? "*" : "")}");
        sb.AppendLine("authentication level:i:0");
        sb.AppendLine("prompt for credentials:i:0");
        if (!string.IsNullOrWhiteSpace(info.Gateway))
        {
            sb.AppendLine($"gatewayhostname:s:{info.Gateway}");
            sb.AppendLine("gatewayusagemethod:i:1");
            sb.AppendLine("gatewayprofileusagemethod:i:1");
        }

        var dir = Path.Combine(Path.GetTempPath(), "RdpManager");
        System.IO.Directory.CreateDirectory(dir);
        var safe = string.Concat(info.Host.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(dir, $"{safe}.rdp");
        File.WriteAllText(path, sb.ToString());
        return path;
    }
}
