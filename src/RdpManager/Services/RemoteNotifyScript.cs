using System.IO;
using System.Text;

namespace RdpManager.Services;

/// <summary>
/// リモート側に配置する通知送信スクリプト一式のエクスポート。
/// スクリプトは RDP 仮想チャネル "CCNOTIF" へ Base64(UTF-8 JSON) を書き込み、
/// クライアント側（RdpClientHost）が OnChannelReceivedData で受け取る。
/// </summary>
public static class RemoteNotifyScript
{
    public const string ScriptFileName = "rdp-notify.ps1";
    public const string HooksSampleFileName = "claude-hooks-sample.json";

    /// <summary>2ファイルを書き出し、作成したフルパスを返す。</summary>
    public static string[] Export(string folder)
    {
        var ps1 = Path.Combine(folder, ScriptFileName);
        var hooks = Path.Combine(folder, HooksSampleFileName);
        // ps1 は Windows PowerShell 5.1 でも文字化けしないよう BOM 付き UTF-8
        File.WriteAllText(ps1, Script, new UTF8Encoding(true));
        File.WriteAllText(hooks, HooksSample, new UTF8Encoding(false));
        return new[] { ps1, hooks };
    }

    // 静的仮想チャネルの1チャンク上限(1600バイト)を超えないよう、送信側で長さを丸める。
    // ここは PowerShell の閉じヒアストリング("@)が行頭必須のため、意図的に列0で埋め込む。
    // 送信失敗は端末に理由を出すが exit code は 0 を保つ（Claude Code の hook を壊さないため）。
    // PowerShell 5.1 互換のため $PSStyle・三項演算子・`u{} 記法は使わない。
    private const string Script =
"""
<#
rdpmanager remote notification sender.

Sends a notification to the rdpmanager client over the RDP static virtual
channel "CCNOTIF", and prints a banner on the local terminal.

Exits 0 even when the notification cannot be delivered (outside an RDP
session, or the RDP client did not register the channel), so it is always
safe to call from a hook. Use -Strict to get a non-zero exit for debugging.

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File rdp-notify.ps1 `
    -Message "text" [-Title "title"] [-Level info|warn] [-Quiet] [-Diag] [-Strict]
#>
param(
    [Parameter(Mandatory = $true)][string]$Message,
    [string]$Title = "",
    [ValidateSet("info", "warn")][string]$Level = "info",
    [switch]$Quiet,
    [switch]$Diag,
    [switch]$Strict
)

Add-Type -Namespace RdpNotify -Name Wts -MemberDefinition @"
[DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
public static extern IntPtr WTSVirtualChannelOpen(IntPtr hServer, int SessionId, string pVirtualName);
[DllImport("wtsapi32.dll", SetLastError = true)]
public static extern bool WTSVirtualChannelWrite(IntPtr hChannel, byte[] Buffer, int Length, out int pBytesWritten);
[DllImport("wtsapi32.dll")]
public static extern bool WTSVirtualChannelClose(IntPtr hChannel);
"@ -ErrorAction SilentlyContinue -ErrorVariable addTypeError

# ── 端末の表示能力を判定する ──
# ANSI と Unicode 罫線は別物。コードページ 932 では ANSI が使えても罫線が化ける。
$script:UseAnsi = $false
$script:UseUnicode = $false
if (-not [Console]::IsOutputRedirected -and -not $env:NO_COLOR) {
    if ($env:WT_SESSION -or $env:TERM_PROGRAM -or $env:ConEmuANSI -eq 'ON') {
        $script:UseAnsi = $true
    } else {
        try { $script:UseAnsi = [bool]$Host.UI.SupportsVirtualTerminal } catch { $script:UseAnsi = $false }
    }
    try { $script:UseUnicode = ([Console]::OutputEncoding.CodePage -eq 65001) } catch { $script:UseUnicode = $false }
}

$ESC = [string][char]27

function Get-DisplayWidth([string]$s) {
    # 罫線を揃えるため全角を2幅で数える（CJK の主要ブロックのみの簡易判定）。
    $w = 0
    foreach ($ch in $s.ToCharArray()) {
        $c = [int][char]$ch
        if (($c -ge 0x1100 -and $c -le 0x115F) -or ($c -ge 0x2E80 -and $c -le 0xA4CF) -or
            ($c -ge 0xAC00 -and $c -le 0xD7A3) -or ($c -ge 0xF900 -and $c -le 0xFAFF) -or
            ($c -ge 0xFE30 -and $c -le 0xFE6F) -or ($c -ge 0xFF00 -and $c -le 0xFF60) -or
            ($c -ge 0xFFE0 -and $c -le 0xFFE6)) { $w += 2 } else { $w += 1 }
    }
    return $w
}

function Write-Banner([string[]]$Lines, [bool]$IsWarn) {
    if ($Quiet) { return }
    if ([Console]::IsOutputRedirected) {
        # リダイレクト先に制御文字や罫線を混ぜない。
        foreach ($l in $Lines) { Write-Host $l }
        return
    }

    if ($script:UseUnicode) {
        $tl = [string][char]0x2554; $tr = [string][char]0x2557
        $bl = [string][char]0x255A; $br = [string][char]0x255D
        $h  = [string][char]0x2550; $v  = [string][char]0x2551
    } else {
        $tl = '+'; $tr = '+'; $bl = '+'; $br = '+'; $h = '-'; $v = '|'
    }

    $inner = 0
    foreach ($l in $Lines) {
        $w = Get-DisplayWidth $l
        if ($w -gt $inner) { $inner = $w }
    }
    $inner += 4

    if ($script:UseAnsi) {
        if ($IsWarn) { $color = $ESC + '[1;33m' } else { $color = $ESC + '[36m' }
        $reset = $ESC + '[0m'
    } else {
        $color = ''; $reset = ''
    }

    $bar = $h * $inner
    Write-Host ($color + $tl + $bar + $tr + $reset)
    foreach ($l in $Lines) {
        # 行は「│ + 空白2 + 本文 + pad + │」なので pad は inner - 幅 - 2。
        $pad = ' ' * ($inner - (Get-DisplayWidth $l) - 2)
        Write-Host ($color + $v + $reset + '  ' + $l + $pad + $color + $v + $reset)
    }
    Write-Host ($color + $bl + $bar + $br + $reset)
    # warn のときだけベルを鳴らす。
    if ($IsWarn -and $script:UseAnsi) { Write-Host -NoNewline ([string][char]7) }
}

if ($Title.Length -gt 50) { $Title = $Title.Substring(0, 50) }
if ($Message.Length -gt 200) { $Message = $Message.Substring(0, 200) }

$isWarn = ($Level -eq 'warn')
if ($isWarn -and $script:UseUnicode) { $head = ([string][char]0x26A0) + ' ' + $Title } else { $head = $Title }
if ([string]::IsNullOrEmpty($Title)) { $head = 'rdpmanager' }

# ── ペイロード生成 ──
function New-Payload {
    $json = @{ title = $Title; message = $Message; level = $Level } | ConvertTo-Json -Compress
    [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($json))
}

$payload = New-Payload
# A static virtual channel delivers at most 1600 bytes per chunk; the client
# drops split payloads, so shrink the message until it fits in one chunk.
if ($payload.Length -gt 1500) {
    $Message = $Message.Substring(0, [Math]::Min(80, $Message.Length))
    $payload = New-Payload
}

# ── 送信 ──
# RDP セッション内かどうかは SESSIONNAME ではなくチャネルが開けるかで判定する。
# SESSIONNAME は昇格プロセスや一部のホストで未設定になり、誤検知で黙って死ぬため。
$status = ''
$detail = @()
$ok = $false

if ($addTypeError) {
    $status = 'FAILED (init) - Add-Type failed: ' + $addTypeError[0].Exception.Message
} else {
    $handle = [RdpNotify.Wts]::WTSVirtualChannelOpen([IntPtr]::Zero, -1, "CCNOTIF")
    # GetLastWin32Error は Open の直後に取る（間に別の呼び出しを挟むと上書きされる）。
    $err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
    if ($handle -eq [IntPtr]::Zero) {
        $status = "NOT DELIVERED - 'CCNOTIF' unavailable (win32=" + $err + ")"
        $detail = @(
            'Did you reconnect the session in rdpmanager? (the channel is registered at connect time)',
            'Is this terminal inside an RDP session opened by rdpmanager? (plain mstsc is not supported)',
            'Is View > Remote Notifications (Toast) enabled in rdpmanager?'
        )
    } else {
        try {
            $bytes = [System.Text.Encoding]::ASCII.GetBytes($payload)
            $written = 0
            $wrote = [RdpNotify.Wts]::WTSVirtualChannelWrite($handle, $bytes, $bytes.Length, [ref]$written)
            $werr = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
            if (-not $wrote) {
                $status = 'FAILED (write) - win32=' + $werr
            } elseif ($written -ne $bytes.Length) {
                $status = 'FAILED (write) - partial ' + $written + '/' + $bytes.Length
            } else {
                $status = 'delivered'
                $ok = $true
            }
        } finally {
            [void][RdpNotify.Wts]::WTSVirtualChannelClose($handle)
        }
    }
}

# ── 端末表示 ──
$stamp = (Get-Date).ToString('HH:mm:ss')
# 配列要素の連結は括弧で括る（PowerShell では , が + より強く結合し、要素が分裂するため）。
$lines = @($head, ('  ' + $Message), ('  ' + $stamp + '  ->  rdpmanager (' + $status + ')'))
Write-Banner $lines $isWarn
if (-not $ok -and -not $Quiet -and $detail.Count -gt 0) {
    foreach ($d in $detail) { Write-Host ('  * ' + $d) }
}

if ($Diag) {
    Write-Host ''
    Write-Host '--- diagnostics ---'
    Write-Host ('SESSIONNAME     : ' + $env:SESSIONNAME)
    Write-Host ('CLIENTNAME      : ' + $env:CLIENTNAME)
    Write-Host ('PSVersion       : ' + $PSVersionTable.PSVersion.ToString())
    Write-Host ('OutputCodePage  : ' + [Console]::OutputEncoding.CodePage)
    Write-Host ('AnsiSupported   : ' + $script:UseAnsi)
    Write-Host ('UnicodeBoxChars : ' + $script:UseUnicode)
    Write-Host ('Redirected      : ' + [Console]::IsOutputRedirected)
    Write-Host ('PayloadBytes    : ' + $payload.Length)
    Write-Host ('Result          : ' + $status)
}

if ($Strict -and -not $ok) { exit 1 }
exit 0
""";

    private const string HooksSample =
"""
{
  "_comment": "Sample Claude Code hooks for the remote machine. Merge the 'hooks' section into ~/.claude/settings.json on the remote host and replace C:\\path\\to with the actual folder holding rdp-notify.ps1. The script also prints a banner on the remote terminal; add -Quiet to suppress it.",
  "hooks": {
    "Notification": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "powershell -NoProfile -ExecutionPolicy Bypass -File C:\\path\\to\\rdp-notify.ps1 -Title \"Claude Code\" -Message \"Waiting for your input\""
          }
        ]
      }
    ],
    "Stop": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "powershell -NoProfile -ExecutionPolicy Bypass -File C:\\path\\to\\rdp-notify.ps1 -Title \"Claude Code\" -Message \"Task finished\""
          }
        ]
      }
    ]
  }
}
""";
}
