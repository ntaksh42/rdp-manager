using Microsoft.Toolkit.Uwp.Notifications;

namespace RdpManager.Services;

/// <summary>
/// Windows トースト通知の表示とクリック活性化の橋渡し。
/// 未パッケージアプリでもクリックでアプリへ戻れるよう ToastNotificationManagerCompat を使う。
/// 起動コストに影響させないため、初期化（COM アクティベータ登録）は初回 Show まで遅延する。
/// </summary>
public static class ToastService
{
    /// <summary>トーストがクリックされたときに対象セッションキーを通知（非 UI スレッドで発火し得る）。</summary>
    public static event Action<string>? Activated;

    private static bool _initialized;

    public static void Show(string? sessionKey, string sessionTitle, RemoteNotification n)
    {
        try
        {
            EnsureInitialized();
            string title = string.IsNullOrEmpty(n.Title) ? sessionTitle : n.Title;
            if (n.Level == "warn") title = "⚠ " + title;
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(n.Message)
                .AddAttributionText(sessionTitle);
            if (!string.IsNullOrEmpty(sessionKey))
                builder.AddArgument("sessionKey", sessionKey);
            builder.Show();
        }
        catch (Exception ex)
        {
            // トースト不可（通知無効化ポリシー等）でもセッション動作には影響させない
            Logger.Warn($"Toast could not be shown: {ex.Message}");
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = ToastArguments.Parse(e.Argument);
            if (args.TryGetValue("sessionKey", out string key) && key.Length > 0)
                Activated?.Invoke(key);
        };
    }
}
