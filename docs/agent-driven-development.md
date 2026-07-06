# エージェント駆動開発の環境

rdpmanager をコーディングエージェント(Claude Code / Codex / Copilot 等)と一緒に開発するための設定一式について説明する。

## 構成

| ファイル/ディレクトリ | 役割 |
| --- | --- |
| `AGENTS.md` | プロジェクトガイドの正本(エージェント横断標準。`CLAUDE.md` はここを `@import` するだけ) |
| `.claude/settings.json` | 定型コマンド(`dotnet build`/`dotnet test` 等)の事前許可と hooks の配線 |
| `.claude/hooks/kill-rdpmanager-before-build.ps1` | `dotnet build`/`run`/`publish` の前に実行中の rdpmanager を自動 kill(MSB3026 ビルドロック防止の機械的強制) |
| `.claude/skills/verify` | 変更後のフル検証(0 警告ビルド → `dotnet test` → `--selftest`)を `/verify` で実行 |
| `.claude/skills/release` | MSI リリース手順(バージョン 2 箇所同期 → publish → WiX → `gh release`)を `/release` で実行 |

## 運用の流れ

- 機能ブランチを作成する
- エージェントに実装を依頼する(`AGENTS.md` がプロジェクト規約として読み込まれる)
- `/verify` でビルド・テスト・selftest を通す
- PR を作成する
- レビューしてマージする

## 設計方針

決定論的に強制したいものは hooks(例: ビルド前に必ず rdpmanager を kill する)、手続き的で複数手順のフローは skills(例: verify、release)、そしてエージェントが毎セッション知っておくべき最小限の知識だけを AGENTS.md に置く、という役割分担にしている。この分担により、AGENTS.md が肥大化せず、機械的に守られるべき制約が「守り忘れ」で崩れることも防げる。

## 今後の候補(未導入)

- **spec-driven development**: `docs/specs/` 配下に要求 → 設計 → タスクの成果物を段階的に置く開発方式(GitHub Spec Kit など)。大きめの機能追加で要件のブレを防ぐのに向くが、単独開発者の小粒な変更が多い現状ではオーバーヘッドが勝る可能性があり保留。
- **`@claude` メンション対応ワークフロー**: Issue や PR コメントで `@claude` とメンションすると対話的に作業させる GitHub Actions ワークフロー。実装依頼をトリガーできるようにする拡張として検討中。
