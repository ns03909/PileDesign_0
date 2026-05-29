# 収束リグレッションテスト

水平解析の収束挙動 (反復数 / 収束フラグ / 残差) を例題ごとにスナップショット化し、コード変更で退化していないかを自動検証する。

## 目的

v22〜v29 の収束改善パッチ群のように、ある例題の収束が改善した一方で別例題が退化するケースは頻発する。
このテストでは「全例題の per-case 反復数表」を JSON として固定し、毎ビルドで差分検出する。

## 動作仕組み

1. [HeadlessHorizontalRunner](HeadlessHorizontalRunner.cs) が **本番の `HorizontalCalculationViewModel`** を `BypassUiPromptsForTesting=true` で実行
2. 解析後 `HCVM.StepSummariesSnapshot()` で `StepSummary` を取得
3. `(Level, LoadCaseNo, ComboNo, IsLiquefaction)` ごとに集計 → [ConvergenceSnapshot](ConvergenceSnapshot.cs)
4. 既存スナップショット (`Snapshots/{ExampleName}.json`) と比較、退化があれば fail

## 退化判定基準

| 項目       | 許容                                                        |
| ---------- | ----------------------------------------------------------- |
| 反復数     | 絶対 +10 以下 **または** 比率 ×1.10 以下                    |
| 収束フラグ | 完全一致 (収束→未収束 は即 fail)                            |
| 残差       | ×10 まで許容 (両方とも < 1e-3 の "well-converged" 時は無視) |

## 通常テスト実行

```powershell
dotnet test TestProject1 --filter "FullyQualifiedName~ConvergenceRegression"
```

退化があれば fail。例:

```text
[Example9] L2-1.C1.Liq: 反復数退化 12 → 47 (+35, ×3.92) 許容: +10 or ×1.10
```

## スナップショット更新 (意図的改善 / 新規例題追加時)

```powershell
$env:UPDATE_SNAPSHOTS = "1"; dotnet test TestProject1 --filter "FullyQualifiedName~ConvergenceRegression"
```

これで `Snapshots/*.json` が現在の挙動に更新される。**git diff で意図した変更だけが入っているか必ず確認** してから commit。

## 例題追加

[ConvergenceRegressionTests.cs](ConvergenceRegressionTests.cs) の `[DataRow]` を追加:

```csharp
[DataRow("Example3_5", "PileExample3_5", 4, 16)]
[DataRow("ExampleK8", "PileExampleK8", 4, 8)]
```

その後 `UPDATE_SNAPSHOTS=1` で再生成。

## 現行カバレッジ (2026-05-19 時点)

| 例題                            | L1 反復/ステップ | L2 反復/ステップ | 残差   |
| ------------------------------- | ---------------- | ---------------- | ------ |
| Example9   (基礎指針'19 #9)     | 15 / 4           | 53 / 8           | 1e-23  |
| Example3_5 (設計例集 鋼管杭)    | 22 / 4           | 113 / 16         | 4e-7   |
| Example10  (基礎指針'19 #10)    | 26 / 4           | 159 / 16         | 4e-7   |
| ExampleK8  (関東支部 計算例8)   | 49 / 4           | 97 / 8           | 3e-7   |

全 4 例題で `ForceNonLinear=true` (M-φ / line search / bisection 経路を経由) で実測。
4 ケース合計テスト時間 ~10 秒。

## 既知の制限

### 並列実行は非決定的

- `Parallelism=1` (逐次) 固定。並列実行の収束差は別テストで扱う

### スナップショット差分の解釈

- マシン依存の数値ドリフト (MKL のバージョン違いなど) で残差が ±1 桁変わることはある → 許容範囲を ×10 と緩めに設定
- 反復数は環境非依存 (同じ K 行列・同じ NR 経路なら確定的)

### 例題ごとの LoadCase 数が少ない (1L1 + 1L2)

- `BuildExampleInputModel` で作る `InputModel` は各レベル 1 ケースのみ → カバー範囲が限定的
- 複数組合せ (Counter-Loading 等) の退化を捉えるには、追加で LoadCase / LoadCombination を増やす必要あり (未対応)
