using System.Text;
using System.Text.Json;

namespace RdpManager.Services;

/// <summary>リモート側から仮想チャネル経由で届く通知1件。</summary>
public sealed record RemoteNotification(string Title, string Message, string Level)
{
    private const int MaxPayloadLength = 4096;
    private const int MaxTitleLength = 100;
    private const int MaxMessageLength = 500;

    /// <summary>
    /// チャネル生データを解釈する。ペイロードは Base64(UTF-8 JSON) を正とするが、素の JSON も受け付ける。
    /// リモート側の任意プロセスが同チャネルへ書き込めるため、解釈できないデータは黙って捨てる（false）。
    /// </summary>
    public static bool TryParse(string raw, out RemoteNotification notification)
    {
        notification = null!;
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxPayloadLength) return false;

        string json = raw.Trim();
        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(json));
        }
        catch (FormatException) { /* Base64 でなければ素の JSON として試す */ }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            string title = GetString(doc.RootElement, "title");
            string message = GetString(doc.RootElement, "message");
            string level = GetString(doc.RootElement, "level");
            if (string.IsNullOrWhiteSpace(message)) return false;
            notification = new RemoteNotification(
                Truncate(title, MaxTitleLength),
                Truncate(message, MaxMessageLength),
                level == "warn" ? "warn" : "info");
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static string GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
