using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.ViewModels;

namespace TestProject1;

/// <summary>
/// 解析の再現に要る条件 (<see cref="PileDesign.Models.AnalysisRunMetadata"/>) を、解析を実行したときの値で残すこと。
/// 追加実行で、別の版の解析結果を 1 つのモデルに並べないこと。
/// </summary>
[TestClass]
public class AnalysisRunMetadataTests
{
    /// <summary>
    /// 条件は解析を終えたときに取り、登録するときはそれを使う (登録するまでに画面の設定を変えても、それは解析に使っていない)。
    /// ケースの状態は登録するときの結果から取る。
    /// </summary>
    [TestMethod]
    public void TheSettingsAreTakenWhenTheAnalysisRuns()
    {
        string run = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs");
        StringAssert.Contains(TestSource.MethodBody(run, "private async Task FinishRunAsync("), "_settingsAtLastRun = CaptureRunSettings();");
        string vm = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs");
        StringAssert.Contains(vm, "var runMetadata = _settingsAtLastRun ?? CaptureRunSettings();");
        StringAssert.Contains(vm, "runMetadata.Cases = CasesOf(this.CurrentModel);");
    }

    /// <summary>記録する収束の基準・反復の上限は、解析が使う定数そのもの (写しを持たない)。</summary>
    [TestMethod]
    public void TheRecordedTolerancesAreTheOnesTheSolverUses()
    {
        string run = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs");
        StringAssert.Contains(run, "const double alpha = BaseConvergenceTolerance;");
        StringAssert.Contains(run, "const double RELAXED_ALPHA = RelaxedResidualTolerance;");
        StringAssert.Contains(run, "maxIterations = SkipIteration ? 1 : MaximumNewtonIterations;");
        Assert.AreEqual(1e-6, HorizontalCalculationViewModel.BaseConvergenceTolerance);
        Assert.AreEqual(100, HorizontalCalculationViewModel.MaximumNewtonIterations);
    }

    /// <summary>
    /// <b>本題。</b> 追加実行は、前の結果と同じ版のときだけできる。版が違えば同じ入力・設定でも結果が変わりうるので、
    /// 別の版の結果を並べると「同じ入力で結果が変わった」ときに条件の差かプログラムの差かを切り分けられない。
    /// </summary>
    [TestMethod]
    public void IncrementalRuns_RequireTheSameProgramVersion()
    {
        string vm = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs");
        StringAssert.Contains(vm, "if (prev.AppVersion != MainWindowViewModel.AppVersion)");
        StringAssert.Contains(vm, "AppVersion = MainWindowViewModel.AppVersion,");
        Assert.IsNull(new AnalysisRunSnapshot().AppVersion, "(前提) 以前の版の結果は版の記録を持たない");
    }

    private static MainWindowViewModel HorizontalDone(out PileDesign.Models.AnalysisRunMetadata horizontal)
    {
        var input = new PileDesign.Models.InputData.InputModel();
        var vm = new MainWindowViewModel { CurrentInputModel = input };
        input.AttachViewModel(vm);
        vm.CurrentModel = new AnaModel();
        vm.IsHorizontalAnalysisDone = true;
        horizontal = new PileDesign.Models.AnalysisRunMetadata
        {
            Kind = PileDesign.Models.AnalysisKind.Horizontal, ConvergenceMethod = "ラインサーチ", MaximumIterations = 100,
        };
        vm.CaptureAnalysisResultSet(horizontal);
        return vm;
    }

    private static PileDesign.Models.AnalysisRunMetadata Settlement() => new()
    {
        Kind = PileDesign.Models.AnalysisKind.GroupSettlement,
        Conditions = [new("荷重の置き方", "全体矩形")],
    };

    /// <summary>
    /// <b>本題。</b> 水平解析のあとに沈下解析を実行しても、水平解析の条件の記録は残る。
    /// 控えは解析のたびに取り直すので、以前は取り直した控えに水平解析の記録が引き継がれず消えていた
    /// (沈下解析の条件は、もともと記録していなかった)。
    /// </summary>
    [TestMethod]
    public void RunningSettlementAfterHorizontal_KeepsTheHorizontalRecord()
    {
        var vm = HorizontalDone(out var horizontal);
        vm.IsGroupPileSettlementAnalysisDone = true;
        vm.CaptureAnalysisResultSet(Settlement());

        var set = vm.CurrentResultSet!;
        Assert.AreSame(horizontal, set.RecordOf(PileDesign.Models.AnalysisKind.Horizontal), "水平解析の条件が消えた");
        Assert.IsNotNull(set.RecordOf(PileDesign.Models.AnalysisKind.GroupSettlement));

        // 同じ種類をもう一度実行したら差し替える (並べない)
        var again = Settlement();
        vm.CaptureAnalysisResultSet(again);
        Assert.AreEqual(2, vm.CurrentResultSet!.RunRecords.Count);
        Assert.AreSame(again, vm.CurrentResultSet.RecordOf(PileDesign.Models.AnalysisKind.GroupSettlement));
    }

    /// <summary>
    /// 水平解析の入力を編集してから沈下だけ再実行すると、控えは取り直さない (解析時の結果と編集後の入力を組にしない)。
    /// その経路でも、いま実行した沈下解析の条件は記録する。
    /// </summary>
    [TestMethod]
    public void SettlementRecordIsKept_EvenWhenTheSnapshotIsNotRetaken()
    {
        var vm = HorizontalDone(out var horizontal);
        var before = vm.CurrentResultSet;
        vm.MarkInputChangedSinceAnalysis(MainWindowViewModel.AnalysisInputScope.Model);
        vm.IsGroupPileSettlementAnalysisDone = true;
        vm.CaptureAnalysisResultSet(Settlement());

        Assert.AreSame(before, vm.CurrentResultSet, "(前提) 控えは取り直さない経路");
        Assert.AreSame(horizontal, vm.CurrentResultSet!.RecordOf(PileDesign.Models.AnalysisKind.Horizontal));
        Assert.IsNotNull(vm.CurrentResultSet.RecordOf(PileDesign.Models.AnalysisKind.GroupSettlement), "沈下解析の条件が記録されていない");
    }

    /// <summary>結果が消えた種類の記録は引き継がない (結果の無い解析の条件を計算書に書かない)。</summary>
    [TestMethod]
    public void RecordsOfKindsWithoutResults_AreDropped()
    {
        var vm = HorizontalDone(out _);
        vm.IsHorizontalAnalysisDone = false;
        vm.IsGroupPileSettlementAnalysisDone = true;
        vm.CaptureAnalysisResultSet(Settlement());
        Assert.IsNull(vm.CurrentResultSet!.RecordOf(PileDesign.Models.AnalysisKind.Horizontal));
    }

    /// <summary>
    /// 計算書: 実行条件は表紙に並べず、「計算条件・仮定」の章に解析の種類ごとの表で書く (表の題に種類と実行した時刻)。
    /// 表紙に並べると文字で埋まってモデル図が押し出され、どの条件がどの解析のものかも読みにくかった。
    /// 表紙に残すのは解析の時刻・入力の識別・解いたケースの数・解析のあとの編集だけ。
    /// </summary>
    [TestMethod]
    public void TheReportShowsConditionsPerAnalysisKind_InTheAssumptionsChapter()
    {
        var vm = HorizontalDone(out _);
        vm.IsGroupPileSettlementAnalysisDone = true;
        vm.CaptureAnalysisResultSet(Settlement());

        string cover = vm.DescribeAnalysisConditions()!;
        Assert.IsFalse(cover.Contains("ラインサーチ") || cover.Contains("荷重の置き方") || cover.Contains('\n'),
            "表紙に実行条件が並んでいます: " + cover);

        var source = vm.CaptureReportSource(vm.CurrentInputModel!, wantsFactoredEvaluation: false);
        CollectionAssert.AreEqual(new[] { PileDesign.Models.AnalysisKind.Horizontal, PileDesign.Models.AnalysisKind.GroupSettlement },
            source.RunRecords.Select(r => r.Kind).ToArray());

        // 種類ごとの表: 行は項目と値に分かれ、ほかの種類の条件は混ざらない
        var body = new DocumentFormat.OpenXml.Wordprocessing.Body();
        PileDesign.Output.WordDocument.AddItemValueTable(body, source.RunRecords[1].DescribeRows());
        var cells = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.TableCell>().Select(c => c.InnerText).ToList();
        CollectionAssert.AreEqual(new[] { "項目", "値", "荷重の置き方", "全体矩形" }, cells);

        string assumptions = TestSource.Read("Graphics_r1", "Output", "WordDocument.Assumptions.cs");
        StringAssert.Contains(TestSource.MethodBody(assumptions, "private void AddCalculationAssumptionsSection("), "AddRunRecordsSection(body);");
        StringAssert.Contains(TestSource.MethodBody(assumptions, "private void AddRunRecordsSection("),
            "AddTableCaption(body, \"解析の実行条件: \" + record.Heading());");
        string cover2 = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Output", "WordDocument.cs"), "private void AddFrontMatter(");
        Assert.IsFalse(cover2.Contains("RunRecords"), "表紙に実行条件を書いています");
    }

    /// <summary>水平解析以外の解析も、実行した箇所で条件を渡す (渡し忘れると、その解析の条件だけが残らない)。</summary>
    [TestMethod]
    public void EveryAnalysisPassesItsConditions()
    {
        string vb = TestSource.Read("Graphics_r1", "ViewModels", "VerticalBeamCalculationViewModel.cs");
        StringAssert.Contains(vb, "_mainWindowViewModel.CaptureAnalysisResultSet(runSettings);");
        StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "SettlementViewModel.cs"),
            "mainWindowViewModel.CaptureAnalysisResultSet(DescribeRun(InputModel.ElementDivision.SoilPiles));");
        StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.ModelEditing.cs"),
            "CaptureAnalysisResultSet(DescribeGroupSettlementRun(pgs));");

        // 本体で引数なしに呼んでいる箇所が無いこと (呼ぶと、その解析の条件だけが記録されない)
        int scanned = 0;
        foreach (var file in System.IO.Directory.EnumerateFiles(TestSource.Dir("Graphics_r1"), "*.cs", System.IO.SearchOption.AllDirectories))
        {
            if (file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}")) continue;
            scanned++;
            Assert.IsFalse(System.IO.File.ReadAllText(file).Contains("CaptureAnalysisResultSet();"),
                $"{System.IO.Path.GetFileName(file)}: 解析の条件を渡さずに結果の控えを取っています");
        }
        TestSource.AssertScanned(scanned, 100, "本体のソース");

        var vm = new VerticalBeamCalculationViewModel(new MainWindowViewModel()) { AnalyzeLevel2 = false, LoadStepsCount = 7 };
        var record = vm.CaptureRunSettings();
        Assert.AreEqual(PileDesign.Models.AnalysisKind.VerticalBeam, record.Kind);
        StringAssert.Contains(record.DescribeSettings(), "荷重ステップ数 7");
        StringAssert.Contains(record.DescribeSettings(), "荷重 VL・L1");

        var single = SettlementViewModel.DescribeRun([new PileDesign.Models.InputData.SoilPile { No = 2, Dp = 1200 }]);
        Assert.AreEqual(PileDesign.Models.AnalysisKind.SingleSettlement, single.Kind);
        StringAssert.Contains(single.DescribeSettings(), "杭セット2");
        StringAssert.Contains(single.DescribeSettings(), "杭先端径 1200 mm");
    }

    /// <summary>
    /// 計算書: 解析した版といま出力している版が違えば注意する (応答値は解析した版、限界曲線はいまの版になる)。
    /// 版の記録の無い以前の結果では言わない (比べようがない)。
    /// </summary>
    [TestMethod]
    public void TheReportNotesADifferentProgramVersion()
    {
        var input = new PileDesign.Models.InputData.InputModel();
        var diffs = PileDesign.Output.WordDocument.CollectAnalysisConditionDiffs(new AnalysisRunSnapshot { AppVersion = "0.9.0-beta" }, input);
        Assert.IsTrue(diffs.Exists(d => d.Contains("解析プログラムの版（解析時: 0.9.0-beta")));
        Assert.IsFalse(PileDesign.Output.WordDocument.CollectAnalysisConditionDiffs(new AnalysisRunSnapshot(), input)
            .Exists(d => d.Contains("解析プログラムの版")), "版の記録の無い結果で版の違いを言っています");
        Assert.IsFalse(PileDesign.Output.WordDocument.CollectAnalysisConditionDiffs(
            new AnalysisRunSnapshot { AppVersion = PileDesign.Common.AppInfo.Version }, input).Exists(d => d.Contains("解析プログラムの版")));
    }
}
