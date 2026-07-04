namespace RdpManager.Models;

public sealed class LaunchInfo
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 3389;
    public string Username { get; init; } = "";
    public string Domain { get; init; } = "";
    /// <summary>メモリ上のみ平文。使い終えたら ScrubPassword() でクリアする。</summary>
    public string Password { get; set; } = "";
    public bool Fullscreen { get; init; }
    public bool SmartSizing { get; init; } = true;
    public bool RedirectClipboard { get; init; } = true;
    public bool RedirectDrives { get; init; }
    public string Gateway { get; init; } = "";
    /// <summary>描画パフォーマンス最適化（壁紙/アニメ/テーマ等を無効化）。</summary>
    public bool PerformanceMode { get; init; } = true;
    /// <summary>サーバー証明書の検証レベル。0=検証なし / 1=警告 / 2=不一致で接続不可（mstsc 既定）。</summary>
    public int AuthenticationLevel { get; init; } = 2;
    /// <summary>全モニタにリモートデスクトップを展開（外部 mstsc の use multimon）。</summary>
    public bool UseMultimon { get; init; }

    /// <summary>不要になったパスワードをメモリ上からクリアする緩和措置。</summary>
    public void ScrubPassword() => Password = "";
}
