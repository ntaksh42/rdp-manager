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
    [GeneratedRegex(@"\{([^}]*)\}|\[(\d+)-(\d+)\]")]
    private static partial Regex GroupRegex();

    public static List<string> Expand(string pattern)
    {
        var result = new List<string> { "" };
        int pos = 0;
        foreach (Match m in GroupRegex().Matches(pattern))
        {
            var literal = pattern.Substring(pos, m.Index - pos);
            var options = new List<string>();
            if (m.Groups[1].Success)
            {
                options.AddRange(m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries));
            }
            else
            {
                string lo = m.Groups[2].Value, hi = m.Groups[3].Value;
                int a = int.Parse(lo), b = int.Parse(hi), width = lo.Length;
                if (a > b) (a, b) = (b, a);
                for (int n = a; n <= b; n++) options.Add(n.ToString().PadLeft(width, '0'));
            }

            var next = new List<string>(result.Count * Math.Max(options.Count, 1));
            foreach (var r in result)
                foreach (var o in options)
                    next.Add(r + literal + o);
            result = next.Count > 0 ? next : result;
            pos = m.Index + m.Length;
        }
        var tail = pattern.Substring(pos);
        for (int k = 0; k < result.Count; k++) result[k] += tail;
        return result;
    }
}
