# RdpManager

接続先を整理し、Windows 標準の RDP クライアント（`mstsc.exe`）でワンクリック接続する軽量な RDP 接続マネージャ（WPF / .NET）。

「自分専用 RDCMan」をコンセプトに、接続先をツリーで整理し、資格情報を安全に保存して即接続することを目的とした個人〜小チーム向けツールです。

## 特長

- 接続ツリー（フォルダ自由ネスト）で接続先を整理
- 接続・フォルダの追加 / 編集 / 削除（CRUD）
- 接続定義を JSON で永続化（`%APPDATA%\RdpManager\connections.json`）
- 資格情報を **DPAPI**（CurrentUser）で暗号化保存（平文保存なし）
- 資格情報の継承（直接入力 / プロファイル / 親フォルダから継承）
- インクリメンタル検索
- クイック接続（未登録ホストへその場で接続）
- 接続は Windows 標準 `mstsc.exe` を起動（OS 純正の RDP スタックを使用）。資格情報は Win32 `CredWrite` API で Windows 資格情報マネージャーへ直接登録し、`.rdp` に平文パスワードを書かず、コマンドライン引数にも露出させません（ログオフで自動消去されるセッションスコープ）

## 動作環境

- Windows 10 / 11
- 実行: MSI インストーラ版はランタイム同梱（self-contained）。ソースからの実行には .NET 10 SDK が必要

## インストール

[Releases](../../releases) から `RdpManager-x.y.z.msi` をダウンロードして実行してください。スタートメニューに「RdpManager」が登録されます。

## ソースからのビルド

```powershell
git clone https://github.com/ntaksh42/rdp-manager.git
cd rdp-manager
dotnet build
dotnet run --project src/RdpManager
```

## 使い方

- ツリーの接続を**ダブルクリック**、または選択して「▶ 接続」で mstsc を起動
- 「🖥️ 新規接続」でホスト・ポート・資格情報・ゲートウェイ・各種設定を登録
- 上部検索ボックスで絞り込み、右上のクイック接続で未登録ホストへ即接続

## 更新の確認

メニュー「ヘルプ → 更新を確認」で GitHub Releases の最新版と比較し、新しいバージョンがあればダウンロードページへ案内します。

## コードサイニング（任意）

MSI は既定では未署名のため SmartScreen 警告が出ます。コードサイニング証明書（.pfx）をお持ちの場合は以下で署名できます（証明書が必要、自己署名では警告は消えません）。

```powershell
installer\sign.ps1 -Msi dist\RdpManager-x.y.z.msi -Pfx <cert.pfx> -Password <pfxパスワード>
```

## セキュリティ上の注記

- パスワードは DPAPI で暗号化され、同一 Windows ユーザー・同一マシンでのみ復号できます（別環境へ移行時は再入力が必要）
- 接続時の資格情報は `CredWrite` API で資格情報マネージャーへ直接登録するため、コマンドライン引数には露出しません。登録はセッションスコープ（ログオフで自動消去）です

## ライセンス

[MIT](LICENSE)

## 設計ドキュメント

詳細な仕様・競合調査・実装方針は [docs/仕様たたき台.md](docs/仕様たたき台.md) を参照。
