# CLAUDE.md

このリポジトリで作業するときの前提です。**まず `README.md` を読んでください。**
構成・全体の流れ・踏み抜きやすい暗黙の前提・メッセージの書き方・用語は
すべてそちらにあります。ここには作業上の約束だけを書きます。

## 言語

コメント・コミットメッセージ・UI 文字列・ヘルプはすべて**日本語**です。
コード識別子は英語のままで構いません。

## 変更したら必ず

```
dotnet build TestProject1/TestProject1.csproj
dotnet test  TestProject1/TestProject1.csproj --no-build
```

- ビルド結果は **`0 エラー` の行**で確認します。アプリ起動中は `MSB3021` で
  失敗しますが、これは `error CS` の grep に引っかかりません。
- テストは失敗 0 を維持します。件数が減っていたら、消したテストがないか確認を。

## ビルドとテストで守れない領域

**単一ファイル発行 (publish) でしか出ない解析があります。**
配布は `PublishSingleFile` + `SelfContained` で、`IL3000` 系
（`Assembly.Location` は単一ファイルでは常に空文字）などは
publish のときだけ検査されます。`TreatWarningsAsErrors` が有効なので
**警告ではなくエラー**になり、publish だけが落ちます。

次のような変更をしたら publish も通してください。

- 実行ファイルやアセンブリの場所を扱う
  （`Assembly.Location` は使わず `AppContext.BaseDirectory`）
- リフレクション・動的読み込み
- NuGet パッケージの更新

```
dotnet publish Graphics_r1/PileDesign.csproj -p:PublishProfile=FolderProfile
```

なお **AvalonDock は net8.0 向けアセットを持たず**、.NET Framework 4.8 向けが
互換フォールバックで使われています（`NU1701`。許容設定に入れてあります）。
更新したらドッキング操作を実機で確認してください。

## 触ったら足すテスト

このリポジトリでは「ビルドは通るが実行時に静かに壊れる」種類の不具合が
繰り返し起きています。該当する変更をしたら、対応するテストに追加してください。

| 変更 | 追加先 |
|---|---|
| ウィンドウの XAML | `*XamlSmokeTests` (StaticResource のキー誤りはビルドを通る) |
| コマンドのバインド | `DeadBindingTests` が自動で検査 |
| ヘルプのアンカー | `HelpAnchorTests` / `DeadBindingTests` が自動で検査 |
| 解析結果テーブルの列 | `ResultColumnTooltipTests` (説明の書き忘れ検出) |
| 画面の用語 | `TerminologyTests` (引退した呼び名の復活を検出) |
| メッセージ | `UserFacingMessageTests` (内部用語の露出を検出) |

## 数値を動かす変更

解析結果を変える変更は、影響範囲を明示してから行ってください。
とくに次は独断で直さないこと。

- 並列に集めた寄与の加算順 (`README.md` の「暗黙の前提」参照)。
- M-φ に渡す軸力の扱い。ランプであって VL 固定ではありません。

### 既知の数値上の問題 (未修正)

直すべきものですが、全モデルの結果が動くので**影響範囲を出してから個別のコミット**で。

- `PileSection.GJ` が断面二次モーメント (`π(D⁴−d⁴)/64`) を使っている。
  円形断面の断面二次極モーメントは `/32` なので**ねじり剛性が 2 倍過小**。
- `PileSection.W` / `WCorroded` が鋼材断面積を二重に控除している
  (`Ac` の時点で主筋・テンドンを引いてあるのに、もう一度引いている)。
  **自重が 1〜3% 過小**で、引抜き抵抗の側では危険側。

> `PileSection.EI` のテンドン換算項は 2026-08-20 に実装済みです
> (`TendonIEquivalent` / `SectionFlexuralRigidityTests`)。
> ここに「無い」と書いてあったのは誤りでした。

## コミット

- 1 コミットの粒度は「1 つのまとまった意図」。
- 本文には**何を直したかだけでなく、なぜそうなっていたか**を書きます。
  この方針で書かれた既存のコミットに倣ってください。
- コミット・プッシュは指示されたときだけ行います。
