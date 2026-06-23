namespace RdpManager.Common;

/// <summary>
/// cmd.exe 経由で起動する文字列に外部由来の値（host/user 等）を埋め込む際の安全化。
/// CSV/.rdp インポートで信頼できない値が入り得るため、シェルのメタ文字を除去/検査する。
/// </summary>
public static class ShellSafe
{
    // cmd.exe が解釈するメタ文字（コマンド連結・リダイレクト・変数展開など）
    private static readonly char[] Meta = { '&', '|', '<', '>', '^', '(', ')', '"', '%', '`', ';', '\r', '\n', '\t' };

    /// <summary>メタ文字・制御文字を含むなら true。</summary>
    public static bool HasMeta(string s)
        => s.Any(c => Array.IndexOf(Meta, c) >= 0 || char.IsControl(c));

    /// <summary>メタ文字・制御文字を取り除いた文字列を返す。</summary>
    public static string Strip(string s)
        => new(s.Where(c => Array.IndexOf(Meta, c) < 0 && !char.IsControl(c)).ToArray());
}
