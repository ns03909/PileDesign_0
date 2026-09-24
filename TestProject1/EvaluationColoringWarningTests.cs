using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;

namespace TestProject1
{
    /// <summary>
    /// 検定の組み立てに失敗したとき、キャンバスの色分けでもそれを知らせること。
    ///
    /// 失敗した検定はまとめに入らないので、色分けは組めた検定 (たとえば支持力) だけで塗られる。
    /// 以前は失敗をダッシュボードにしか出さず、杭頭の緑の印などがそのまま残って、
    /// 水平解析も含めて OK と読めた。
    /// </summary>
    [TestClass]
    public class EvaluationColoringWarningTests
    {
        private static PileEvaluationSummary WithFailures(params string[] parts)
            => PileEvaluationSummary.FromResults(null, null, new EvaluationResult([]), "A",
                Array.ConvertAll(parts, p => new PileEvaluationSummary.BuildFailure(p, "試験用の失敗")));

        [TestMethod]
        public void NoFailure_NoWarning()
        {
            Assert.IsNull(MainWindowViewModel.DescribeColoringFailure(WithFailures(), horizontalDone: true));
        }

        [TestMethod]
        public void AFailedHorizontalEvaluation_IsNamedInTheWarning()
        {
            string? warning = MainWindowViewModel.DescribeColoringFailure(
                WithFailures(PileEvaluationSummary.HorizontalPart), horizontalDone: true);

            Assert.IsNotNull(warning, "水平解析の検定が組めなかったのに、色分けに注意書きが出ません");
            StringAssert.Contains(warning, PileEvaluationSummary.HorizontalPart);
            StringAssert.Contains(warning, "OK ではありません");
        }

        [TestMethod]
        public void BothFailures_AreNamed()
        {
            string? warning = MainWindowViewModel.DescribeColoringFailure(
                WithFailures(PileEvaluationSummary.HorizontalPart, PileEvaluationSummary.BearingPart), horizontalDone: true);

            StringAssert.Contains(warning, PileEvaluationSummary.HorizontalPart);
            StringAssert.Contains(warning, PileEvaluationSummary.BearingPart);
        }

        /// <summary>水平解析をしていなければ、水平解析の失敗は数えない (組もうとしていない)。</summary>
        [TestMethod]
        public void HorizontalFailureIsIgnoredWhenTheAnalysisHasNotRun()
        {
            Assert.IsNull(MainWindowViewModel.DescribeColoringFailure(
                WithFailures(PileEvaluationSummary.HorizontalPart), horizontalDone: false));
            Assert.IsNotNull(MainWindowViewModel.DescribeColoringFailure(
                WithFailures(PileEvaluationSummary.BearingPart), horizontalDone: false));
        }

        /// <summary>
        /// 失敗を含むまとめを受け取ったら凡例の注意書きが立ち、失敗の無いまとめで消えること。
        /// 画面の凡例はこの 2 つのプロパティに結んでいる。
        /// </summary>
        [TestMethod]
        public void TheViewModelRaisesAndClearsTheWarning()
        {
            var vm = new MainWindowViewModel { CurrentModel = new AnaModel(), IsHorizontalAnalysisDone = true };
            Assert.IsFalse(vm.HasEvaluationColoringWarning);

            var changed = new System.Collections.Generic.List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            vm.AdoptEvaluationSummary(WithFailures(PileEvaluationSummary.HorizontalPart));
            Assert.IsTrue(vm.HasEvaluationColoringWarning, "検定が組めなかったのに、凡例の注意書きが立っていません");
            StringAssert.Contains(vm.EvaluationColoringWarning, PileEvaluationSummary.HorizontalPart);
            CollectionAssert.Contains(changed, nameof(MainWindowViewModel.HasEvaluationColoringWarning),
                "表示を切り替える通知が出ていません (凡例に出ない)");

            vm.AdoptEvaluationSummary(WithFailures());
            Assert.IsFalse(vm.HasEvaluationColoringWarning, "組めるようになったのに、注意書きが残っています");
        }

        /// <summary>まとめを作り直す所が、注意書きを決める所 (AdoptEvaluationSummary) を通ること。</summary>
        [TestMethod]
        public void RebuildingTheSummaryGoesThroughAdopt()
        {
            string body = TestSource.MethodBody(
                TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.EvaluationColoring.cs"),
                "public PileEvaluationSummary GetEvaluationSummary(bool force = false)");
            StringAssert.Contains(body, "AdoptEvaluationSummary(PileEvaluationSummary.Build(this));");
            Assert.IsFalse(body.Contains("_evaluationSummaryCache = ", StringComparison.Ordinal),
                "まとめを直接差し替えています (凡例の注意書きが更新されない)");

            string tooltip = TestSource.Read("Graphics_r1", "Views", "MainWindow.CanvasResultsTooltips.cs");
            StringAssert.Contains(tooltip, "MainWindowViewModel.DescribeColoringFailure(",
                "検定比のツールチップが、組めなかった検定を知らせていません");
        }
    }
}
