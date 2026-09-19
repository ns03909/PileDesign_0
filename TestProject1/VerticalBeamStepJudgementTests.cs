using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 基礎梁鉛直解析の荷重ステップの判定
    /// (<see cref="VerticalBeamCalculationViewModel.JudgeStep"/>)。
    ///
    /// <para>残差が基準を下回れば収束。上回っていても基準の 1000 倍以内なら受理し、ケースの
    /// 収束の印は保つ (ステップ自身は未収束として記録する)。この緩い受理は元からの規則。</para>
    ///
    /// <para>2026-09-19 まで判定が <c>residual &gt; 許容値 × 1000</c> という比較だけだったため、
    /// 残差が NaN のときどの比較も偽になってすり抜け、「ステップは未収束なのにケースは収束」の
    /// まま計算書の収束状態に出ていた。ケースの印は計算書 (収束状態) と保存ファイルへ届く。</para>
    /// </summary>
    [TestClass]
    public class VerticalBeamStepJudgementTests
    {
        private const double Tol = 1e-6;
        private const int MaxIter = 100;

        private static (bool Converged, bool CaseStaysConverged, string? Reason) Judge(double residual)
            => VerticalBeamCalculationViewModel.JudgeStep(residual, Tol, MaxIter);

        /// <summary>基準を下回れば収束。理由は付けない。</summary>
        [TestMethod]
        public void BelowTheToleranceIsConverged()
        {
            var j = Judge(Tol * 0.5);
            Assert.IsTrue(j.Converged);
            Assert.IsTrue(j.CaseStaysConverged);
            Assert.IsNull(j.Reason);
        }

        /// <summary>基準ちょうどは収束にしない (判定は厳密不等号)。</summary>
        [TestMethod]
        public void ExactlyAtTheToleranceIsNotConverged()
        {
            Assert.IsFalse(Judge(Tol).Converged);
        }

        /// <summary>
        /// 基準の 1000 倍以内なら、ステップは未収束と記録しつつケースの印は保つ。
        /// </summary>
        [TestMethod]
        public void WithinThousandTimesTheCaseStaysConverged()
        {
            var j = Judge(Tol * 1000);
            Assert.IsFalse(j.Converged, "ステップ自身は未収束");
            Assert.IsTrue(j.CaseStaysConverged, "1000 倍ちょうどまでは受理する");
            StringAssert.Contains(j.Reason, "最大反復回数");
        }

        /// <summary>1000 倍を超えたらケースの印を落とす。</summary>
        [TestMethod]
        public void BeyondThousandTimesTheCaseBecomesUnconverged()
        {
            var j = Judge(Tol * 1001);
            Assert.IsFalse(j.Converged);
            Assert.IsFalse(j.CaseStaysConverged);
        }

        /// <summary>
        /// <b>本題。</b> 残差が NaN なら受理しない。比較だけで書くと NaN がすり抜けて
        /// 「ステップは未収束なのにケースは収束」になる。
        /// </summary>
        [TestMethod]
        public void NotANumberNeverKeepsTheCaseConverged()
        {
            var j = Judge(double.NaN);
            Assert.IsFalse(j.Converged, "NaN は収束ではない");
            Assert.IsFalse(j.CaseStaysConverged, "NaN でケースの印が残ってはいけない");
            StringAssert.Contains(j.Reason, "数値でなくなりました");
        }

        /// <summary>∞ も同じ。</summary>
        [TestMethod]
        public void InfinityNeverKeepsTheCaseConverged()
        {
            Assert.IsFalse(Judge(double.PositiveInfinity).CaseStaysConverged);
            StringAssert.Contains(Judge(double.PositiveInfinity).Reason, "数値でなくなりました");
        }
    }
}
