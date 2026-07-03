# RdpManager

接続先をツリーで整理し、RDP セッションを**ウィンドウ内のタブに埋め込み表示**する軽量な RDP 接続マネージャ（WPF / .NET）。外部 `mstsc.exe` 起動や SSH / Telnet / VNC の外部クライアント起動にも対応。

「自分専用 RDCMan」をコンセプトに、接続先をツリーで整理し、資格情報を安全に保存して即接続することを目的とした個人〜小チーム向けツールです。

## 特長

- 接続ツリー（フォルダ自由ネスト）で接続先を整理
- 接続・フォルダの追加 / 編集 / 削除（CRUD）
- 接続定義を JSON で永続化（`%APPDATA%\RdpManager\connections.json`）
- 資格情報を **DPAPI**（CurrentUser）で暗号化保存（平文保存なし）
- 資格情報の継承（直接入力 / プロファイル / 親フォルダから継承）
- インクリメンタル検索
- Quick Switch: グローバルホットキー（既定 Ctrl+Alt+Home、変更可）で全接続をインクリメンタル検索して即切替
- リモート通知: 接続先からの通知を RDP 仮想チャネル経由で受け取りトースト表示（[詳細](docs/remote-notifications.md)）
- 通常はウィンドウ内タブに埋め込み表示（mstscax ActiveX）で接続。「Open in External Window」選択時のみ Windows 標準 `mstsc.exe` を外部起動し、資格情報は Win32 `CredWrite` API で Windows 資格情報マネージャーへ直接登録して `.rdp` に平文パスワードを書かず、コマンドライン引数にも露出させません（ログオフで自動消去されるセッションスコープ）

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

- ツリーの接続を**ダブルクリック**（または Enter / 「▶ Connect」）でウィンドウ内タブに接続を表示
- 右クリック → 「Open in Right Pane (Split)」で左右分割表示、「Open in External Window」で外部 mstsc 起動
- 「🖥️ 新規接続」でホスト・ポート・資格情報・ゲートウェイ・各種設定を登録
- 上部検索ボックスで絞り込み
- タブは**中クリック**または右クリックメニューから閉じられます。同じ接続を再度開くと既存タブを前面に表示（切断中なら再接続）
- ウィンドウの位置・サイズと開いていたセッションは次回起動時に復元されます

### キーボードショートカット

| キー | 動作 |
| --- | --- |
| Enter（ツリー上） | 選択中の接続を開く |
| F2 / Del（ツリー上） | 編集 / 削除 |
| Ctrl+N / Ctrl+Shift+N | 新規接続 / 新規フォルダ |
| Ctrl+D | 複製 |
| Ctrl+F | 検索ボックスへフォーカス（Esc でクリア） |
| Ctrl+W | 現在のタブを閉じる |
| F11 / Ctrl+Alt+Pause | 全画面切替（RDP フォーカス中も有効。Esc でも解除可） |
| Ctrl+Alt+PageUp / PageDown | タブ巡回（RDP フォーカス中も有効） |
| Ctrl+Alt+1〜9 | タブ番号ジャンプ（RDP フォーカス中も有効） |
| Ctrl+Alt+Home（変更可） | Quick Switch（接続の検索・切替。RDP フォーカス中も有効） |

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
