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
| `rdp-notify.ps1` | 通知送信スクリプト本体（依存ゼロ、PowerShell 5.1 で動作）。リモート側の端末にもバナーを表示する |
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
powershell -NoProfile -ExecutionPolicy Bypass -File C:\tools\rdp-notify.ps1 -Message "test" -Diag
```

スクリプトは端末にバナーを出し、3行目に配信結果を併記します。**トーストが出ないときも、この行を見れば理由が分かります。**

```
╔══════════════════════════════════════════╗
║  Claude Code                             ║
║    Task finished                         ║
║    20:41:02  ->  rdpmanager (delivered)  ║
╚══════════════════════════════════════════╝
```

`(delivered)` なら送信成功です。rdpmanager のタブを別のものに切り替えるかウィンドウを非アクティブにしておくと（通知元タブを見ている間は抑制されるため）、クライアント側にトーストが出ます。

`-Diag` を付けると `SESSIONNAME` / `CLIENTNAME` / ANSI 可否 / ペイロード長などの環境情報も出力されます。

## トラブルシューティング

配信できなかった場合は `NOT DELIVERED - 'CCNOTIF' unavailable (win32=<番号>)` と原因候補が表示されます。

| win32 | 意味 | 対処 |
| --- | --- | --- |
| 1722 | RPC サーバーが利用不可 | RDP セッション外で実行している。rdpmanager 経由の RDP セッション内で実行する |
| 2250 | ネットワーク接続が存在しない | セッションが切れている。接続し直す |
| その他 | チャネル未登録など | **rdpmanager で接続し直す**（最も多い原因。下記参照） |

**最も多い原因は再接続忘れです。** 静的仮想チャネルは接続時にのみ登録されるため、rdpmanager を更新・再起動した場合は**セッションを開き直さないとチャネルが存在しません**。以前は通知が来ていたのに来なくなった場合、まずこれを疑ってください。

## 知っておくと良い挙動

- スクリプトは **RDP セッション外（コンソールログオンや SSH 経由）では、理由を1行表示して正常終了（exit 0）**します。rdpmanager 以外の RDP クライアント（素の mstsc 等）で接続中もチャネルが開けず同様です。**失敗しても exit code は 0 のまま**なので、フックに常設してもどの環境でも害がありません
- RDP セッション内かどうかは `SESSIONNAME` ではなく**チャネルが実際に開けるか**で判定します（`SESSIONNAME` は昇格プロセスや一部の端末で未設定になり、誤検知するため）
- 静的仮想チャネルは接続時に登録されるため、通知が効くのは **rdpmanager（対応バージョン）で接続し直した後**です
- `-Title` / `-Message` は自由に変えられます。`-Level warn` を付けるとトーストのタイトルに ⚠ が付き、端末のバナーも黄色＋ベル音になります

### スイッチ一覧

| スイッチ | 効果 |
| --- | --- |
| `-Level warn` | ⚠ 付き・黄色バナー・ベル音 |
| `-Quiet` | 端末への表示を一切行わない（送信のみ） |
| `-Diag` | 環境情報をダンプする（切り分け用） |
| `-Strict` | 配信失敗時に exit 1 を返す（手動デバッグ用。フックでは使わないこと） |

端末表示は環境に応じて自動的に劣化します: 色付きボックス → 色なしボックス → ASCII 罫線（`+ - |`）→ **リダイレクト時はプレーンテキスト1行ずつ**（制御文字を混ぜない）。`NO_COLOR` 環境変数でも色を抑制できます。

## プロトコル仕様（他ツールから送る場合）

`rdp-notify.ps1` を使わずに自前で送ることもできます。

- チャネル名: `CCNOTIF`（静的仮想チャネル）。リモート側から `WTSVirtualChannelOpen` / `WTSVirtualChannelWrite` で書き込む
- ペイロード: `Base64(UTF-8 JSON)`。素の JSON も受理される。`OnChannelReceivedData` はチャネルの生バイト列を「2 バイト = 1 文字」で BSTR に詰めて渡すため、クライアント側は文字列としての解釈に失敗した場合に UTF-16 コード単位をバイト列へ戻して再解釈する。マルチバイト文字を確実に通すため Base64 を推奨
- JSON スキーマ: `{"title": "...", "message": "...", "level": "info" | "warn"}`（`message` 必須。`title` 省略時はセッション名を表示）
- サイズ制限: 静的チャネルの1チャンク上限（1600 バイト）を超えると分割されて破棄される。1書き込み 1500 文字以内に収めること
- 解釈できないデータは黙って破棄される（リモート側の任意プロセスが同チャネルへ書き込めるため）
