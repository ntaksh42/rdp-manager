using System.Net;

namespace RdpManager.Common;

/// <summary>
/// host:port の整形・分離。IPv6 リテラル（コロンを含む）は角括弧で囲って曖昧さを避ける。
/// 例: ("fe80::1", 3390) => "[fe80::1]:3390" / "[::1]:3389" => ("::1", 3389)
/// </summary>
public static class HostAddress
{
    public static bool IsIPv6(string host)
        => IPAddress.TryParse(host, out var ip)
           && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;

    /// <summary>表示・接続用に host を整形（IPv6 は角括弧で囲む）。port が既定(3389)なら port を付けない。</summary>
    public static string Format(string host, int port, int defaultPort = 3389)
    {
        var h = IsIPv6(host) ? $"[{host}]" : host;
        return port == defaultPort ? h : $"{h}:{port}";
    }

    /// <summary>常に port を付けて整形（既定ポートでも付与）。</summary>
    public static string FormatWithPort(string host, int port)
        => (IsIPv6(host) ? $"[{host}]" : host) + ":" + port;

    /// <summary>"host"/"host:port"/"[ipv6]"/"[ipv6]:port"/"裸の ipv6" を (host, port) に分離。</summary>
    public static (string host, int? port) Parse(string value)
    {
        value = value.Trim();
        if (value.StartsWith('['))
        {
            var end = value.IndexOf(']');
            if (end > 0)
            {
                var host = value[1..end];
                var rest = value[(end + 1)..];
                if (rest.StartsWith(':') && int.TryParse(rest[1..], out var p)) return (host, p);
                return (host, null);
            }
            return (value, null);
        }
        // 角括弧なし: コロンが1個だけなら host:port、複数なら裸の IPv6 リテラル
        int first = value.IndexOf(':');
        if (first >= 0 && first == value.LastIndexOf(':'))
        {
            var host = value[..first];
            if (int.TryParse(value[(first + 1)..], out var p)) return (host, p);
            return (host, null);
        }
        return (value, null);
    }
}
