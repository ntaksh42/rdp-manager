using System.Text;

namespace RdpManager.Services;

public sealed record ImportedConn(string Name, string Host, int Port, string Domain, string Username, string Comment);

/// <summary>CSV / .rdp の入出力（パスワードは扱わない）。</summary>
public static class ImportExport
{
    public static string ToCsv(IEnumerable<ImportedConn> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Host,Port,Domain,Username,Comment");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",",
                Q(r.Name), Q(r.Host), r.Port.ToString(), Q(r.Domain), Q(r.Username), Q(r.Comment)));
        return sb.ToString();
    }

    public static List<ImportedConn> FromCsv(string text)
    {
        var list = new List<ImportedConn>();
        foreach (var f in ParseCsv(text))
        {
            if (f.Count == 0) continue;
            // ヘッダ行はスキップ
            if (f[0].Equals("Name", StringComparison.OrdinalIgnoreCase) &&
                f.Count > 1 && f[1].Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;

            string name = Unescape(f.ElementAtOrDefault(0) ?? "");
            string host = Unescape(f.ElementAtOrDefault(1) ?? "");
            if (string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(name)) continue;
            if (string.IsNullOrWhiteSpace(host)) host = name;
            if (string.IsNullOrWhiteSpace(name)) name = host;
            int port = int.TryParse(f.ElementAtOrDefault(2), out var p) ? p : 3389;
            list.Add(new ImportedConn(name, host, port,
                Unescape(f.ElementAtOrDefault(3) ?? ""), Unescape(f.ElementAtOrDefault(4) ?? ""), Unescape(f.ElementAtOrDefault(5) ?? "")));
        }
        return list;
    }

    public static ImportedConn? FromRdp(string text, string fallbackName)
    {
        string host = "", user = "", domain = "";
        int port = 3389;
        bool portFromFullAddress = false; // full address 側の明示ポートを優先するため、行順に依存せず優先度を記録する
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("full address:s:", StringComparison.OrdinalIgnoreCase))
            {
                var v = line["full address:s:".Length..].Trim();
                var (h, p) = RdpManager.Common.HostAddress.Parse(v);
                host = h;
                if (p is { } pp) { port = pp; portFromFullAddress = true; }
            }
            else if (line.StartsWith("server port:i:", StringComparison.OrdinalIgnoreCase))
            {
                var v = line["server port:i:".Length..].Trim();
                if (!portFromFullAddress && int.TryParse(v, out var pp) && pp is >= 1 and <= 65535)
                    port = pp;
            }
            else if (line.StartsWith("username:s:", StringComparison.OrdinalIgnoreCase))
            {
                var v = line["username:s:".Length..].Trim();
                var bs = v.IndexOf('\\');
                if (bs > 0) { domain = v[..bs]; user = v[(bs + 1)..]; }
                else user = v;
            }
        }
        if (string.IsNullOrWhiteSpace(host)) return null;
        return new ImportedConn(fallbackName, host, port, domain, user, "");
    }

    private static string Q(string s)
    {
        s ??= "";
        // CSV 数式インジェクション対策: Excel 等が数式と解釈する先頭文字の前に ' を付与して無害化する
        if (s.Length > 0 && "=+-@".Contains(s[0]))
            s = "'" + s;
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    /// <summary>Q() が数式インジェクション対策で付与した先頭の ' を取り除く（インポート時のラウンドトリップ用）。</summary>
    private static string Unescape(string s)
    {
        if (s.Length >= 2 && s[0] == '\'' && "=+-@".Contains(s[1]))
            return s[1..];
        return s;
    }

    /// <summary>
    /// RFC4180 準拠の CSV パーサ。引用符で囲まれたフィールド内の改行・カンマ・""(=") を正しく扱う。
    /// 行分割を先に行わず、クォート状態を見ながらレコード/フィールドを切り出す。
    /// </summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        bool rowHasContent = false;

        void EndField() { row.Add(sb.ToString()); sb.Clear(); }
        void EndRow()
        {
            EndField();
            // 空行（フィールドが1つで中身も空）は捨てる
            if (rowHasContent) rows.Add(row);
            row = new List<string>();
            rowHasContent = false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                switch (c)
                {
                    case '"': inQuotes = true; rowHasContent = true; break;
                    case ',': EndField(); rowHasContent = true; break;
                    case '\r': break; // \r\n / \r どちらも \n 側 or 次で行終端
                    case '\n': EndRow(); break;
                    default: sb.Append(c); rowHasContent = true; break;
                }
            }
        }
        // 末尾フィールド/行
        if (rowHasContent || sb.Length > 0 || row.Count > 0) EndRow();
        return rows;
    }
}
