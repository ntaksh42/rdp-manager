using System.Diagnostics;
using RdpManager.Common;

namespace RdpManager.Services;

/// <summary>RDP 以外のプロトコルを対応する外部クライアントで起動する。</summary>
public static class ProtocolLauncher
{
    /// <summary>実際に接続するポート。RDP 以外でポートが既定の 3389 のままなら各プロトコルの既定ポートに読み替える
    /// （起動と死活チェックで同じ読み替えを使うため共通化）。</summary>
    public static int EffectivePort(string protocol, int port) => port != 3389 ? port : protocol.ToUpperInvariant() switch
    {
        "SSH" => 22,
        "TELNET" => 23,
        "VNC" => 5900,
        _ => port
    };

    /// <summary>起動した場合 true。未対応・失敗時は false。</summary>
    public static bool Launch(string protocol, string host, int port, string user, out string? message)
    {
        message = null;
        // host/user は cmd 経由で起動するため、メタ文字・空白を含む値はコマンドインジェクション防止のため拒否
        if (ShellSafe.HasMeta(host) || host.Contains(' '))
        {
            message = "The host contains characters that are not allowed.";
            return false;
        }
        if (ShellSafe.HasMeta(user) || user.Contains(' '))
        {
            message = "The username contains characters that are not allowed.";
            return false;
        }
        // 先頭が '-' だと ssh/telnet にオプションとして解釈される（引数インジェクション）ため拒否
        if (host.StartsWith('-'))
        {
            message = "The host must not start with '-'.";
            return false;
        }
        if (user.StartsWith('-'))
        {
            message = "The username must not start with '-'.";
            return false;
        }
        try
        {
            switch (protocol.ToUpperInvariant())
            {
                case "SSH":
                    var sshPort = EffectivePort(protocol, port);
                    var target = string.IsNullOrEmpty(user) ? host : $"{user}@{host}";
                    // 宛先の前に -- を入れ、万一の混入時も ssh のオプションとして解釈されないようにする
                    Process.Start(new ProcessStartInfo("cmd.exe", $"/k ssh -p {sshPort} -- {target}") { UseShellExecute = true });
                    return true;

                case "TELNET":
                    var telnetPort = EffectivePort(protocol, port);
                    Process.Start(new ProcessStartInfo("cmd.exe", $"/k telnet {host} {telnetPort}") { UseShellExecute = true });
                    return true;

                case "VNC":
                    var vncPort = EffectivePort(protocol, port);
                    try
                    {
                        // IPv6 リテラルはポートのコロンと区別できるよう角括弧で囲む（vnc://[fe80::1]:5900）
                        Process.Start(new ProcessStartInfo($"vnc://{HostAddress.FormatWithPort(host, vncPort)}") { UseShellExecute = true });
                        return true;
                    }
                    catch
                    {
                        message = "No VNC viewer found. Please install a VNC client.";
                        return false;
                    }

                default:
                    message = $"Unsupported protocol: {protocol}";
                    return false;
            }
        }
        catch (Exception ex)
        {
            message = $"Failed to launch the {protocol} client.\n{ex.Message}";
            return false;
        }
    }
}
