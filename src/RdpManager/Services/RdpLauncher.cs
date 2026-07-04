using System.Diagnostics;
using System.IO;
using System.Text;
using RdpManager.Common;
using RdpManager.Models;

namespace RdpManager.Services;

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
        // 既存の汎用 TERMSRV 資格情報（cmdkey /generic 等でユーザーが恒久登録したもの）があれば
        // 上書きで失わないよう退避し、クリーンアップ時に書き戻す。
        bool wroteCred = false;
        var existingCred = CredentialManager.ReadTerminalServerGeneric(info.Host);
        if (!string.IsNullOrEmpty(info.Username) && !string.IsNullOrEmpty(info.Password))
        {
            var user = string.IsNullOrEmpty(info.Domain) ? info.Username : $"{info.Domain}\\{info.Username}";
            wroteCred = CredentialManager.WriteTerminalServer(info.Host, user, info.Password);
        }

        // 2) .rdp ファイルを生成
        var rdpPath = WriteRdpFile(info);

        // 3) mstsc 起動（ArgumentList でパスのクオートを安全に処理）
        var psi = new ProcessStartInfo { FileName = "mstsc.exe", UseShellExecute = true };
        psi.ArgumentList.Add(rdpPath);
        var proc = Process.Start(psi)!;

        // 4) 書き込んだ TERMSRV 資格情報をクリーンアップ。
        // CRED_PERSIST_SESSION でもログオフまで残るため、mstsc が読み終えた頃に削除する。
        // 退避した既存の資格情報があれば、削除せず元の内容へ書き戻す。
        if (wroteCred)
        {
            var host = info.Host;
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                if (existingCred is { } e)
                {
                    CredentialManager.WriteTerminalServer(host, e.user, e.password, e.persist);
                    Logger.Info($"Restored previous TERMSRV credential for {host}.");
                }
                else
                {
                    CredentialManager.DeleteTerminalServer(host);
                    Logger.Info($"Cleaned up TERMSRV credential for {host}.");
                }
            });
        }

        // 5) 外部起動ではこれ以上 LaunchInfo のパスワードは不要なのでクリア（A-4 緩和）。
        info.ScrubPassword();
        return proc;
    }

    private static string WriteRdpFile(LaunchInfo info)
    {
        var address = HostAddress.Format(info.Host, info.Port);
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
        sb.AppendLine($"authentication level:i:{info.AuthenticationLevel}");
        sb.AppendLine("prompt for credentials:i:0");
        if (info.UseMultimon) sb.AppendLine("use multimon:i:1");
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
