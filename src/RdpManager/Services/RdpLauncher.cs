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

    // ホスト（= TERMSRV/<host> の資格情報ターゲット）をキーに、未実行の遅延クリーンアップを追跡する。
    // アプリ終了時に CleanupAllPending() から一括で片付けられるようにするため。
    // 資格情報の退避・書き込み・復旧が遅延タスク（スレッドプール）と交錯しないよう Gate で直列化する。
    private static readonly object Gate = new();
    private static readonly Dictionary<string, PendingCleanup> PendingCleanups = new(StringComparer.OrdinalIgnoreCase);

    public static Process Launch(LaunchInfo info)
    {
        // 1) 資格情報を登録（パスワードがある場合）。CredWrite で直接書き込み。
        // 既存の汎用 TERMSRV 資格情報（cmdkey /generic 等でユーザーが恒久登録したもの）があれば
        // 上書きで失わないよう退避し、クリーンアップ時に書き戻す。
        PendingCleanup pending;
        lock (Gate)
        {
            // 同じホストの前回起動分のクリーンアップが未実行（30秒以内の再起動）なら、現在の TERMSRV 資格情報は
            // 前回このアプリが書いた一時的なもの。それを「既存」として退避すると、ユーザー本来の資格情報が
            // 一時資格情報で上書き復旧されて失われるため、前回の退避内容と書き込み有無を引き継ぐ
            PendingCleanups.TryGetValue(info.Host, out var prev);
            var existingCred = prev is not null ? prev.ExistingCred : CredentialManager.ReadTerminalServerGeneric(info.Host);
            bool wroteCred = prev?.WroteCred == true;
            if (!string.IsNullOrEmpty(info.Username) && !string.IsNullOrEmpty(info.Password))
            {
                var user = string.IsNullOrEmpty(info.Domain) ? info.Username : $"{info.Domain}\\{info.Username}";
                wroteCred |= CredentialManager.WriteTerminalServer(info.Host, user, info.Password);
            }
            pending = new PendingCleanup(info.Host, RdpPathFor(info.Host), wroteCred, existingCred);
            PendingCleanups[info.Host] = pending; // 前回分の遅延クリーンアップは自分のエントリでなくなるため no-op になる
        }

        try
        {
            // 2) .rdp ファイルを生成
            WriteRdpFile(info, pending.RdpPath);

            // 3) mstsc 起動（ArgumentList でパスのクオートを安全に処理）
            var psi = new ProcessStartInfo { FileName = "mstsc.exe", UseShellExecute = true };
            psi.ArgumentList.Add(pending.RdpPath);
            var proc = Process.Start(psi)!;

            // 4) 書き込んだ TERMSRV 資格情報・一時 .rdp ファイルをクリーンアップ。
            // CRED_PERSIST_SESSION でもログオフまで残るため、mstsc が読み終えた頃に削除する。
            // 30秒以内にアプリが終了した場合は App.OnExit から CleanupAllPending() で即時に片付ける。
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                CleanupOne(pending);
            });

            // 5) 外部起動ではこれ以上 LaunchInfo のパスワードは不要なのでクリア（A-4 緩和）。
            info.ScrubPassword();
            return proc;
        }
        catch
        {
            // .rdp 生成や起動自体が失敗した場合、30秒後のクリーンアップを待たず即座に片付ける
            CleanupOne(pending);
            throw;
        }
    }

    /// <summary>未実行の遅延クリーンアップをすべて即座に片付ける。App 終了処理から呼ぶ。</summary>
    public static void CleanupAllPending()
    {
        PendingCleanup[] all;
        lock (Gate) all = PendingCleanups.Values.ToArray();
        foreach (var pending in all)
            CleanupOne(pending);
    }

    /// <summary>expected が現在の登録と同一の場合だけ片付ける（後から同じホストで再起動していれば、
    /// そちらの遅延クリーンアップに任せる）。</summary>
    private static void CleanupOne(PendingCleanup expected)
    {
        lock (Gate)
        {
            if (!PendingCleanups.TryGetValue(expected.Host, out var current) || !ReferenceEquals(current, expected)) return;
            PendingCleanups.Remove(expected.Host);

            if (expected.WroteCred)
            {
                // 退避した既存の資格情報があれば、削除せず元の内容へ書き戻す。
                if (expected.ExistingCred is { } e)
                {
                    CredentialManager.WriteTerminalServer(expected.Host, e.user, e.password, e.persist);
                    Logger.Info($"Restored previous TERMSRV credential for {expected.Host}.");
                }
                else
                {
                    CredentialManager.DeleteTerminalServer(expected.Host);
                    Logger.Info($"Cleaned up TERMSRV credential for {expected.Host}.");
                }
            }
        }

        try
        {
            if (File.Exists(expected.RdpPath)) File.Delete(expected.RdpPath);
        }
        catch { /* 一時ファイル削除の失敗はアプリ動作を妨げない */ }
    }

    private static string RdpPathFor(string host)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rdpmanager");
        var safe = string.Concat(host.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(dir, $"{safe}.rdp");
    }

    private static void WriteRdpFile(LaunchInfo info, string path)
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

        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
    }
}
