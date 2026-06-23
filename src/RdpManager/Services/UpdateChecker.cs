using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace RdpManager.Services;

/// <summary>GitHub Releases の最新版を確認し、更新があるか判定する。</summary>
public static class UpdateChecker
{
    private const string LatestApi = "https://api.github.com/repos/ntaksh42/rdp-manager/releases/latest";
    public const string ReleasesUrl = "https://github.com/ntaksh42/rdp-manager/releases";

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public sealed record Result(bool IsNewer, string LatestTag, Version Current);

    public static async Task<Result?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RdpManager-UpdateCheck");
            var json = await http.GetStringAsync(LatestApi);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var latest = ParseVersion(tag);
            if (latest is null) return null;
            return new Result(latest > CurrentVersion, tag, CurrentVersion);
        }
        catch
        {
            return null;
        }
    }

    private static Version? ParseVersion(string tag)
    {
        var s = tag.TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : null;
    }
}
