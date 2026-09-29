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

値はコード ([ConvergenceRegressionTests.cs](ConvergenceRegressionTests.cs) の定数) が正です。ここを変えたら表も直してください
(2026-09-30 に見直したとき、表は反復数を「+10 / ×1.10」、物理量を 1% と書いたまま、コードは +20 / ×1.50・5% になっていました)。

| 項目                    | 比べる値                                          | 許容                                                        |
| ----------------------- | ------------------------------------------------- | ----------------------------------------------------------- |
| 反復数                  | ケースごとの反復の合計                            | 絶対 +20 以下 **または** 比率 ×1.50 以下                    |
| 収束フラグ              | ケースが収束したか                                | 完全一致 (収束→未収束 は即 fail)                            |
| 残差                    | 最終ステップの ‖R‖/‖F‖                             | ×10 まで許容 (両方とも < 1e-3 の "well-converged" 時は無視) |
| 変位                    | 代表点 (作用点) の 6 成分・全節点の最大水平変位   | 相対 5% (両方とも 1e-9 未満なら比べない)                    |
| 反力                    | 水平地盤ばねの最大水平反力                        | 相対 5% (同上)                                              |
| 断面力 (検定値を決める) | 杭要素の最大曲げモーメント (両端の My・Mz の合成) | 相対 5% (同上)。記録を持つスナップショットだけ比べる        |

物理量の 5% は、非線形の反復 (line search の経路) で 3〜4% 動くケースがあるための幅です。10% を超える退化は捕まえます。

## 数値計算を変えたときに比べるもの

解析結果を変えうる変更 (解法・材料則・断面積分・荷重の組み立て・計算の順序) は、次の網をすべて通してから入れます。
どれも代表の例題 (設計例集・基礎指針の計算例) を解いて比べます。

| 網                                                                                     | 比べるもの                                       | 許容・計算の順序                                                                        |
| -------------------------------------------------------------------------------------- | ------------------------------------------------ | --------------------------------------------------------------------------------------- |
| 収束の回帰 (このフォルダ)                                                              | 反復数・収束・残差・変位・反力・曲げモーメント   | 上の表。逐次 (`Parallelism=1`) で解く                                                   |
| 並列の決定性 ([ParallelDeterminismTests.cs](ParallelDeterminismTests.cs))             | 同じ入力を 2 回解いた変位・反力・曲げモーメント | **ビット一致**。並列に集めた寄与の加算順を固定しているので、順序が変われば落ちる       |
| 検定の文 ([EvaluationTextGoldenTests.cs](../EvaluationTextGoldenTests.cs))            | 検定値 (M/Mu・Q/Qu など) を含む検定の全文        | **完全一致** (表示の桁まで)。式をまとめ直して丸めの順が変わると 0.1 動いて落ちる        |
| 出口の不変条件 ([AnalysisOutputInvariantTests.cs](../AnalysisOutputInvariantTests.cs)) | 荷重の反転・2 倍に対する応答 (線形域)            | 相対 2% (反復の打ち切り誤差)。正解の値が無くても「最初から間違っていたもの」を捕まえる |

意図して結果を変えるときは、スナップショット・golden を取り直し、差分に意図した変更だけが入っているかを確かめてから入れます。

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

書き出す先は**ビルドの出力フォルダ** (`TestProject1/bin/.../ConvergenceRegression/Snapshots/`) です。ソースの
`Snapshots/*.json` へは手で写します。反復数は 5 回解いた最大を採るので、丸ごと写すと反復数も動くことがあります。
比べる項目を足しただけのときは、足した項目だけを写し、ほかの値が動いていないことを確かめてください
(2026-09-30 に曲げモーメントを足したときはこの形で写し、既存の変位・反力が動いていないことを確かめた)。
**git diff で意図した変更だけが入っているか必ず確認** してから commit。

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
