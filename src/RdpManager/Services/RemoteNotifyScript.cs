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
    private const string Script =
"""
<#
rdpmanager remote notification sender.

Sends a notification to the rdpmanager client over the RDP static virtual
channel "CCNOTIF". Does nothing (exit 0) outside an RDP session or when the
RDP client did not register the channel, so it is always safe to call.

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File rdp-notify.ps1 `
    -Message "text" [-Title "title"] [-Level info|warn]
#>
param(
    [Parameter(Mandatory = $true)][string]$Message,
    [string]$Title = "",
    [ValidateSet("info", "warn")][string]$Level = "info"
)

# Not inside an RDP session (console logon etc.) -> nothing to notify.
if ($env:SESSIONNAME -and $env:SESSIONNAME -notlike "RDP-*") { exit 0 }

Add-Type -Namespace RdpNotify -Name Wts -MemberDefinition @"
[DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
public static extern IntPtr WTSVirtualChannelOpen(IntPtr hServer, int SessionId, string pVirtualName);
[DllImport("wtsapi32.dll", SetLastError = true)]
public static extern bool WTSVirtualChannelWrite(IntPtr hChannel, byte[] Buffer, int Length, out int pBytesWritten);
[DllImport("wtsapi32.dll")]
public static extern bool WTSVirtualChannelClose(IntPtr hChannel);
"@

if ($Title.Length -gt 50) { $Title = $Title.Substring(0, 50) }
if ($Message.Length -gt 200) { $Message = $Message.Substring(0, 200) }

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

$handle = [RdpNotify.Wts]::WTSVirtualChannelOpen([IntPtr]::Zero, -1, "CCNOTIF")
if ($handle -eq [IntPtr]::Zero) { exit 0 }  # channel not registered by the client (not rdpmanager)
try {
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($payload)
    $written = 0
    [void][RdpNotify.Wts]::WTSVirtualChannelWrite($handle, $bytes, $bytes.Length, [ref]$written)
} finally {
    [void][RdpNotify.Wts]::WTSVirtualChannelClose($handle)
}
exit 0
""";

    private const string HooksSample =
"""
{
  "_comment": "Sample Claude Code hooks for the remote machine. Merge the 'hooks' section into ~/.claude/settings.json on the remote host and replace C:\\path\\to with the actual folder holding rdp-notify.ps1.",
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
