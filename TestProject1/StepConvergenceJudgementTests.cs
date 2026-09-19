using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// ステップが収束したかの判定 (<see cref="HorizontalCalculationViewModel.JudgeStepConvergence"/>)。
    ///
    /// <para>NR 反復ループの抜け方は 3 通りある。(1) 残差が実効基準を下回った、(2) 反復上限に達した、
    /// (3) リミットサイクルとして早期に打ち切った。<b>収束したかどうかは抜け方ではなく残差で決める。</b></para>
    ///
    /// <para>2026-09-19 まで判定が <c>!(反復数 &gt; 上限 &amp;&amp; 残差 ≧ 基準)</c> だったため、(3) で抜けた
    /// ステップは「反復上限に達していない」だけで収束と記録されていた。早期打ち切りは残差が実効基準
    /// 以上のときだけ通る道なので、収束のはずがない。残差が NaN になった場合も同じ穴に落ちていた
    /// (NaN との比較はすべて false なので、ループは「基準を満たした」ように抜ける)。</para>
    ///
    /// <para>この印は回転ばねの履歴確定・内力基準の時間平均・ステップ状態 (検定・計算書・保存ファイル)
    /// まで届く。収束扱いのまま流れると、振動が収まっていない状態が結果として採用される。</para>
    /// </summary>
    [TestClass]
    public class StepConvergenceJudgementTests
    {
        private const double Alpha = 1e-6;   // 実効収束基準
        private const int MaxIterations = 100;

        private static (bool Converged, string? Reason) Judge(double residual, int nIteration = 50,
            double effectiveAlpha = Alpha, int maxIterations = MaxIterations)
            => HorizontalCalculationViewModel.JudgeStepConvergence(residual, effectiveAlpha, nIteration, maxIterations);

        /// <summary>残差が基準を下回っていれば収束。反復数がいくつでも変わらない。</summary>
        [TestMethod]
        public void BelowTheCriterionIsConvergedWhateverTheIterationCount()
        {
            foreach (int iter in new[] { 1, 50, 100, 101, 1000 })
            {
                var (converged, reason) = Judge(1e-7, iter);
                Assert.IsTrue(converged, $"反復 {iter} 回で残差が基準未満なら収束");
                Assert.IsNull(reason, "収束したときに理由は付けない");
            }
        }

        /// <summary>基準ちょうどは収束にしない (判定は厳密不等号)。</summary>
        [TestMethod]
        public void ExactlyAtTheCriterionIsNotConverged()
        {
            Assert.IsFalse(Judge(Alpha).Converged);
        }

        /// <summary>
        /// 反復上限に達して残差が残っていれば未収束。理由に上限の回数を書く。
        /// </summary>
        [TestMethod]
        public void HittingTheIterationLimitIsUnconvergedAndSaysSo()
        {
            var (converged, reason) = Judge(1e-3, MaxIterations + 1);
            Assert.IsFalse(converged);
            StringAssert.Contains(reason, "最大反復回数");
            StringAssert.Contains(reason, "100");
        }

        /// <summary>
        /// <b>本題。</b> 反復上限に達していないのに残差が残っている = リミットサイクルの早期打ち切り。
        /// これを収束扱いにしていたのが 2026-09-19 に直した不具合。
        /// </summary>
        [TestMethod]
        public void LeavingEarlyWithResidualLeftIsUnconvergedNotConverged()
        {
            var (converged, reason) = Judge(1e-3, nIteration: 42);
            Assert.IsFalse(converged, "反復上限前に抜けても、残差が残っていれば収束ではない");
            StringAssert.Contains(reason, "リミットサイクル");
        }

        /// <summary>
        /// 残差が NaN になったら未収束。NaN との比較はすべて false になるため、
        /// 反復ループは「基準を満たした」ように抜ける。残差で判定すれば NaN は収束にならない。
        /// </summary>
        [TestMethod]
        public void NotANumberIsUnconverged()
        {
            var (converged, reason) = Judge(double.NaN);
            Assert.IsFalse(converged, "NaN は収束ではない");
            StringAssert.Contains(reason, "数値でなくなりました");
        }

        /// <summary>∞ も同じ。反復上限に達していても、理由は「数値でない」を優先する。</summary>
        [TestMethod]
        public void InfinityIsUnconvergedAndItsReasonWins()
        {
            Assert.IsFalse(Judge(double.PositiveInfinity).Converged);
            StringAssert.Contains(Judge(double.PositiveInfinity, MaxIterations + 1).Reason, "数値でなくなりました");
        }

        /// <summary>
        /// 緩めた基準でも規則は同じ。緩和後の基準を下回っていれば収束、上回っていれば未収束。
        /// </summary>
        [TestMethod]
        public void TheRelaxedCriterionIsUsedAsIs()
        {
            Assert.IsTrue(Judge(5e-3, effectiveAlpha: 1e-2).Converged, "緩めた基準を下回れば収束");
            Assert.IsFalse(Judge(5e-2, effectiveAlpha: 1e-2).Converged, "緩めた基準でも上回れば未収束");
        }

        /// <summary>簡易法 (反復 1 回) でも同じ規則で決まる。</summary>
        [TestMethod]
        public void TheSingleIterationModeFollowsTheSameRule()
        {
            Assert.IsTrue(Judge(1e-9, nIteration: 2, maxIterations: 1).Converged);
            Assert.IsFalse(Judge(1e-3, nIteration: 2, maxIterations: 1).Converged);
        }
    }
}
