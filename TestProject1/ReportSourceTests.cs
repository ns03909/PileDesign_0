using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TestProject1;

/// <summary>
/// 計算書の元データ (<see cref="ReportSource"/>) を出力の開始時に 1 つの時点へ固定すること。
///
/// 計算書は表紙のモデル図を写すところで画面のメッセージを回す。そのあいだに解析の完了の知らせなどが走ると、
/// それより後で画面から読む値 (解析を済ませたかの印・基礎梁考慮の鉛直解析の結果・検定の結果) が
/// 別の時点のものになり、1 冊の計算書に混ざる。
/// </summary>
[TestClass]
[DoNotParallelize]
public class ReportSourceTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

    /// <summary><b>本題。</b> 取ったあとに画面の状態が変わっても、元データは変わらない。</summary>
    [TestMethod]
    public void TheCapturedStateDoesNotFollowLaterChanges()
    {
        var vm = new MainWindowViewModel { CurrentInputModel = new InputModel() };
        vm.IsVerticalAnalysisDone = true;
        vm.VerticalBeamCaseResults = [new PileDesign.FEM.VerticalBeamCaseResult { LoadCaseName = "VL" }];

        var source = vm.CaptureReportSource(vm.CurrentInputModel!, wantsFactoredEvaluation: false);

        vm.IsVerticalAnalysisDone = false;
        vm.VerticalBeamCaseResults.Add(new PileDesign.FEM.VerticalBeamCaseResult { LoadCaseName = "後から" });

        Assert.IsTrue(source.IsVerticalAnalysisDone, "印が取ったあとの値を追っている");
        Assert.AreEqual(1, source.VerticalBeamCaseResults!.Count, "結果の一覧が取ったあとの追加を追っている");
    }

    [TestMethod]
    public void AnalysisRunMetadata_IsIncludedInTheCapturedReportSource()
    {
        var input = new InputModel();
        var model = new PileDesign.FEM.AnaModel();
        var metadata = new PileDesign.Models.AnalysisRunMetadata
        {
            ApplicationVersion = "1.0.34-beta",
            ConvergenceMethod = "ラインサーチ",
            Level1Steps = 4,
            Level2Steps = 16,
            CaseParallelism = 8,
            MaximumIterations = 100,
            BaseResidualTolerance = 1e-6,
            Cases = [new PileDesign.Models.AnalysisCaseMetadata
            {
                Level = 1, LoadCaseNo = 2, LoadCaseName = "X方向",
                LoadCombinationNo = 4, LoadCombinationName = "地震時", Status = "Converged",
            }],
        };
        var resultSet = PileDesign.Models.AnalysisResultSet.Capture(
            input, model, null, hasHorizontal: true, hasVertical: false,
            hasGroupPileSettlement: false, hasVerticalBeam: false, isElementSplit: false,
            runRecords: [metadata]);
        Assert.IsNotNull(resultSet);

        var vm = new MainWindowViewModel { CurrentInputModel = input };
        vm.SetRestoredResultSet(resultSet, changedSinceAnalysis: false);
        var source = vm.CaptureReportSource(input, wantsFactoredEvaluation: false);

        // 実行条件は表紙の 1 行ではなく、「計算条件・仮定」の章の表に書く (出力の開始時の写しを渡す)
        Assert.AreEqual(1, source.RunRecords.Count);
        var rows = source.RunRecords[0].DescribeRows();
        CollectionAssert.Contains(rows, ("収束安定化", "ラインサーチ"));
        CollectionAssert.Contains(rows, ("反復上限", "100"));
        CollectionAssert.Contains(rows, ("解いたケース", "L1 X方向 (地震時)・収束"));
        Assert.IsFalse(source.AnalysisConditions?.Contains("ラインサーチ") == true, "表紙に実行条件が並んでいます");
    }

    /// <summary>
    /// 表紙のモデル図は画面 (編集中の入力) を写す。計算書の入力が解析時の控えで、解析のあとに編集していれば写さない。
    /// </summary>
    [TestMethod]
    public void TheCoverIsCapturedOnlyWhenTheScreenShowsTheReportInput()
    {
        var vm = new MainWindowViewModel { CurrentInputModel = new InputModel() };
        Assert.IsTrue(vm.CaptureReportSource(vm.CurrentInputModel!, false).CoverMatchesReportInput,
            "計算書の入力が画面の入力そのものなら写してよい");

        var snapshot = new InputModel();
        Assert.IsTrue(vm.CaptureReportSource(snapshot, false).CoverMatchesReportInput,
            "控えでも、解析のあとに編集していなければ中身は同じ");

        vm.RestoreInputChangedSinceAnalysis(true);   // 解析のあとに編集した (読込で戻すのと同じ入口)
        Assert.IsFalse(vm.CaptureReportSource(snapshot, false).CoverMatchesReportInput,
            "解析のあとに編集した入力の図を、解析時の入力の計算書の表紙に載せようとしている");
    }

    /// <summary>
    /// 検定を載せる設定で水平解析を済ませていれば、出力の開始時に検定を求めておくこと (結果か、組めなかった理由のどちらか)。
    /// どちらも無いと、計算書の検定の節が黙って欠ける。
    /// </summary>
    [TestMethod]
    public void TheEvaluationIsComputedUpFrontWhenWanted()
    {
        var vm = new MainWindowViewModel { CurrentInputModel = new InputModel() };
        vm.CurrentModel = new PileDesign.FEM.AnaModel();
        vm.IsHorizontalAnalysisDone = true;

        var wanted = vm.CaptureReportSource(vm.CurrentInputModel!, wantsFactoredEvaluation: true);
        Assert.IsTrue(wanted.FactoredEvaluation != null || wanted.FactoredEvaluationError != null,
            "検定を載せる設定なのに、出力の開始時に検定を求めていない (計算書の検定の節が黙って欠ける)");

        var notWanted = vm.CaptureReportSource(vm.CurrentInputModel!, wantsFactoredEvaluation: false);
        Assert.IsNull(notWanted.FactoredEvaluation);
        Assert.IsNull(notWanted.FactoredEvaluationError);
    }

    /// <summary>
    /// 出力の入口で、元データを取る前に元データ (<c>_source</c>) を読まないこと。取る前は既定値 (解析していない扱い)。
    /// 以前、検定を求めるかの判定がこれを読み、検定の節が常に欠ける形になりかけた。
    /// </summary>
    [TestMethod]
    public void TheEntryDoesNotReadTheSourceBeforeCapturingIt()
    {
        string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Output", "WordDocument.cs"),
            "public void CreateWordDocument(");
        int capture = body.IndexOf("_source = ", System.StringComparison.Ordinal);
        Assert.IsTrue(capture >= 0, "元データを取る箇所が見つかりません (テストの前提が崩れている)");
        int firstRead = body.IndexOf("_source.", System.StringComparison.Ordinal);
        Assert.IsTrue(firstRead < 0 || firstRead > capture, "元データを取る前に読んでいます");
    }

    /// <summary>
    /// 計算書の組み立てが、画面の状態 (解析を済ませたかの印・基礎梁考慮の鉛直解析の結果・検定) を
    /// 途中で直接読まないこと。読むのは <see cref="ReportSource"/> だけ。
    /// </summary>
    [TestMethod]
    public void TheReportDoesNotReadTheLiveStateDirectly()
    {
        var live = new Regex(@"mainWindowViewModel\??\.(Is(Horizontal|Vertical|VerticalBeam)AnalysisDone|VerticalBeamCaseResults)\b"
                             + @"|BuildEvaluationResult\(mainWindowViewModel,\s*(factored:\s*)?true\)");
        var files = Directory.GetFiles(TestSource.Dir("Graphics_r1", "Output"), "WordDocument*.cs");
        TestSource.AssertScanned(files.Length, 5, "計算書の組み立て (WordDocument*.cs)");
        var hits = files.SelectMany(f => File.ReadAllLines(f).Select((l, i) => (File: Path.GetFileName(f), Line: i + 1, Text: l)))
            .Where(l => !l.Text.TrimStart().StartsWith("//") && live.IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}  {l.Text.Trim()}")
            .ToList();
        Assert.AreEqual(0, hits.Count, "計算書の途中で画面の状態を直接読んでいます。ReportSource から読んでください:\n  "
            + string.Join("\n  ", hits));
    }
}
