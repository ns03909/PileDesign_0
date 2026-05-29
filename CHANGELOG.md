# CHANGELOG

PileDesign 杭基礎検討プログラムの変更履歴。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に準拠し、
バージョニングは [Semantic Versioning](https://semver.org/lang/ja/) に従う。

## [Unreleased] — 2026-05-17 〜 2026-05-19 セッション

### 🔧 解析・FEM (Analysis / FEM)

- **VL 単独ケースで X 反力が出る問題** を修正
  - 液状化「あり」設定で `groundDisp1L` の強制変位が VL にも適用され、Chang ばねを介して
    擬似的な X 方向反力が発生していた
  - `InitializeSoilDisplacementIncrement` で VL ケースを早期 return するよう修正
- **P-S 非線形ばねを沈下解析と同じ物理関数で評価** するように刷新
  - 従来の (δ, P) 履歴の線形補間 (`VerticalPileSpringCurve`) に加え、
    沈下解析 `VerticalLoadTransferMethod` の物理関数 (`GetTangentStiffnessPilePerimeter` /
    `GetTangentStiffnessPileToeFromSettlement` 等) を直接呼ぶ新クラス
    [`PileVerticalSoilSpringModel`](Graphics_r1/FEM/PileVerticalSoilSpringModel.cs) を導入
  - 杭体自重を各杭節点に Fz として注入 (沈下解析 `SetWeights()` と同じ式)
  - 杭頭節点 (k=0) の Uz は CapNode の slave、その自重は jointNode に集約することで
    `MapOnGlobalLoad` の 1 段 slave 解決制約を回避
- **計算例9 を X 軸対称化** (杭群重心と AP X 座標の 5mm ずれを 0 に補正)
  - 杭 X 座標: 0.6 / 7.375 / **14.725** / **22.075** / **29.425** / **36.200** (中点 18.400)
  - AP X 座標: 18.43 → **18.400**
- **キャプリング工法 2 例題 (3.7.1, 3.7.3) の地盤データに**
  `isGroundDisplacementIgnored: true` を追加。`GroundExampleLoader.ApplyToGroundInput` で
  地盤変位「考慮しない」モードを反映するように拡張
- **代表節点 (AP) 非表示時に根入部関連の表示も連動して隠す** ように修正
  (3D ボックス / 土圧合力ばね反力)
- **CheckSoilEmbedment の null ガード追加** (`EmbedmentInput == null` 時に
  NRE が出るバグの修正)

### 🎨 UI / UX

- **キーボード操作の全 18 ウィンドウ統一** (Enter / Esc / Alt+X / Tab / Space)
  - モーダルダイアログ: `IsDefault`/`IsCancel` + アクセスキー `(_O)`/`(_C)`/`(_S)` + 初期フォーカス
  - エディタウィンドウ: Enter (IsDefault) + Ctrl+Enter (既存) + Alt+S の三系統
  - 共通スタイル [Styles.xaml](Graphics_r1/Styles.xaml#L170-L205) として再利用化
    (`PrimaryDialogButtonStyle`, `DialogCancelButtonStyle`, `DialogSecondaryButtonStyle`)
- **ファイルメニューに Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+S ショートカット** 追加
  - ボタンラベルに `(Ctrl+N)` 等を明示
  - ファイル メニュー名を `ファイル(F)` に統一 (他リボンタブと整合)
- **ステータスバー右側に「⌨ ショートカット (Ctrl+/)」ボタン** 追加
  - クリック / Ctrl+/ で `ShortcutKeysWindow` を起動
  - パワーユーザー機能 (Ctrl+Shift+P コマンドパレット等) の発見性向上
- **「杭・梁要素 解析結果」トグルボタンを目立たせ** (アクセント色枠 + 太字 + SemiBold)
  - 周囲コントロールと整合した高さ 30 / FontSize 12
- **解析結果 RZ ダイアグラムをバブル表示**
  - `IsBubbleVisible` ON 時、ビュー方向に依存しない真円バブルとして RZ 反力を表示
  - 3D 回転しても読みやすい
- **配置 DataGrid の ComboBox 選択時に値が消える問題** を修正
  - 暗黙の `DataGridCell` スタイル `IsSelected → IsEditing=True` が原因
  - `DataGridTemplateColumn` + 常時 ComboBox 表示に置換 (杭体番号 / 地盤番号)
- **配置 DataGrid で全杭非表示時に反力結果が表示される問題** を修正
  - `visibleSoilSprings.Count > 0` のガードが空セットを「フィルタ無効」と誤判定していた
  - 杭関連ばね / 根入部ばね を別ロジックで判定するヘルパー化
- **水平解析ウィンドウの「VL 単独ケースも解析」チェックでスクロール飛び** 問題を修正
  - `RequestBringIntoView` を `AddHandler(handledEventsToo: true)` で抑制 + オフセット復元
- **ステップ数表示のずれ** (10/8 → 10/10) を修正
  - `TotalCalculationCount` / `TotalLoadCaseCount` に VL 擬似ケース分を加算
- **AnalysisPreflightDialog にアクセスキー + 初期フォーカス** 追加
- **DocxOutputWindow / GroupSettlementWithBeamWindow / MoveCopyWindow** 等のボタン整理
- **「外力・反力サマリー」の細部修正** (土圧合力ばね反力の集計、対称性)

### ➕ 新機能 (Features)

- **コマンドライン引数によるファイル起動** (B2)
  ```
  PileDesign.exe project.json
  PileDesign.exe --open project.json    # -o でも可
  ```
- **ドラッグ&ドロップ対応** (B3) — エクスプローラから .json をドロップしてプロジェクト読込
  - 複数ファイル / 非対応形式時はステータスバーで通知
- **ヘルプに「ファイルを開く方法」セクション** 追加 (D&D / CLI / 関連付け手順)

### 🧪 テスト・品質 (Tests / Quality)

- **収束リグレッションテスト基盤** を新規構築 ([TestProject1/ConvergenceRegression/](TestProject1/ConvergenceRegression/))
  - 本番 `HorizontalCalculationViewModel` を `BypassUiPromptsForTesting=true` で headless 実行
  - 4 例題 (Example9 / Example3_5 / Example10 / ExampleK8) で per-case 反復数 / 収束フラグ /
    残差 / 物理量 (AP 変位 + 最大反力) をスナップショット化
  - 退化判定: 反復数 +20 or ×1.50 / 物理量 ±5%
  - `UPDATE_SNAPSHOTS=1` 環境変数で 5 回実行 max を採用しスナップショット再生成
- **並列実行決定性テスト** — MDOP=1/2/4 で per-case 反復数完全一致を検証 (Example9 + K8)
- **`PileVerticalSoilSpringModel` ユニットテスト** (16 件) — τ-s / R-S 物理関数の正定値性 /
  状態遷移 / 割線=接線関係
- **`VLPseudoCaseTests` (8 件)** — VL 機能の有効化条件 / TotalCalculationCount 加算 /
  setter 通知
- **`LoadCasesInputExtraTests` (10 件) + `ElementDivisionExtraTests` (6 件)** —
  派生 collection の filter 仕様
- **`HelpCoverageTests` (3 件)** — XAML 主要ラベル (リボンタブ + ウィンドウ Title +
  最近追加機能) が help.html に記載されているかを自動検査
- **`SaveLoadRoundTripTests` 拡充** (4 → 14 例題 + 新規 schema フィールド)
- **GitHub Actions CI** ([.github/workflows/build-test.yml](.github/workflows/build-test.yml))
  - push / PR / 手動実行で build + 全テスト (~800 件) 自動実行
  - 失敗時 trx を 14 日間アーティファクト保持
  - concurrency 制御でコスト節約

### 🗑️ 削除 (Removed)

- **`PileDesignCore/` プロトタイプ削除** (100 .cs files、.sln 未登録の古いフォーク)
- **`PileDesign.Cli/` プロトタイプ削除** (5 .cs files、HeadlessAnalysisRunner は
  TestProject1 の `HeadlessHorizontalRunner` で代替)
- **`PileDesign.Mcp/` プロトタイプ削除** (6 .cs files、UI 未バインドの orphan)
- **`MainWindowViewModel.RegisterMcpServerCommand`** + `FindClaudeDesktopConfigPath` 削除
  (UI 未バインド)

### 📈 統計

| 項目 | Before | After | Δ |
|---|---|---|---|
| テスト数 | 752 | **827** | +75 |
| .cs ファイル数 | ~700 | ~590 | -111 (prototype 削除) |
| TestProject1 サイズ (.cs) | 33 | **37** | +4 |
| プロジェクト | 5 (内 3 が orphan) | **3** (Graphics_r1, TestProject1, BenchmarkSuite1) | クリーン化 |

### 📚 ドキュメント (Docs)

- **CHANGELOG.md 新規作成** (本ファイル)
- **help.html 更新**
  - 「ファイルを開く方法」セクション (D&D / CLI / 関連付け手順)
  - ダイアログ共通操作 6 行追加 (Enter / Esc / Alt / Tab / Space)
  - ショートカット一覧にステータスバーボタンの説明追記
- **[TestProject1/ConvergenceRegression/README.md](TestProject1/ConvergenceRegression/README.md)**
  新規追加 (収束リグレッションテストの使い方)

---

## 過去のリリース (git log より)

`git log` で参照可能。代表的なマイルストーン:

- `7b8bd2f` 水平解析 76% 高速化 / docx 出力 5× 高速化
- `83f07f1` UI 全面リニューアル: Fluent.Ribbon 移行
- `12cacee` 水平解析強化 + Undo DeepCopy 8-9× 高速化
- `47838d2` キャプリングパイル工法を実装
- `e6cb4f5` 鋼管杭+鉄筋定着工法
- `2711581` v22-v26 非線形収束修正の最小再現テスト 6 件追加
- `5728626` E.18 結果ダッシュボード + C.9 コマンドパレット
