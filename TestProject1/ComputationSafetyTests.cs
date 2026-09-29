using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1;

/// <summary>
/// 計算結果の安全性: 式の入力の範囲・計算結果の有限性・解析結果の全体の検査・ケースの状態の知らせ・
/// 結果の前提の記録・単位の換算・解析前の誤りからの案内。
/// </summary>
[TestClass]
[DoNotParallelize]
public class ComputationSafetyTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

    // ── Chang の式 ──

    private static Chang ValidChang()
    {
        var c = new Chang(_EI: 50000, _beta: 0.5, _h: 0, _horizontalLoad: 100, _ar: 1.0) { Kh0 = 20000, Beta0 = 0.5 };
        return c;
    }

    [TestMethod]
    public void Chang_ValidInput_HasNoProblemAndFiniteResults()
    {
        var c = ValidChang();
        c.Update();
        Assert.AreEqual("", c.Problem);
        Assert.IsTrue(double.IsFinite(c.PileHeadDisplacement) && c.PileHeadDisplacement > 0);
        Assert.IsTrue(double.IsFinite(c.MaxBendingMoment));
    }

    /// <summary><b>本題。</b> 範囲外の入力では計算せず、結果を数値でない値にして理由を示す (普通の結果に見せない)。</summary>
    [TestMethod]
    public void Chang_InvalidInput_IsReportedNotComputed()
    {
        var c = ValidChang();
        c.Ar = 1.5;
        c.EI = 0;
        c.Update();
        StringAssert.Contains(c.Problem, "曲げ剛性 EI");
        StringAssert.Contains(c.Problem, "固定度 αr");
        Assert.IsTrue(double.IsNaN(c.PileHeadDisplacement), "範囲外の入力で計算した値を出している");
    }

    // ── Steinbrenner の式の土層 ──

    [TestMethod]
    public void SettlementLayers_OutOfRangeValues_AreReportedByLayer()
    {
        var layers = new ObservableCollection<SettlementSoilLayer>
        {
            new() { Thickness = 2, Ek = 10000, PoissonsRatio = 0.3 },
            new() { Thickness = 0, Ek = 0, PoissonsRatio = 0.7 },
        };
        var problems = Steinnbrener.DescribeLayerProblems(layers);
        Assert.AreEqual(3, problems.Count);
        Assert.IsTrue(problems.All(p => p.Contains("2 層目")), "問題の層を示していない");
        Assert.AreEqual(0, Steinnbrener.DescribeLayerProblems(layers.Take(1)).Count);
    }

    /// <summary>群杭沈下の 2 つの解析の入口が、土層の値を確かめてから解くこと。</summary>
    [TestMethod]
    public void BothSettlementAnalyses_CheckTheLayersFirst()
    {
        StringAssert.Contains(TestSource.Read("Graphics_r1", "Services", "SettlementAnalysisService.cs"), "Steinnbrener.DescribeLayerProblems(");
        StringAssert.Contains(TestSource.Read("Graphics_r1", "Services", "IterativeBeamSettlementService.cs"), "Steinnbrener.DescribeLayerProblems(");
    }

    // ── 断面の入力と剛性 ──

    /// <summary>例題の杭体は、材料と剛性の検査を通ること (誤って止めない)。</summary>
    [TestMethod]
    public void ExamplePileBodies_PassTheMaterialAndStiffnessChecks()
    {
        var (input, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
        if (input == null) { Assert.Inconclusive(error); return; }
        Assert.AreEqual("", CheckInputData.CheckPileBodyGeometry(input, ""), "正しい例題の杭体を止めている");
    }

    /// <summary><b>本題。</b> 範囲外の材料 (Ec が 0) を、杭体・区間を示して止めること。</summary>
    [TestMethod]
    public void AZeroYoungsModulus_IsReportedWithItsLocation()
    {
        var (input, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
        if (input == null) { Assert.Inconclusive(error); return; }
        var section = input.PileBodies.SelectMany(b => b.PileBodySegments).Select(s => s.PileSection)
            .First(s => s.PileBodyType == PileTypeNames.InsituRc);
        section.ConcreteE = 0;

        string message = CheckInputData.CheckPileBodyGeometry(input, "");
        StringAssert.Contains(message, "ヤング係数 Ec");
        StringAssert.Matches(message, new Regex(@"杭体\d+ 区間\d+"));
    }

    // ── 解析結果の全体の検査 ──

    [TestMethod]
    public void NonFiniteResults_AreFoundWithCaseTargetAndPile()
    {
        var lc = new LoadCase { LoadName = "L2 X+" };
        var node = new Node { Name = "杭節点-4-2" };
        node.NodeResults.Add(new NodeResult { LoadCase = lc, CumulativeDisp = new NodeDisp(1, double.NaN, 0, 0, 0, 0) });
        var ok = new Node { Name = "杭節点-5-2" };
        ok.NodeResults.Add(new NodeResult { LoadCase = lc, CumulativeDisp = new NodeDisp(1, 2, 0, 0, 0, 0) });
        var model = new AnaModel { Nodes = [node, ok] };

        var findings = AnalysisResultValidator.FindNonFinite(model, out int total);
        Assert.AreEqual(1, total);
        Assert.AreEqual(4, findings[0].PileNo);
        string text = AnalysisResultValidator.Describe(findings, total)!;
        StringAssert.Contains(text, "L2 X+");
        StringAssert.Contains(text, "杭節点-4-2");
        StringAssert.Contains(text, "関係する杭: No.4");

        Assert.IsNull(AnalysisResultValidator.Describe(AnalysisResultValidator.FindNonFinite(new AnaModel(), out int none), none));
    }

    // ── ケースの状態の知らせ ──

    [TestMethod]
    public void RunOutcome_ListsUnconvergedCasesAndContinuedFailures()
    {
        var a = new LoadCase { LoadName = "L1", Level = 1, No = 1 };
        var b = new LoadCase { LoadName = "L2", Level = 2, No = 1 };
        var steps = new[]
        {
            new AnalysisStepResult { LoadCase = a, Step = 1, Status = StepStatus.Converged },
            new AnalysisStepResult { LoadCase = b, Step = 1, Status = StepStatus.Converged },
            new AnalysisStepResult { LoadCase = b, Step = 2, Status = StepStatus.Unconverged },
        };
        string text = AnalysisRunOutcome.Describe(steps, ["L2: 軸剛性を 0 にできませんでした"], out bool attention);
        Assert.IsTrue(attention);
        StringAssert.Contains(text, "2 ケースのうち、収束 1");
        StringAssert.Contains(text, "未収束: L2");
        StringAssert.Contains(text, "軸剛性を 0 にできませんでした");

        string clean = AnalysisRunOutcome.Describe(steps.Take(1), [], out bool none);
        Assert.IsFalse(none);
        StringAssert.Contains(clean, "全 1 ケースが収束しました");
    }

    // ── 結果の前提の記録 ──

    [TestMethod]
    public void AnalysisConditions_AreNullWithoutAnalysis_AndMentionEditsAfterIt()
    {
        var vm = new MainWindowViewModel { CurrentInputModel = new InputModel() };
        Assert.IsNull(vm.DescribeAnalysisConditions());
        vm.CurrentModel = new AnaModel();
        vm.RestoreInputChangedSinceAnalysis(true);
        StringAssert.Contains(vm.DescribeAnalysisConditions(), "解析のあとに入力が編集されています");
    }
}
