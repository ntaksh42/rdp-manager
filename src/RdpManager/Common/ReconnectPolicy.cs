namespace RdpManager.Common;

/// <summary>RDP 切断後の自動再接続可否とバックオフを決める純粋ロジック。</summary>
public static class ReconnectPolicy
{
    private static readonly int[] RetryDelaySeconds = [1, 2, 5];

    public static int MaxRetries => RetryDelaySeconds.Length;

    /// <summary>完了済みの再試行回数に対応する次回待機時間。上限到達後は null。</summary>
    public static TimeSpan? NextDelay(int completedRetries)
        => completedRetries >= 0 && completedRetries < RetryDelaySeconds.Length
            ? TimeSpan.FromSeconds(RetryDelaySeconds[completedRetries])
            : null;

    /// <summary>接続確立後の切断が、再試行してよい一時的な通信障害かを判定する。</summary>
    public static bool IsTransientDisconnect(int disconnectReason, int extendedReason)
    {
        // ExtendedDisconnectReason が付く切断は、ユーザー操作・ポリシー・ライセンス・
        // プロトコルエラー等であり、接続を繰り返しても改善しないため再試行しない。
        if (extendedReason != 0) return false;

        return disconnectReason is
            0 or    // 詳細なし。接続確立後に限って呼ばれるため一時断として扱う
            264 or  // connection timed out
            516 or  // socket connect failed
            520 or  // host not found
            772 or  // winsock send failed
            1028 or // socket receive failed
            1288 or // DNS lookup failed
            1540 or // gethostbyname failed
            1796 or // timeout occurred
            2308;   // socket closed
    }

    public static string DescribeDisconnect(int disconnectReason) => disconnectReason switch
    {
        264 or 1796 => "The connection timed out.",
        516 => "Could not connect to the server.",
        520 or 1288 or 1540 => "The server name could not be resolved.",
        772 => "The network connection failed while sending data.",
        1028 => "The network connection failed while receiving data.",
        2308 => "The network connection was closed.",
        _ => "The connection was lost."
    };
}
