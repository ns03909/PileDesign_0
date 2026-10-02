using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.ViewModels;
using System;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 収束しなかったステップに、原因に辿り着く手掛かり (理由・残差の推移と傾向・残差の大きい箇所 = 杭・深さ・ばね) が残り、
    /// 解析のまとめ (ログ・計算書に載る文) に出ること。
    ///
    /// <para>以前は未収束を「ケース・ステップ・最終残差」だけで知らせていた。どこで釣り合わないのかは開発用のログにしか出なかった。</para>
    /// </summary>
    [TestClass]
    public class UnconvergedDiagnosisTests
    {
        [DataTestMethod]
        [DataRow(new[] { 1e-2, 5e-3, 2e-3, 8e-4 }, "減少中 (反復が足りない)")]
        [DataRow(new[] { 1e-3, 3e-3, 9e-3 }, "増加 (発散)")]
        [DataRow(new[] { 1e-3, 2e-3, 1.1e-3, 2.1e-3, 1.0e-3, 1.9e-3 }, "振動 (行き来している)")]
        [DataRow(new[] { 1e-3, 0.98e-3, 0.97e-3, 0.95e-3, 0.94e-3 }, "停滞")]
        [DataRow(new[] { 1e-3, double.NaN, 1e-3 }, "数値でない")]
        [DataRow(new[] { 1e-3 }, "反復が少ない")]
        public void TheTrendIsClassified(double[] trend, string expected)
            => Assert.AreEqual(expected, UnconvergedDiagnosis.ClassifyTrend(trend));

        [TestMethod]
        public void TheTrendKeepsTheLastIterationsOnly()
        {
            var d = UnconvergedDiagnosis.Build("理由", Enumerable.Range(1, 30).Select(i => (double)i).ToArray(), []);
            Assert.AreEqual(UnconvergedDiagnosis.TrendLength, d.ResidualTrend.Count);
            Assert.AreEqual(30.0, d.ResidualTrend[^1]);
        }

        /// <summary>
        /// 反復の上限を 2 回にして、未収束の経路を確実に通す。未収束のステップには手掛かりがあり、
        /// 残差の大きい箇所は実在の杭を指し、まとめの文に出る。収束したステップには手掛かりを付けない。
        /// </summary>
        [TestMethod]
        [Timeout(600000)]
        public void UnconvergedSteps_CarryTheirCause_IntoTheSummary()
        {
            HorizontalCalculationViewModel? hcvm = null;
            MainWindowViewModel vm;
            try
            {
                vm = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 2,
                    Level2Steps = 2,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.None,
                    UseLineSearch = false,
                    Parallelism = 1,
                    Configure = h => { hcvm = h; h.MaximumIterationsForTesting = 2; },
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }

            var steps = hcvm!.StepSummariesSnapshot();
            var failed = steps.Where(s => s.Status >= StepStatus.Unconverged).ToList();
            TestSource.AssertScanned(failed.Count, 1, "収束しなかったステップ");

            int pileCount = vm.CurrentInputModel!.PileLayoutItems.Count;
            foreach (var s in failed)
            {
                Assert.IsNotNull(s.Diagnosis, $"{s.CaseTag} step {s.Step}: 収束しなかったのに手掛かりがありません");
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.Diagnosis!.Reason));
                Assert.IsTrue(s.Diagnosis.ResidualTrend.Count >= 1, "残差の推移がありません");
                Assert.AreEqual(s.FinalResidual, s.Diagnosis.ResidualTrend[^1], "推移の最後が最終の残差と違います");
                Assert.IsTrue(s.Diagnosis.Hotspots.Count >= 1, "残差の大きい箇所がありません");
                var ranked = s.Diagnosis.Hotspots.Select(h => Math.Abs(h.Residual)).ToArray();
                CollectionAssert.AreEqual(ranked.OrderByDescending(r => r).ToArray(), ranked, "残差の大きい順に並んでいません");
                foreach (var h in s.Diagnosis.Hotspots.Where(h => h.PileNo != null))
                    Assert.IsTrue(h.PileNo >= 1 && h.PileNo <= pileCount, $"杭番号 {h.PileNo} が杭の範囲 (1〜{pileCount}) にありません");
            }
            Assert.IsTrue(failed.SelectMany(s => s.Diagnosis!.Hotspots).Any(h => h.PileNo != null), "残差の大きい箇所がどの杭も指していません");
            Assert.IsTrue(steps.Where(s => s.Status < StepStatus.Unconverged).All(s => s.Diagnosis == null), "収束したステップに手掛かりが付いています");

            string report = HorizontalCalculationViewModel.BuildStepSummaryReportText(steps.ToArray());
            Console.WriteLine(report);
            StringAssert.Contains(report, "残差の大きい箇所:");
            StringAssert.Contains(report, "傾向:");
            StringAssert.Contains(report, failed[0].Diagnosis!.Hotspots[0].Describe());
        }
    }
}
