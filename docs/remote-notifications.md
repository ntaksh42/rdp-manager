# リモート通知（Remote Notifications）

RDP 接続先（リモート側）から任意の通知を送り、クライアント側で Windows トースト通知として受け取る機能です。主な用途はリモートで動かしている Claude Code の「入力待ち / 応答完了」に気づくことですが、ビルド完了通知など任意の用途に使えます。

通知は **RDP 静的仮想チャネル `CCNOTIF`** で既存の RDP 接続内をトンネルします。外部サービス・追加ポートの開放・ファイアウォール設定は不要です。

## クライアント側（rdpmanager）の動作

- 受信した通知を Windows トーストとして表示します（どの接続からの通知かをセッション名で表示）
- **トーストをクリックすると rdpmanager が前面化し、通知元のタブへジャンプ**します
- 通知元タブを表示中（ウィンドウがフォアグラウンド）のときは通知を出しません
- View → **Remote Notifications (Toast)** で ON/OFF できます（既定 ON）

## リモート側のセットアップ

リモート機ごとに1回だけ行います。インストール作業や管理者権限は不要です。

### 1. ファイルをエクスポートして持ち込む

クライアント側で File → **Export Remote Notification Script…** を実行すると、次の2ファイルが保存されます。

| ファイル | 内容 |
| --- | --- |
| `rdp-notify.ps1` | 通知送信スクリプト本体（依存ゼロ、PowerShell 5.1 で動作） |
| `claude-hooks-sample.json` | Claude Code フック設定のサンプル |

RDP のクリップボードまたはドライブリダイレクトでリモート機にコピーします。置き場所は任意です（以下では `C:\tools\rdp-notify.ps1` とします）。

### 2. Claude Code のフック設定をマージ

リモート機の `~/.claude/settings.json`（`C:\Users\<ユーザー>\.claude\settings.json`）に、サンプルの `hooks` セクションを追記します。スクリプトのパスは実際の配置先に直してください。

```json
{
  "hooks": {
    "Notification": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "powershell -NoProfile -ExecutionPolicy Bypass -File C:\\tools\\rdp-notify.ps1 -Title \"Claude Code\" -Message \"Waiting for your input\""
          }
        ]
      }
    ],
    "Stop": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "powershell -NoProfile -ExecutionPolicy Bypass -File C:\\tools\\rdp-notify.ps1 -Title \"Claude Code\" -Message \"Task finished\""
          }
        ]
      }
    ]
  }
}
```

既に `hooks` がある場合は `Notification` / `Stop` の配列に要素を足す形でマージします。設定はリモート側 Claude Code の次セッションから有効です。

### 3. 動作確認

rdpmanager からそのホストに接続した状態で、リモート側のターミナルから手動実行します。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File C:\tools\rdp-notify.ps1 -Message "test"
```

rdpmanager のタブを別のものに切り替えるかウィンドウを非アクティブにしておくと（通知元タブを見ている間は抑制されるため）、クライアント側にトーストが出れば成功です。

## 知っておくと良い挙動

- スクリプトは **RDP セッション外（コンソールログオンや SSH 経由）では何もせず正常終了**します。rdpmanager 以外の RDP クライアント（素の mstsc 等）で接続中もチャネルが開けず no-op です。フックに常設してもどの環境でも害がありません
- 静的仮想チャネルは接続時に登録されるため、通知が効くのは **rdpmanager（対応バージョン）で接続し直した後**です
- `-Title` / `-Message` は自由に変えられます。`-Level warn` を付けるとトーストのタイトルに ⚠ が付きます

## プロトコル仕様（他ツールから送る場合）

`rdp-notify.ps1` を使わずに自前で送ることもできます。

- チャネル名: `CCNOTIF`（静的仮想チャネル）。リモート側から `WTSVirtualChannelOpen` / `WTSVirtualChannelWrite` で書き込む
- ペイロード: `Base64(UTF-8 JSON)`。素の JSON も受理される。`OnChannelReceivedData` はチャネルの生バイト列を「2 バイト = 1 文字」で BSTR に詰めて渡すため、クライアント側は文字列としての解釈に失敗した場合に UTF-16 コード単位をバイト列へ戻して再解釈する。マルチバイト文字を確実に通すため Base64 を推奨
- JSON スキーマ: `{"title": "...", "message": "...", "level": "info" | "warn"}`（`message` 必須。`title` 省略時はセッション名を表示）
- サイズ制限: 静的チャネルの1チャンク上限（1600 バイト）を超えると分割されて破棄される。1書き込み 1500 文字以内に収めること
- 解釈できないデータは黙って破棄される（リモート側の任意プロセスが同チャネルへ書き込めるため）
