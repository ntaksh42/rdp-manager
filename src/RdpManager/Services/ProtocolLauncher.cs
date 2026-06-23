using System.Diagnostics;

namespace RdpManager.Services;

/// <summary>RDP 以外のプロトコルを対応する外部クライアントで起動する。</summary>
public static class ProtocolLauncher
{
    /// <summary>起動した場合 true。未対応・失敗時は false。</summary>
    public static bool Launch(string protocol, string host, int port, string user, out string? message)
    {
        message = null;
        try
        {
            switch (protocol.ToUpperInvariant())
            {
                case "SSH":
                    var sshPort = port == 3389 ? 22 : port;
                    var target = string.IsNullOrEmpty(user) ? host : $"{user}@{host}";
                    Process.Start(new ProcessStartInfo("cmd.exe", $"/k ssh -p {sshPort} {target}") { UseShellExecute = true });
                    return true;

                case "TELNET":
                    var telnetPort = port == 3389 ? 23 : port;
                    Process.Start(new ProcessStartInfo("cmd.exe", $"/k telnet {host} {telnetPort}") { UseShellExecute = true });
                    return true;

                case "VNC":
                    var vncPort = port == 3389 ? 5900 : port;
                    try
                    {
                        Process.Start(new ProcessStartInfo($"vnc://{host}:{vncPort}") { UseShellExecute = true });
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
