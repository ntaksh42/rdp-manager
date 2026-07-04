---
name: verify
description: RdpManager の変更をフル検証する(プロセス kill → 0警告ビルド → ユニットテスト → RDP 埋め込みの --selftest)。コード変更後・コミット前に使う。
---

# RdpManager 検証手順

以下を順に実行し、すべての結果をまとめて報告する。途中で失敗しても後続を打ち切らず、失敗内容を含めて報告する。

1. 実行中プロセスの kill(ビルドロック MSB3026 の防止):
   `Get-Process RdpManager -ErrorAction SilentlyContinue | Stop-Process -Force`
2. ビルド(このリポジトリは 0 警告が規約): `dotnet build -warnaserror`
3. ユニットテスト: `dotnet test --no-build`
4. RDP ActiveX 埋め込みのヘッドレス自己テスト(UI/COM 部分はユニットテスト対象外のためこれが一次検証):
   `src/RdpManager/bin/Debug/net10.0-windows*/RdpManager.exe --selftest` を実行
   (TFM ディレクトリ名はビルド設定で変わるためワイルドカードで解決する)
   → 終了後に `$env:TEMP\rdpmanager_selftest.txt` の内容を確認して OK/NG を報告
5. UI(XAML・テーマ・フォーカス周り)に影響する変更の場合は、自動検証できない旨と、手動スモークテストすべき操作を報告に含める
