using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1.ConvergenceRegression
{
    /// <summary>
    /// 2026-09-12 の収束研究で入れた 3 点を固定する。
    ///
    /// <list type="number">
    /// <item><b>混成則</b>: 残差比 &gt; 1e-2 の間は修正 NR でも K を再利用しない。地盤変位を入れた
    ///   液状化ケースは、全杭頭が同時にひび割れたあと古い接線で停滞し、初回試行のステップ 1 で
    ///   必ず失敗して再試行に回っていた (計算例9 L1: 200 反復・再試行 1 回 → 64 反復・再試行なし)。</item>
    /// <item><b>緩和受理の区別</b>: 残差 1e-6 に届かず緩めた基準で受理したステップは
    ///   <see cref="PileDesign.FEM.StepStatus.ConvergedRelaxed"/> で記録する (収束と同じ印にしない)。</item>
    /// <item><b>履歴変数はステップ収束後に確定</b>: ひび割れの記憶・方向ロックを反復途中の状態で
    ///   確定させない。修正 NR と Full NR で杭頭変位が 0.4% 違っていた (経路依存) のを無くす。</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class NewtonPathIndependenceTests
    {
        private static HeadlessHorizontalRunner.RunOptions Options(bool modifiedNewton) => new()
        {
            Level1Steps = 4,
            Level2Steps = 8,
            LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
            UseLineSearch = true,
            Parallelism = 1,
            Configure = h => h.UseModifiedNewtonRaphson = modifiedNewton,
        };

        /// <summary>計算例9 (場所打ち RC・液状化・地盤変位あり) が再試行なしで全ステップ収束すること。</summary>
        [TestMethod]
        public void Example9ConvergesWithoutRetryUnderTheHybridRule()
        {
            ConvergenceSnapshot snap;
            try { snap = HeadlessHorizontalRunner.RunExample("Example9", "PileExample9", Options(modifiedNewton: true)); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗")) { Assert.Inconclusive(ex.Message); return; }

            Assert.IsTrue(snap.Cases.Count >= 2, "解析ケースが走っていません");
            foreach (var c in snap.Cases)
            {
                Assert.AreEqual(0, c.BisectionRetries, $"{c.CaseKey}: 再試行が起きています (混成則が効いていない)");
                Assert.IsTrue(c.Converged, $"{c.CaseKey}: 未収束のステップがあります (最終残差 {c.FinalResidual:E2})");
                Assert.IsTrue(c.RelaxedSteps <= 1, $"{c.CaseKey}: 緩和受理が {c.RelaxedSteps} ステップあります");
                Assert.IsTrue(c.FinalResidual < 1e-5, $"{c.CaseKey}: 最終残差 {c.FinalResidual:E2}");
            }
        }

        /// <summary>
        /// 同じ入力を修正 NR と Full NR で解いたとき、収束解が一致すること。
        /// 履歴変数を反復途中で確定していた頃は 0.35〜0.5% 違っていた。
        /// </summary>
        [TestMethod]
        public void ModifiedAndFullNewtonReachTheSameEquilibrium()
        {
            ConvergenceSnapshot mnr, full;
            try
            {
                mnr = HeadlessHorizontalRunner.RunExample("ExampleK8", "PileExampleK8", Options(modifiedNewton: true));
                full = HeadlessHorizontalRunner.RunExample("ExampleK8", "PileExampleK8", Options(modifiedNewton: false));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗")) { Assert.Inconclusive(ex.Message); return; }

            Assert.AreEqual(mnr.Cases.Count, full.Cases.Count);
            for (int i = 0; i < mnr.Cases.Count; i++)
            {
                var a = mnr.Cases[i]; var b = full.Cases[i];
                Assert.IsTrue(a.Converged && b.Converged, $"{a.CaseKey}: 未収束のステップがあります");
                double rel = Math.Abs(a.MaxAbsHorizDisp - b.MaxAbsHorizDisp) / Math.Max(Math.Abs(b.MaxAbsHorizDisp), 1e-12);
                Assert.IsTrue(rel < 1e-3,
                    $"{a.CaseKey}: 修正 NR {a.MaxAbsHorizDisp:E6} と Full NR {b.MaxAbsHorizDisp:E6} の最大変位が {rel:P3} 違います。"
                    + "収束解が解法の経路に依存しています (履歴変数が反復途中で確定している)");
            }
        }

        /// <summary>混成則の閾値が仕上げ域を残す値であること (常時 Full NR にしない)。</summary>
        [TestMethod]
        public void TheHybridThresholdLeavesAFinishingRegionForModifiedNewton()
        {
            Assert.AreEqual(1e-2, HorizontalCalculationViewModel.FullNRAboveResidualRatio, 0.0);
        }
    }
}
