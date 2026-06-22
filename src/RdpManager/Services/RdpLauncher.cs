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
}

/// <summary>
/// Windows 標準 RDP クライアント（mstsc.exe）を起動して接続する。
/// 資格情報は cmdkey で Windows 資格情報マネージャーへ登録してから .rdp で接続するため、
/// パスワードを平文の .rdp に書かない。
/// </summary>
public static class RdpLauncher
{
    public static Process Launch(LaunchInfo info)
    {
        // 1) 資格情報を一時登録（パスワードがある場合）
        if (!string.IsNullOrEmpty(info.Username) && !string.IsNullOrEmpty(info.Password))
        {
            var user = string.IsNullOrEmpty(info.Domain) ? info.Username : $"{info.Domain}\\{info.Username}";
            RunCmdKey($"/generic:TERMSRV/{info.Host} /user:{user} /pass:{info.Password}");
        }

        // 2) .rdp ファイルを生成
        var rdpPath = WriteRdpFile(info);

        // 3) mstsc 起動
        return Process.Start(new ProcessStartInfo
        {
            FileName = "mstsc.exe",
            Arguments = $"\"{rdpPath}\"",
            UseShellExecute = true
        })!;
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

    private static void RunCmdKey(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "cmdkey.exe",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            p?.WaitForExit(3000);
        }
        catch { /* 資格情報登録失敗時は mstsc のプロンプトにフォールバック */ }
    }
}
