using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    /// <summary>起動ごとの後始末情報（TERMSRV 資格情報の復旧・削除と、一時 .rdp ファイルの削除）。</summary>
    private sealed record PendingCleanup(string Host, string RdpPath, bool WroteCred, (string user, string password, uint persist)? ExistingCred);

    // .rdp のパス（ホストごとに一意）をキーに、未実行の遅延クリーンアップを追跡する。
    // アプリ終了時に CleanupAllPending() から一括で片付けられるようにするため。
    private static readonly ConcurrentDictionary<string, PendingCleanup> PendingCleanups = new();

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
        PendingCleanups[rdpPath] = new PendingCleanup(info.Host, rdpPath, wroteCred, existingCred);

        try
        {
            // 3) mstsc 起動（ArgumentList でパスのクオートを安全に処理）
            var psi = new ProcessStartInfo { FileName = "mstsc.exe", UseShellExecute = true };
            psi.ArgumentList.Add(rdpPath);
            var proc = Process.Start(psi)!;

            // 4) 書き込んだ TERMSRV 資格情報・一時 .rdp ファイルをクリーンアップ。
            // CRED_PERSIST_SESSION でもログオフまで残るため、mstsc が読み終えた頃に削除する。
            // 30秒以内にアプリが終了した場合は App.OnExit から CleanupAllPending() で即時に片付ける。
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                CleanupOne(rdpPath);
            });

            // 5) 外部起動ではこれ以上 LaunchInfo のパスワードは不要なのでクリア（A-4 緩和）。
            info.ScrubPassword();
            return proc;
        }
        catch
        {
            // 起動自体が失敗した場合、30秒後のクリーンアップを待たず即座に片付ける
            CleanupOne(rdpPath);
            throw;
        }
    }

    /// <summary>未実行の遅延クリーンアップをすべて即座に片付ける。App 終了処理から呼ぶ。</summary>
    public static void CleanupAllPending()
    {
        foreach (var key in PendingCleanups.Keys.ToArray())
            CleanupOne(key);
    }

    private static void CleanupOne(string rdpPath)
    {
        if (!PendingCleanups.TryRemove(rdpPath, out var pending)) return;

        if (pending.WroteCred)
        {
            // 退避した既存の資格情報があれば、削除せず元の内容へ書き戻す。
            if (pending.ExistingCred is { } e)
            {
                CredentialManager.WriteTerminalServer(pending.Host, e.user, e.password, e.persist);
                Logger.Info($"Restored previous TERMSRV credential for {pending.Host}.");
            }
            else
            {
                CredentialManager.DeleteTerminalServer(pending.Host);
                Logger.Info($"Cleaned up TERMSRV credential for {pending.Host}.");
            }
        }

        try
        {
            if (File.Exists(pending.RdpPath)) File.Delete(pending.RdpPath);
        }
        catch { /* 一時ファイル削除の失敗はアプリ動作を妨げない */ }
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

        var dir = Path.Combine(Path.GetTempPath(), "rdpmanager");
        System.IO.Directory.CreateDirectory(dir);
        var safe = string.Concat(info.Host.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(dir, $"{safe}.rdp");
        File.WriteAllText(path, sb.ToString());
        return path;
    }
}
