# PreToolUse フック: dotnet build/run/publish の実行前に RdpManager プロセスを kill する。
# 実行中の exe が出力ファイルをロックし、ビルドが MSB3026 で失敗するため(CLAUDE.md の規約を機械的に強制)。
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$cmd = $payload.tool_input.command
if ($cmd -match 'dotnet\s+(build|run|publish)') {
    Get-Process RdpManager -ErrorAction SilentlyContinue | Stop-Process -Force
}
exit 0
