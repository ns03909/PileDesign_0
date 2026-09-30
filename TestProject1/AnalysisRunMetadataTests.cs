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
