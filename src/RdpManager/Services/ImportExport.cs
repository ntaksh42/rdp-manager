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
        var lines = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var f = ParseCsvLine(line);
            if (f.Count == 0) continue;
            // ヘッダ行はスキップ
            if (f[0].Equals("Name", StringComparison.OrdinalIgnoreCase) &&
                f.Count > 1 && f[1].Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;

            string name = f.ElementAtOrDefault(0) ?? "";
            string host = f.ElementAtOrDefault(1) ?? "";
            if (string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(name)) continue;
            if (string.IsNullOrWhiteSpace(host)) host = name;
            if (string.IsNullOrWhiteSpace(name)) name = host;
            int port = int.TryParse(f.ElementAtOrDefault(2), out var p) ? p : 3389;
            list.Add(new ImportedConn(name, host, port,
                f.ElementAtOrDefault(3) ?? "", f.ElementAtOrDefault(4) ?? "", f.ElementAtOrDefault(5) ?? ""));
        }
        return list;
    }

    public static ImportedConn? FromRdp(string text, string fallbackName)
    {
        string host = "", user = "", domain = "";
        int port = 3389;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("full address:s:", StringComparison.OrdinalIgnoreCase))
            {
                var v = line["full address:s:".Length..].Trim();
                var idx = v.LastIndexOf(':');
                if (idx > 0 && int.TryParse(v[(idx + 1)..], out var pp)) { host = v[..idx]; port = pp; }
                else host = v;
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
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }
}
