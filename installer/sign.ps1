# MSI コードサイニング用スクリプト
#
# コードサイニング証明書（.pfx）が必要です。証明書は購入（DigiCert / Sectigo 等）
# またはエンタープライズ CA から発行してください。自己署名証明書では SmartScreen 警告は消えません。
#
# 使い方:
#   .\sign.ps1 -Msi ..\dist\RdpManager-x.y.z.msi -Pfx C:\path\to\cert.pfx -Password <pfxパスワード>
#
param(
    [Parameter(Mandatory = $true)][string]$Msi,
    [Parameter(Mandatory = $true)][string]$Pfx,
    [Parameter(Mandatory = $true)][string]$Password,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

# signtool は Windows SDK に含まれます（未導入なら Windows SDK をインストール）
$signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
if (-not $signtool) {
    $signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Select-Object -Last 1 -ExpandProperty FullName
}
if (-not $signtool) { throw "signtool.exe が見つかりません。Windows SDK をインストールしてください。" }

& $signtool sign /f $Pfx /p $Password /fd SHA256 /tr $TimestampUrl /td SHA256 $Msi
if ($LASTEXITCODE -ne 0) { throw "署名に失敗しました。" }
& $signtool verify /pa $Msi
Write-Host "署名完了: $Msi"
