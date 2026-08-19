# .NET Version Upgrade

## Preferences
- **Flow Mode**: Automatic
- **Target Framework**: net10.0
- **Execution Scope**: Assessment + planning only (no code changes)

## Source Control
- **Source Branch**: main
- **Working Branch**: upgrade-dotnet-10
- **Commit Strategy**: After Each Task
- **Branch Sync**: Auto (Merge)

## Upgrade Options
**Source**: .github/upgrades/scenarios/dotnet-version-upgrade/upgrade-options.md

### Strategy
- Upgrade Strategy: Top-Down

### Project Structure
- Package Management: Per-Project (defer CPM to post-migration)

### Compatibility
- Unsupported Packages: Defer Resolution (8 incompatible packages)
- Unsupported API Handling: Fix Inline

## Strategy
**Selected**: Top-Down (Application-First)
**Rationale**: 3プロジェクト構成で、依存先の中核である PileDesign を先に移行し、追従プロジェクトを段階適用することで互換性リスクを分離できるため。

### Execution Constraints
- PileDesign を先行し、依存する TestProject1 / BenchmarkSuite1 は後続で順次対応する。
- 互換性を優先し、構造変更や設計変更は行わず、TFM・パッケージ・必要最小限のAPI修正に限定する。
- 互換パッケージ未確定項目はビルド継続性を優先して段階解決（Defer Resolution）とする。
- 各タスク完了時にビルド/テストで回帰確認し、次タスクへ進む。
- 最終タスクで全体ビルド・テスト・保留項目（CPM導入など）を整理する。

## User Preferences
### Execution Style
- 破壊的変更は避ける（互換性最優先）

## Key Decisions Log
- 2026-07-22: ユーザー選択により、まず評価のみ実施（コード変更なし）。
- 2026-07-22: 破壊的変更を避ける最小変更プランを優先する。
- 2026-07-22: 実行は開始せず、最小変更の実行計画（plan/tasks）の作成まで実施する。
