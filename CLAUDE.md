# CLAUDE.md

このリポジトリで作業するときの前提です。**まず `README.md` を読んでください。**
構成・全体の流れ・踏み抜きやすい暗黙の前提・メッセージの書き方・用語は
すべてそちらにあります。ここには作業上の約束だけを書きます。

## 言語

コメント・コミットメッセージ・UI 文字列・ヘルプはすべて**日本語**です。
コード識別子は英語のままで構いません。

## 変更したら必ず

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1
```

これ 1 本で、本体のビルド → テストのビルド → 実行 → **実行件数の確認**まで行います。
絞り込みは `-Filter "FullyQualifiedName~PrecastShearQNTests"`、
様子がおかしいときは `-Clean` を付けます。

素で `dotnet` を叩く場合は、順番と確認を自分で守ってください。

```
dotnet build Graphics_r1/PileDesign.csproj      ← 本体を先に
dotnet build TestProject1/TestProject1.csproj
dotnet test  TestProject1/TestProject1.csproj --no-build
```

### 緑を信じてよい条件

**このリポジトリでは「成功!」の表示だけでは足りません。** 実行されなかったテストは
失敗として出ないので、緑のまま通ります。実際に 1,768 件が 1,308 件になったまま
「成功!」と表示され、460 件が実行されていないのに commit できる状態になりました。

- ビルド結果は **`0 エラー` の行**で確認します。アプリ起動中は `MSB3021` で
  失敗しますが、これは `error CS` の grep に引っかかりません。
  **ビルドが通っていないのに `--no-build` でテストを走らせないこと。**古いバイナリが動きます。
- **実行件数を前回と見比べます。** 減っていたら、消したテストがないか確認を。
  `tools/run-tests.ps1` は下限を下回ったら失敗にします。
  `TestSuiteIntegrityTests` はアセンブリに含まれるテストの数を見ます。
  前者は実行の途中切れ、後者はビルドの取りこぼしを捕まえます。**両方いります。**

### 踏みやすい落とし穴

| 症状 | 原因と対処 |
|---|---|
| `MSB3021` / `error MSB3027` | アプリ起動中。閉じてから。`run-tests.ps1` は先に見て止めます |
| `CS2001: .g.cs が見つかりません` | WPF の一時プロジェクト (`_wpftmp`) の間欠不良。**本体を先にビルド**すれば出ません。出たら `Graphics_r1/obj` を消す |
| 件数が急に減った | 中間生成物の食い違い。`-Clean`（`obj` と `bin` を消して組み直し） |
| 例題を使うテストが大量に Inconclusive | 出力先を変えてビルドした。`-p:BaseOutputPath` を全体実行に使わないこと |

**`-p:BaseOutputPath` を全体実行に使わないでください。** アプリ起動中の回避策として
使いたくなりますが、例題の場所が変わって 13 件が失敗・203 件がスキップされたまま
「成功」と出ます。アプリを閉じるのが正解です。

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
| 杭種・断面タイプ・工法の追加 | `SectionInvariantTests` に代表断面を登録 (未登録だと `EverySectionTypeIsRegistered` が落ちる)。カタログに Mcr/Mu があれば `PrecastCatalogCrackMomentTests` の流儀で突合 |
| 断面計算オブジェクトの生成 | `SectionAssemblyTests`。ファクトリ (`PileSection.CreateSectionCalculator()`) と杭頭部 (`PileTop`) 以外で断面・材料を `new` しない。自前で組むと材料側のオプション (KCTB の εcu 等) が渡らず、解析と違う曲線になる (杭断面ウィンドウ 10 か所と杭中間部 M-φ で実際に起きた) |
| 材料則・断面積分 | `SectionInvariantTests` (零ひずみで N≈0 / 材料の σ(0)=0 / Mcr>0 / M-φ 単調) と `CrackStrainThresholdTests`。**プレストレスひずみは断面積分側だけが足す**（材料側 `GetStress` は足さない） |

## 数値を動かす変更

解析結果を変える変更は、影響範囲を明示してから行ってください。
とくに次は独断で直さないこと。

- 並列に集めた寄与の加算順 (`README.md` の「暗黙の前提」参照)。
- M-φ に渡す軸力の扱い。ランプであって VL 固定ではありません。

> `PileSection` の既知の誤り 2 件 (`GJ` の 2 倍過小、自重 `W` の鋼材二重控除) は
> 2026-08-24 に修正しました (`SectionWeightAndTorsionTests`)。
> `EI` のテンドン換算項も 2026-08-20 に実装済みです
> (`TendonIEquivalent` / `SectionFlexuralRigidityTests`)。
> **既知問題リストは、直したときに同じコミットで消すこと。**

## コミット

- 1 コミットの粒度は「1 つのまとまった意図」。
- 本文には**何を直したかだけでなく、なぜそうなっていたか**を書きます。
  この方針で書かれた既存のコミットに倣ってください。
- コミット・プッシュは指示されたときだけ行います。
