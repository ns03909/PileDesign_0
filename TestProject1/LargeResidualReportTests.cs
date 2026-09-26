using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 連立方程式の解の相対残差が大きかった回数を、水平解析のログとサマリーに出すこと。
    ///
    /// 解は差し替えずに使う (外側の反復の収束判定で精度を確保する) が、回数が多いモデルは条件が悪い手掛かりになる。
    /// 計算例 3-5 は荷重が大きい段階で残差が 1e-6 を超える解が出る (2026-09-26 に 126 回を確認)。
    /// </summary>
    [TestClass]
    public class LargeResidualReportTests
    {
        [TestMethod]
        public void TheMessageIsEmptyWhenThereAreNone()
        {
            Assert.AreEqual(0, HorizontalCalculationViewModel.DescribeLargeResidualSolves(0, 0).Count());
            var lines = HorizontalCalculationViewModel.DescribeLargeResidualSolves(126, 9).ToList();
            StringAssert.Contains(lines[0], "相対残差が 1E-6 を超えた反復 126 回 (9 ステップ)");
            StringAssert.Contains(lines[1], "解はそのまま使い");
        }

        /// <summary>計算例 3-5 を解き、ステップごとの数え上げとサマリーの行が出ること。</summary>
        [TestMethod]
        public void TheSummaryReportsLargeResidualSolves()
        {
            HorizontalCalculationViewModel? hcvm = null;
            try
            {
                HeadlessHorizontalRunner.RunExample("Example3_5", "PileExample3_5", new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 4, Level2Steps = 16, UseLineSearch = true, Parallelism = 1,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Both,
                    Configure = h => hcvm = h,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive("例題ファイルなし");
                return;
            }

            var steps = hcvm!.StepSummariesSnapshot().ToArray();
            long total = steps.Sum(s => s.LargeResidualSolves);
            Assert.IsTrue(total > 0, "(前提) 計算例 3-5 で残差の大きい解が出ること (出なくなったら例題を替える)");

            string report = HorizontalCalculationViewModel.BuildStepSummaryReportText(steps);
            StringAssert.Contains(report, $"を超えた反復 {total} 回", "サマリーに残差の大きい解の回数が出ていません");
        }
    }
}
