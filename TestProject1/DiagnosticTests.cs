using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;

namespace TestProject1;

/// <summary>
/// 構造化した診断 (<see cref="Diagnostic"/>): 問題の場所を文から拾わず番号で持ち、知らせる文・杭の選択・
/// 入力画面への移動・ログ・計算書で同じものを使うこと。
/// </summary>
[TestClass]
[DoNotParallelize]
public class DiagnosticTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

    /// <summary>杭 3 本: No.1 と No.3 が杭体 2、No.2 が杭体 1。No と PileNo はわざと食い違わせる。</summary>
    private static InputModel ThreePiles()
    {
        var input = new InputModel();
        input.AttachViewModel(new MainWindowViewModel { CurrentInputModel = input });
        input.PileLayoutItems ??= [];
        input.PileLayoutItems.Add(new PileLayoutDataItem { No = 1, PileNo = 11, PileBodyNo = 2, GroundNo = 1 });
        input.PileLayoutItems.Add(new PileLayoutDataItem { No = 2, PileNo = 12, PileBodyNo = 1, GroundNo = 2 });
        input.PileLayoutItems.Add(new PileLayoutDataItem { No = 3, PileNo = 13, PileBodyNo = 2, GroundNo = 2 });
        return input;
    }

    // ── 1. 場所を番号で持つ ──

    /// <summary><b>本題。</b> 入力の検査は、場所の番号を文とは別に持つ (杭体・区間・地盤・層・杭)。</summary>
    [TestMethod]
    public void InputChecks_CarryTheLocationAsFields()
    {
        var (input, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
        if (input == null) { Assert.Inconclusive(error); return; }
        var (bodyNo, segNo) = input.PileBodies
            .SelectMany((b, bi) => b.PileBodySegments.Select((s, si) => (Body: bi + 1, Seg: si + 1, s.PileSection)))
            .Where(x => x.PileSection?.PileBodyType == PileTypeNames.InsituRc)
            .Select(x => (x.Body, x.Seg)).First();
        input.PileBodies[bodyNo - 1].PileBodySegments[segNo - 1].PileSection.ConcreteE = 0;
        input.GroundsInput[0].GroundLayers[1].LayerThickness = double.NaN;

        var problems = CheckInputData.CollectAnalysisBlockers(input);

        var body = problems.First(p => p.Message.Contains("ヤング係数 Ec"));
        Assert.AreEqual(DiagnosticTargetKind.PileBodySegment, body.Target.Kind);
        Assert.AreEqual(bodyNo, body.Target.PileBodyNo);
        Assert.AreEqual(segNo, body.Target.SegmentNo);
        Assert.AreEqual(DiagnosticOrigin.Input, body.Origin);
        StringAssert.StartsWith(body.Message, $"杭体{bodyNo} 区間{segNo}: ", "文の頭の場所が番号と食い違っています");

        var layer = problems.First(p => p.Message.Contains("層厚"));
        Assert.AreEqual(DiagnosticTarget.GroundLayer(1, 2), layer.Target);
        StringAssert.StartsWith(layer.Message, "地盤1 層2: ");
    }

    // ── コンクリートのヤング係数の妥当性 ──

    /// <summary>
    /// <b>本題。</b> γ を下限にすると Ec ≈ 0.4 N/mm² になり、「0 以下」の検査を通って解析が走った。
    /// 杭のコンクリートとしてありえない Ec は、杭体・区間を示して止める。
    /// </summary>
    [TestMethod]
    public void ANearZeroYoungsModulus_StopsTheAnalysis()
    {
        var (input, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
        if (input == null) { Assert.Inconclusive(error); return; }
        var section = input.PileBodies[0].PileBodySegments[0].PileSection;
        section.ConcreteGamma = 0.1;
        section.RecalculateConcreteE();
        Assert.IsTrue(section.ConcreteE > 0 && section.ConcreteE < 1, $"(前提) Ec = {section.ConcreteE}");

        var problem = CheckInputData.CollectAnalysisBlockers(input).SingleOrDefault(p => p.Message.Contains("小さすぎます"));
        Assert.IsNotNull(problem, "ほぼ 0 の Ec で解析を止めていません");
        Assert.AreEqual(DiagnosticTarget.PileBodySegment(1, 1), problem.Target);
        StringAssert.Contains(problem.Message, "Ec が 0.");   // 「0」に丸めずに値を示す
    }

    /// <summary>止めるほどではないが低い Ec (1,000〜10,000 N/mm²) は、解析を止めずに警告する。</summary>
    [TestMethod]
    public void ALowYoungsModulus_IsOnlyAWarning()
    {
        var (input, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
        if (input == null) { Assert.Inconclusive(error); return; }
        input.PileBodies[0].PileBodySegments[0].PileSection.ConcreteE = 5_000;

        Assert.IsFalse(CheckInputData.CollectAnalysisBlockers(input).Any(p => p.Message.Contains("ヤング係数 Ec")), "警告で済む Ec で止めています");
        Assert.IsTrue(CheckInputData.CollectInputWarnings(input).Any(w => w.StartsWith("杭体1 区間1:") && w.Contains("低め")),
            "低い Ec を警告していません");
    }

    /// <summary>同梱の杭の例題は、Ec の下限・警告に掛からないこと (正しい入力を止めない・騒がない)。</summary>
    [TestMethod]
    public void BundledExamples_HaveARealisticYoungsModulus()
    {
        int checkedFiles = 0;
        foreach (var file in TestSource.ExampleFiles("PileExample*.json", 10))
        {
            string pileName = Path.GetFileNameWithoutExtension(file);
            string groundName = "Example" + pileName["PileExample".Length..];
            if (TestSource.ExamplePath(groundName + ".json") == null) continue;
            var (input, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            Assert.IsNotNull(input, $"{pileName}: {error}");
            checkedFiles++;
            Assert.IsFalse(CheckInputData.CollectAnalysisBlockers(input).Any(p => p.Message.Contains("ヤング係数 Ec")), $"{pileName}: Ec で止めています");
            Assert.IsFalse(CheckInputData.CollectInputWarnings(input).Any(w => w.Contains("低め")), $"{pileName}: Ec を低めと警告しています");
        }
        TestSource.AssertScanned(checkedFiles, 10, "杭の例題");
    }

    /// <summary>単位体積重量 γ の入力欄は、杭のコンクリートとしてありうる値 (10 kN/m³ 以上) に限る。</summary>
    [TestMethod]
    public void TheUnitWeightInput_HasARealisticFloor()
    {
        var lines = TestSource.Read("Graphics_r1", "Views", "PileSectionWindow.xaml").Split('\n');
        var floors = lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("Binding PileSection.ConcreteGamma"))
            .Select(x => string.Join(" ", lines.Skip(x.i).Take(3))).ToList();
        TestSource.AssertScanned(floors.Count, 4, "γ の入力欄");
        Assert.IsTrue(floors.All(f => f.Contains("NumericInput.Min=\"10\"")), "γ の入力欄の下限が 10 kN/m³ になっていません");
    }

    /// <summary>ヤング係数の表示は、0 でない 1 未満の値を「0」に丸めない。</summary>
    [TestMethod]
    public void ModulusDisplay_DoesNotRoundASmallValueToZero()
    {
        Assert.AreEqual((0.437).ToString("G3"), NumberDisplay.Modulus(0.437));
        Assert.AreNotEqual("0", NumberDisplay.Modulus(0.0004));
        Assert.AreEqual((25743.4).ToString("N0"), NumberDisplay.Modulus(25743.4));
        Assert.AreEqual("0", NumberDisplay.Modulus(0));
    }

    [TestMethod]
    public void PileReferenceProblems_PointAtThePile()
    {
        var input = ThreePiles();
        input.PileBodies = [];
        var problems = CheckInputData.CollectSoilPileProblems(input);
        Assert.AreEqual(3, problems.Count);
        CollectionAssert.AreEqual(new int?[] { 1, 2, 3 }, problems.Select(p => p.Target.PileNo).ToArray());
        Assert.IsTrue(problems.All(p => p.Target.Kind == DiagnosticTargetKind.Pile));
    }

    // ── 2. 杭と杭体を混同しない選び方 ──

    /// <summary>
    /// <b>本題。</b> 杭の問題はその杭だけ、杭体の問題はその杭体を使う杭すべてを選び、範囲を書き分ける。
    /// 杭は杭配置の番号 (No) で引き、杭体の番号とは混ぜない。
    /// </summary>
    [TestMethod]
    public void Selection_DistinguishesASpecificPileFromASharedPileBody()
    {
        var input = ThreePiles();

        // 杭 No.2 の問題: 杭体番号 2 を使う杭 (No.1・3) は選ばない
        var pileOnly = DiagnosticSelection.Resolve([Diagnostic.InputAt(DiagnosticTarget.Pile(2), "x")], input);
        CollectionAssert.AreEqual(new[] { 2 }, pileOnly.Piles.Select(p => p.No).ToArray());
        CollectionAssert.AreEqual(new[] { "杭 No.2 (その杭だけ)" }, pileOnly.Scopes.ToArray());

        // 杭体 2 の区間の問題: 杭体 2 を使う杭すべて (杭 No.2 ではない)
        var shared = DiagnosticSelection.Resolve([Diagnostic.InputAt(DiagnosticTarget.PileBodySegment(2, 1), "x")], input);
        CollectionAssert.AreEqual(new[] { 1, 3 }, shared.Piles.Select(p => p.No).ToArray());
        CollectionAssert.AreEqual(new[] { "杭体2 を使う杭すべて: No.1・No.3" }, shared.Scopes.ToArray());

        // 地盤は、その地盤を使う杭すべて
        var ground = DiagnosticSelection.Resolve([Diagnostic.InputAt(DiagnosticTarget.GroundLayer(2, 1), "x")], input);
        CollectionAssert.AreEqual(new[] { 2, 3 }, ground.Piles.Select(p => p.No).ToArray());

        // 使う杭の無い杭体は、無いと書く (黙って何も選ばないのではなく)
        var unused = DiagnosticSelection.Resolve([Diagnostic.InputAt(DiagnosticTarget.PileBody(9), "x")], input);
        Assert.AreEqual(0, unused.Piles.Count);
        StringAssert.Contains(unused.Scopes.Single(), "使っている杭はありません");
    }

    /// <summary>メイン画面は、決めた杭をそのまま選ぶ (番号を取り直さない)。</summary>
    [TestMethod]
    public void MainScreen_SelectsTheResolvedPiles()
    {
        var input = ThreePiles();
        var vm = new MainWindowViewModel { CurrentInputModel = input };
        var selection = DiagnosticSelection.Resolve([Diagnostic.InputAt(DiagnosticTarget.PileBody(2), "x")], input);
        Assert.AreEqual(2, vm.SelectForReview(selection));
        CollectionAssert.AreEqual(new[] { true, false, true }, input.PileLayoutItems.Select(p => p.IsSelected).ToArray());
    }

    /// <summary>同じ杭体・地盤・杭頭の高さの杭をまとめた 1 行は、その杭すべてを指す (同じ杭体のほかの杭は指さない)。</summary>
    [TestMethod]
    public void AGroupedLine_PointsAtEachOfItsPiles()
    {
        var d = Diagnostic.Input(DiagnosticTarget.Pile(1), "杭 No.1・3: …")
            with { MoreTargets = [DiagnosticTarget.Pile(3)] };
        var selection = DiagnosticSelection.Resolve([d], ThreePiles());
        CollectionAssert.AreEqual(new[] { 1, 3 }, selection.Piles.Select(p => p.No).ToArray());
    }

    // ── 3. 入力画面へ ──

    [TestMethod]
    public void EachLocation_HasItsInputScreen()
    {
        Assert.AreEqual(InputDestination.PileBodyWindow, DiagnosticSelection.DestinationOf(DiagnosticTarget.PileBodySegment(1, 2)));
        Assert.AreEqual(InputDestination.PileBodyWindow, DiagnosticSelection.DestinationOf(DiagnosticTarget.PileBody(1)));
        Assert.AreEqual(InputDestination.GroundWindow, DiagnosticSelection.DestinationOf(DiagnosticTarget.GroundLayer(1, 2)));
        Assert.AreEqual(InputDestination.LoadCaseWindow, DiagnosticSelection.DestinationOf(DiagnosticTarget.LoadCase("L1")));
        Assert.AreEqual(InputDestination.SettlementLayers, DiagnosticSelection.DestinationOf(DiagnosticTarget.SettlementLayer(2)));
        // 杭の問題は、その杭を選んで「選択杭の一括変換」を開く (杭体番号・地盤番号・ΔZc・群杭係数を直せる)
        Assert.AreEqual(InputDestination.PileLayoutEdit, DiagnosticSelection.DestinationOf(DiagnosticTarget.Pile(1)));
        Assert.AreEqual(InputDestination.None, DiagnosticSelection.DestinationOf(DiagnosticTarget.Nowhere));

        // 画面を開ける最初の問題 (場所の無い問題は飛ばす)
        var first = DiagnosticSelection.FirstNavigable([
            Diagnostic.InputAt(DiagnosticTarget.Nowhere, "a"),
            Diagnostic.InputAt(DiagnosticTarget.GroundLayer(2, 1), "b"),
        ]);
        Assert.AreEqual(DiagnosticTarget.GroundLayer(2, 1), first);
    }

    /// <summary>
    /// 解析のウィンドウからは「閉じたあとに開く」ことだけを頼み、開くのはメイン画面。開いたら頼みは消える。
    /// 無人実行 (「いいえ」) では頼まない。
    /// </summary>
    [TestMethod]
    public void Navigation_IsRequestedOnlyOnYes_AndConsumedOnce()
    {
        var input = ThreePiles();
        var vm = new MainWindowViewModel { CurrentInputModel = input };
        input.AttachViewModel(vm);

        CheckInputData.ShowBlockers(input, [Diagnostic.InputAt(DiagnosticTarget.PileBody(2), "x")], "水平解析", "入力データ");
        Assert.IsNull(vm.PendingInputNavigation, "「いいえ」なのに入力画面を開く予定にしています");

        vm.RequestInputNavigation(DiagnosticTarget.Pile(1));   // 開く画面の無い場所
        vm.OpenPendingInputNavigation();
        Assert.IsNull(vm.PendingInputNavigation, "開いたあとも頼みが残っています (次に閉じたときにまた開く)");
        Assert.IsNull(vm.InputFocus, "入力画面を閉じたあとも選ぶ場所が残っています");
    }

    /// <summary>入力画面は、問題の場所の杭体・地盤を選んだ状態で開く。</summary>
    [TestMethod]
    public void InputScreens_OpenAtTheFocusedBodyAndGround()
    {
        var main = new MainWindowViewModel();
        var input = main.CurrentInputModel!;
        input.GroundsInput.Add(input.GroundsInput[0].DeepCopy());
        main.InputFocus = DiagnosticTarget.GroundLayer(2, 1);
        Assert.AreEqual(2, new GroundLayerViewModel(main).GroundNo);

        input.PileBodies.Add(input.PileBodies[0].DeepCopy());
        main.InputFocus = DiagnosticTarget.PileBodySegment(2, 1);
        int? body = null;
        var captured = XamlSmokeTestSupport.RunOnStaThread(() => body = new PileBodyViewModel(main).PileBodyNo, out bool timedOut);
        if (timedOut) { Assert.Inconclusive("杭体の画面の ViewModel を作れませんでした (時間切れ)"); return; }
        Assert.IsNull(captured, captured?.ToString());
        Assert.AreEqual(2, body);
    }

    /// <summary>
    /// 群杭沈下の沈下用土層の値の誤りは、押す前に理由を出し、直す場所 (土層タブ) を開く。
    /// 以前は解析の中で止めるだけで、どのタブを直せばよいかは案内していなかった。
    /// </summary>
    [TestMethod]
    public void SettlementLayerProblems_OpenTheSoilLayerTab()
    {
        var vm = new MainWindowViewModel();
        var pgs = vm.CurrentInputModel!.PileGroupSettlement;
        pgs.LoadingType = "任意矩形";
        pgs.RectLoads.Add(new RectLoad { QA = 100 });
        pgs.SettlementSoilLayers.Clear();
        pgs.SettlementSoilLayers.Add(new SettlementSoilLayer { Thickness = 2, Ek = 10000, PoissonsRatio = 0.3 });
        pgs.SettlementSoilLayers.Add(new SettlementSoilLayer { Thickness = 3, Ek = 0, PoissonsRatio = 0.3 });

        StringAssert.Contains(vm.GroupSettlementAnalysisDisabledReason, "2 層目");
        MainWindowViewModel.GroupSettlementInputTab? opened = null;
        vm.ActivateGroupSettlementInputTabAction = tab => opened = tab;
        Assert.IsTrue(vm.ShowGroupSettlementBlockerIfAny());
        Assert.AreEqual(MainWindowViewModel.GroupSettlementInputTab.SoilLayers, opened);
    }

    // ── 解析で止まったとき ──

    /// <summary>
    /// <b>本題。</b> 場所は例外が持つ問題から取り、文は解かない。並列に解いたとき (AggregateException) も辿る。
    /// 文に「杭No.5」と書いてあるだけの例外からは杭を拾わない。
    /// </summary>
    [TestMethod]
    public void AnalysisFailures_TakeTheLocationFromTheException_NotTheText()
    {
        var located = new DiagnosticException(Diagnostic.AnalysisAt(DiagnosticTarget.Pile(4), "要素を作れません"));
        var failed = new AnalysisCaseFailedException("L2 X+", "荷重ステップ 3/12 の反復", located);
        CollectionAssert.AreEqual(new[] { 4 }, AnalysisFailure.PileNosIn(new AggregateException(failed)).ToArray());

        Assert.AreEqual(0, AnalysisFailure.PileNosIn(new InvalidOperationException("杭No.5 の地盤が見つかりません (杭節点-5-3)")).Count);
    }

    /// <summary>場所の分からない止まり方は、止まったケース・段階からログで追うよう案内する。途中の結果の扱いも書く。</summary>
    [TestMethod]
    public void AnUnlocatedFailure_PointsToTheLogByCaseAndStage()
    {
        var failed = new AnalysisCaseFailedException("L2 X+", "荷重ステップ 3/12 の反復", new InvalidOperationException("連立方程式の求解に失敗しました"));
        string text = AnalysisFailure.Describe(failed);
        StringAssert.Contains(text, "特定できませんでした");
        StringAssert.Contains(text, "荷重ケース「L2 X+」の「荷重ステップ 3/12 の反復」");
        StringAssert.Contains(text, "途中までの結果はメイン画面に登録していません");

        var locatedFailure = new AnalysisCaseFailedException("L1", null,
            new DiagnosticException(Diagnostic.AnalysisAt(DiagnosticTarget.PileBody(2), "断面値が無効です")));
        string located = AnalysisFailure.Describe(locatedFailure, DiagnosticSelection.Resolve(AnalysisFailure.DiagnosticsIn(locatedFailure), ThreePiles()));
        StringAssert.Contains(located, "関係する場所: 杭体2");
        StringAssert.Contains(located, "杭体2 を使う杭すべて: No.1・No.3");
        Assert.IsFalse(located.Contains("特定できませんでした"));
    }

    /// <summary>剛性の検査で止まったとき、ゼロの対角成分がある杭を場所として持つ。</summary>
    [TestMethod]
    public void TheStabilityCheck_CarriesThePiles()
    {
        StringAssert.Contains(TestSource.Read("Graphics_r1", "FEM", "AnaModel.cs"), "throw new PileDesign.Common.DiagnosticException(msg, [problem]);");
        Assert.AreEqual(12, PileDesign.FEM.PileNodeNaming.PileNoOf("杭節点-12-3:Ux"));
        Assert.IsNull(PileDesign.FEM.PileNodeNaming.PileNoOf("ActionPoint:Uz"));
    }

    /// <summary>解析の知らせ・入力の検査は、文を正規表現で解いて場所を拾わない (書き方を変えると黙って外れる)。</summary>
    [TestMethod]
    public void NoLocationIsParsedFromMessages()
    {
        foreach (var file in new[] { "AnalysisFailure.cs", "CheckInputData.cs", "DiagnosticSelection.cs" })
            Assert.IsFalse(TestSource.Read("Graphics_r1", "Services", file).Contains("Regex"),
                $"{file} が文から場所を拾っています。Diagnostic.Target を使ってください");
    }

    // ── 4〜5. 段階を分けて共通に使う ──

    [TestMethod]
    public void LogLine_HasTheOriginAndEachNumber()
    {
        var d = Diagnostic.InputAt(DiagnosticTarget.PileBodySegment(2, 3), "Ec が 0 以下");
        string line = d.ToLogLine();
        StringAssert.StartsWith(line, "[入力・解析を止める] model=");
        StringAssert.Contains(line, " kind=PileBodySegment pileBody=2 segment=3 : 杭体2 区間3: Ec が 0 以下");
    }

    /// <summary>解析結果の数値でない値は、段階「解析」の問題として杭とケースを持つ。</summary>
    [TestMethod]
    public void NonFiniteResults_BecomeAnalysisDiagnostics()
    {
        var findings = new[]
        {
            new AnalysisResultValidator.Finding("L2 X+", "節点 杭節点-4-2", 4, "変位 (NaN)"),
            new AnalysisResultValidator.Finding("L1", "節点 ActionPoint", null, "変位 (無限大)"),
        };
        var d = AnalysisResultValidator.ToDiagnostics(findings);
        Assert.IsTrue(d.All(x => x.Origin == DiagnosticOrigin.Analysis));
        Assert.AreEqual(DiagnosticTarget.Pile(4) with { CaseName = "L2 X+" }, d[0].Target);
        Assert.AreEqual(DiagnosticTargetKind.LoadCase, d[1].Target.Kind);
    }

    /// <summary><b>本題 (5)。</b> 計算書の数値でない値は、解析で生じたのか表を作る段階で生じたのかを書き分ける。</summary>
    [TestMethod]
    public void TheReport_SaysWhereTheNonFiniteValuesCameFrom()
    {
        const string table = "数値でない値 (NaN・無限大) を含む表が 1 個あります (計 2 か所)。";
        StringAssert.Contains(WordDocument.DescribeNonFinite(table, 3), "生じた段階: 解析");
        StringAssert.Contains(WordDocument.DescribeNonFinite(table, 0), "生じた段階: 結果の出力");
        StringAssert.Contains(WordDocument.DescribeNonFinite(null, 3), "計算書の表には出ていませんが");
        Assert.IsNull(WordDocument.DescribeNonFinite(null, 0));
    }

    // ── 6. 性能の記録 ──

    [TestMethod]
    public void PerfRecords_KeepTheModelScaleAndConditions()
    {
        PerfLog.Reset();
        try
        {
            string conditions = PerfLog.Conditions(piles: 12, elements: 340, cases: 8, format: "Word (docx)");
            Assert.AreEqual("杭 12 本・要素 340・荷重ケース 8・出力 Word (docx)", conditions);
            PerfLog.Record("計算書の出力", TimeSpan.FromMilliseconds(40), 12, "本の杭", conditions);
            PerfLog.Record("計算書の出力", TimeSpan.FromMilliseconds(10), 3, "本の杭", PerfLog.Conditions(piles: 3));
            Assert.AreEqual(conditions, PerfLog.Summary().Single().ConditionsAtMax, "最も遅かったときの条件を残していません");
            PerfLog.WriteSummary();
        }
        finally { PerfLog.Reset(); }
    }

    /// <summary>解析・保存・計算書の出力・結果の表は、規模と条件を添えて記録する。</summary>
    [TestMethod]
    public void HeavyOperations_RecordTheirConditions()
    {
        var sites = new[]
        {
            ("ViewModels", "HorizontalCalculationViewModel.Run.cs"),
            ("ViewModels", "MainWindowViewModel.FileIO.cs"),
            ("Output", "WordDocument.cs"),
            ("Services", "AnalysisResultTableService.cs"),
        };
        var missing = sites.Where(s => !TestSource.Read("Graphics_r1", s.Item1, s.Item2).Contains("PerfLog.Conditions(")).Select(s => s.Item2).ToList();
        Assert.AreEqual(0, missing.Count, "規模・条件を添えずに記録しています: " + string.Join(", ", missing));
    }

    // ── 7. 途中の結果の扱い ──

    [TestMethod]
    public void UnconvergedCases_AreRegisteredButNotJudged()
    {
        var lc = new LoadCase { LoadName = "L2", Level = 2, No = 1 };
        string text = AnalysisRunOutcome.Describe(
            [new PileDesign.FEM.AnalysisStepResult { LoadCase = lc, Step = 1, Status = PileDesign.FEM.StepStatus.Unconverged }], [], out _);
        StringAssert.Contains(text, "収束していないケースの結果も登録します");
        StringAssert.Contains(text, "OK にも NG にも数えません");
    }

    // ── 8. 代表例の結果比較 ──

    /// <summary>収束の回帰のスナップショットは、変位・反力に加えて杭の曲げモーメントも持つ (検定値を決める断面力)。</summary>
    [TestMethod]
    public void EverySnapshot_RecordsThePileMoment()
    {
        var files = Directory.GetFiles(TestSource.Dir("TestProject1", "ConvergenceRegression", "Snapshots"), "*.json");
        TestSource.AssertScanned(files.Length, 5, "収束の回帰のスナップショット");
        var missing = files.Where(f => !File.ReadAllText(f).Contains("\"maxAbsPileMoment\"")).Select(Path.GetFileName).ToList();
        Assert.AreEqual(0, missing.Count, "杭の曲げモーメントを持たないスナップショット: " + string.Join(", ", missing)
            + "\nUPDATE_SNAPSHOTS=1 で取り直してください");
    }
}
