using System.Text.RegularExpressions;

namespace RdpManager.Common;

/// <summary>
/// RDCMan 互換のホストパターン展開。
/// [n-m] は数値範囲（下限の桁数でゼロ埋め）、{a,b,c} は列挙。複数指定は直積。
/// 例: server[01-03] => server01, server02, server03
///     {dca,dcb}rack[1-2] => dcarack1, dcarack2, dcbrack1, dcbrack2
/// </summary>
public static partial class PatternExpander
{
    /// <summary>展開結果の上限。誤入力（巨大範囲）でのメモリ枯渇・UI フリーズを防ぐ。</summary>
    public const int MaxResults = 1000;

    [GeneratedRegex(@"\{([^}]*)\}|\[(\d+)-(\d+)\]")]
    private static partial Regex GroupRegex();

    /// <summary>
    /// パターンを展開する。総数が <see cref="MaxResults"/> を超える場合は実体化せず例外を投げる
    /// （事前に件数を直積で見積もるため巨大リストを生成しない）。
    /// </summary>
    public static List<string> Expand(string pattern)
    {
        var total = CountExpansions(pattern, out var segments);
        if (total > MaxResults)
            throw new PatternTooLargeException(total);

        var result = new List<string> { "" };
        foreach (var (literal, options) in segments)
        {
            var next = new List<string>(result.Count * Math.Max(options.Count, 1));
            foreach (var r in result)
                foreach (var o in options)
                    next.Add(r + literal + o);
            result = next.Count > 0 ? next : result;
        }
        return result;
    }

    /// <summary>展開せずに総件数だけ見積もる（プレビューの警告表示用）。</summary>
    public static long Count(string pattern) => CountExpansions(pattern, out _);

    private static long CountExpansions(string pattern, out List<(string literal, List<string> options)> segments)
    {
        segments = new List<(string, List<string>)>();
        long total = 1;
        int pos = 0;
        foreach (Match m in GroupRegex().Matches(pattern))
        {
            var literal = pattern.Substring(pos, m.Index - pos);
            var options = new List<string>();
            long segmentCount;
            if (m.Groups[1].Success)
            {
                options.AddRange(m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries));
                if (options.Count == 0) options.Add(""); // "abc{}" のような空展開でも直前リテラルを失わない
                segmentCount = options.Count;
            }
            else
            {
                // \d+ は桁数無制限のため int.Parse だと巨大な値で OverflowException になる。
                // long.Parse でも表現できない桁数はパース失敗として PatternTooLargeException に正規化する。
                string lo = m.Groups[2].Value, hi = m.Groups[3].Value;
                if (!long.TryParse(lo, out var a) || !long.TryParse(hi, out var b))
                    throw new PatternTooLargeException(long.MaxValue);
                if (a > b) (a, b) = (b, a);
                int width = lo.Length;
                try { segmentCount = checked(b - a + 1); }
                catch (OverflowException) { throw new PatternTooLargeException(long.MaxValue); }

                if (segmentCount > MaxResults)
                {
                    // この時点で全体の件数超過が確定するため、巨大レンジを実体化しない
                    // （実際の文字列生成は Expand() 側の上限チェックで拒否されるので不要）
                }
                else
                {
                    for (long n = a; n <= b; n++) options.Add(n.ToString().PadLeft(width, '0'));
                }
            }
            segments.Add((literal, options));
            try { total = checked(total * Math.Max(segmentCount, 1)); }
            catch (OverflowException) { total = int.MaxValue; }
            if (total > int.MaxValue) total = int.MaxValue; // オーバーフロー回避（上限超は確定）
            pos = m.Index + m.Length;
        }
        // 末尾リテラルは件数に影響しないので、最後のセグメントに連結
        var tail = pattern.Substring(pos);
        if (tail.Length > 0)
        {
            if (segments.Count == 0) segments.Add((tail, new List<string> { "" }));
            else segments.Add(("", new List<string> { tail }));
        }
        return total;
    }
}

/// <summary>パターン展開の件数が上限を超えたときに投げる。</summary>
public sealed class PatternTooLargeException(long count)
    : Exception($"The pattern expands to {count} hosts, which exceeds the limit of {PatternExpander.MaxResults}.")
{
    public long Count { get; } = count;
}
