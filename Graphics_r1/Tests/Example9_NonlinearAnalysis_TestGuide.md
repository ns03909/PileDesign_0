# Example 9 非線形解析 テストガイド

## 概要
このガイドは、Example 9 (基礎指針'19 計算例9) で非線形解析 (IsPileNonLinear=true) が正しく動作することを検証する手順を説明します。

## 修正されたバグ
以下の4つのバグが修正されました:

1. **IY/IZ交換バグ** ([HorizontalCalculationViewModel.cs:1182-1184](../ViewModels/HorizontalCalculationViewModel.cs#L1182-L1184))
   - `UpdateBeamMPhiSecant`メソッドで、IYとIZが逆に割り当てられていた
   - 修正前: `EI0y = E * IZ`, `EI0z = E * IY`
   - 修正後: `EI0y = E * IY`, `EI0z = E * IZ`

2. **剛性比率下限が低すぎる** ([HorizontalCalculationViewModel.cs:1148, 1187](../ViewModels/HorizontalCalculationViewModel.cs#L1148))
   - セカント剛性比率の下限が 0.01% (1e-4) だった → 1% (0.01) に変更
   - 過度な剛性低下を防止

3. **反復回数の上限がない** ([HorizontalCalculationViewModel.cs:976](../ViewModels/HorizontalCalculationViewModel.cs#L976))
   - Newton-Raphson法の反復ループに上限がなかった
   - 修正: 最大100回の反復回数制限を追加

4. **収束失敗の警告がない** ([HorizontalCalculationViewModel.cs:1060-1065](../ViewModels/HorizontalCalculationViewModel.cs#L1060-L1065))
   - 収束しなかった場合の警告メッセージを追加
   - ログとユーザー通知の両方を実装

## テスト手順

### 前提条件
- プロジェクトがビルドされていること
- PileDesign.exeが実行可能であること

### ステップ1: Example 9の読み込み

1. PileDesignアプリケーションを起動
2. メインメニューから **「Example 9」** (基礎指針'19 計算例9) を選択
3. データが読み込まれることを確認

### ステップ2: 要素分割

1. メニューまたはツールバーから **「要素分割ウィンドウ」** を開く
2. **「自動要素分割」** ボタンをクリック
3. 分割が完了したら、ウィンドウを閉じる

### ステップ3: 水平解析の実行

1. **「水平解析ウィンドウ」** を開く
2. 以下を確認:
   - **「レベル2地震 1方向」** にチェックが入っている
   - `IsPileNonLinear = true` が設定されている
3. **「解析実行」** ボタンをクリック
4. 解析が実行されることを確認

### ステップ4: 結果の検証

#### ✅ 成功基準

1. **解析が完了する**
   - エラーダイアログが表示されない
   - 「解析完了」のメッセージが表示される

2. **変位が閾値以下**
   - すべての節点の変位が **1.0m未満** であること
   - 結果テーブルまたはグラフで変位を確認

3. **反復回数が制限内**
   - ログに「Maximum iterations 100 reached」が頻繁に出ない
   - 出現した場合でも、解析が停止し、無限ループにならない

4. **収束警告の確認** (出る場合)
   - ログに「Warning: Maximum iterations 100 reached」のメッセージ
   - または「Convergence failed」のダイアログ
   - → これらは修正が機能している証拠

#### ❌ 失敗の兆候

- 変位が1.0m以上になる
- アプリケーションがフリーズする (無限ループ)
- 解析が異常終了する
- 収束しないまま処理が続く

## 期待される動作

### 修正前の動作 (バグあり)
- 杭頭のM-θ関係が非線形の場合、Mが非常に小さくなる
- 大回転が発生
- 杭の変形が1m以上になり、解析が停止する

### 修正後の動作 (バグ修正後)
- IY/IZが正しく適用される → 正しい剛性計算
- 剛性比率の下限が1% → 過度な剛性低下を防止
- 最大100回の反復 → 無限ループを防止
- 収束失敗時に警告 → ユーザーに通知

## トラブルシューティング

### 問題: 「解析がフリーズする」
- **原因**: 反復回数上限の修正が適用されていない可能性
- **対処**: ビルドが最新か確認し、再ビルドする

### 問題: 「変位が1m以上になる」
- **原因**: IY/IZバグまたは剛性比率下限の修正が適用されていない可能性
- **対処**:
  1. [HorizontalCalculationViewModel.cs:1182-1184](../ViewModels/HorizontalCalculationViewModel.cs#L1182-L1184) を確認
  2. `EI0y = beam.Section.Material.E * beam.Section.IY` になっているか確認
  3. [HorizontalCalculationViewModel.cs:1148](../ViewModels/HorizontalCalculationViewModel.cs#L1148) で `Math.Clamp(EIy_eff / EI0y, 0.01, 1.0)` になっているか確認

### 問題: 「収束警告が頻繁に出る」
- **原因**: モデルの条件が厳しい、または許容誤差が小さすぎる
- **対処**:
  - これは正常動作の可能性あり (反復上限が機能している)
  - 変位が1m未満であれば問題なし
  - 必要に応じて、許容誤差や反復上限を調整

## ログの確認

解析後、以下のログメッセージを確認:

```
Warning: Maximum iterations 100 reached. Residual norm=X.XXXe-XX (tolerance=1.000e-06)
```

このメッセージが出た場合:
- ✅ 反復上限が機能している
- ✅ 無限ループを防止できている
- ⚠️ 完全には収束していない可能性あり
- ➡️ 変位が1m未満なら許容範囲

## 検証チェックリスト

- [ ] Example 9が正常に読み込まれる
- [ ] 要素分割が完了する
- [ ] 水平解析ウィンドウが開く
- [ ] レベル2地震1方向がチェックされている
- [ ] IsPileNonLinear = true が設定されている
- [ ] 解析が開始される
- [ ] 解析が完了する (フリーズしない)
- [ ] 変位が1.0m未満である
- [ ] エラーダイアログが表示されない
- [ ] ログに異常なメッセージがない

## 参考情報

### 修正ファイル
- [HorizontalCalculationViewModel.cs](../ViewModels/HorizontalCalculationViewModel.cs)
  - Line 976: `const int maxIterations = 100;`
  - Line 1060-1065: 収束失敗警告
  - Line 1148, 1187: 剛性比率下限 0.01
  - Line 1182-1184: IY/IZ修正

### 関連定数
- [PhysicalConstants.cs](../Constants/PhysicalConstants.cs)
  - `AnalysisConstants.MAX_ITERATIONS = 1000` (汎用定数)
  - `AnalysisConstants.CONVERGENCE_TOLERANCE = 1e-8`

### 解析パラメータ
- 収束許容誤差 (alpha): `1e-6`
- 変位閾値: `1.0m`
- 最大反復回数: `100`
- 剛性比率下限: `0.01` (1%)
- 剛性比率上限: `1.0` (100%)

## まとめ

このテストガイドに従って Example 9 の非線形解析を実行し、すべてのチェック項目が✅であれば、バグ修正が正しく機能していることを確認できます。

もし問題が発生した場合は、上記のトラブルシューティングセクションを参照し、必要に応じてソースコードを確認してください。
