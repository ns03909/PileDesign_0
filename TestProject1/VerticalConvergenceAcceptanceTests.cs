using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;

namespace TestProject1
{
    /// <summary>
    /// 鉛直の荷重伝達法 (荷重-沈下曲線) の収束受理の規則
    /// (<see cref="VerticalLoadTransferMethod.RelaxedTolerance"/> /
    /// <see cref="VerticalLoadTransferMethod.AcceptAtIterationLimit"/>)。
    ///
    /// <para>反復が 100 回を超えると許容値を段階的に緩め (最大 100 倍)、200 回に達したら残差が
    /// 元の許容値の 1000 倍以内なら受理する。この受理は 2026-09-19 まで無音で、曲線の精度が
    /// 落ちていても沈下量がそのまま検定・計算書へ流れていた。受理の規則そのものは変えず、
    /// 受理した段階を <c>Warnings</c> に集約して見せるようにした。ここでは規則の形を固定する。</para>
    /// </summary>
    [TestClass]
    public class VerticalConvergenceAcceptanceTests
    {
        private const double Tol = 1e-6;

        /// <summary>100 反復までは許容値を緩めない。</summary>
        [TestMethod]
        public void ToleranceIsNotRelaxedInTheFirstHundredIterations()
        {
            Assert.AreEqual(Tol, VerticalLoadTransferMethod.RelaxedTolerance(Tol, 1), 1e-30);
            Assert.AreEqual(Tol, VerticalLoadTransferMethod.RelaxedTolerance(Tol, 100), 1e-30);
        }

        /// <summary>101 回目から 1 回ごとに 0.5 倍ずつ増える (101 回で 1.5 倍、120 回で 11 倍)。</summary>
        [TestMethod]
        public void ToleranceGrowsByHalfPerIterationAfterHundred()
        {
            Assert.AreEqual(1.5 * Tol, VerticalLoadTransferMethod.RelaxedTolerance(Tol, 101), 1e-30);
            Assert.AreEqual(11.0 * Tol, VerticalLoadTransferMethod.RelaxedTolerance(Tol, 120), 1e-30);
        }

        /// <summary>緩めるのは 100 倍まで (298 回で到達し、以降は増えない)。</summary>
        [TestMethod]
        public void ToleranceRelaxationIsCappedAtHundredTimes()
        {
            Assert.AreEqual(100.0 * Tol, VerticalLoadTransferMethod.RelaxedTolerance(Tol, 298), 1e-30);
            Assert.AreEqual(100.0 * Tol, VerticalLoadTransferMethod.RelaxedTolerance(Tol, 1000), 1e-30);
        }

        /// <summary>反復上限では、残差が元の許容値の 1000 倍以内なら受理、超えたら不受理。</summary>
        [TestMethod]
        public void AtTheIterationLimitUpToThousandTimesIsAccepted()
        {
            Assert.IsTrue(VerticalLoadTransferMethod.AcceptAtIterationLimit(1000 * Tol, Tol), "1000 倍ちょうどは受理");
            Assert.IsFalse(VerticalLoadTransferMethod.AcceptAtIterationLimit(1001 * Tol, Tol), "1000 倍超は不受理");
            Assert.IsTrue(VerticalLoadTransferMethod.AcceptAtIterationLimit(0.5 * Tol, Tol));
        }

        /// <summary>残差が NaN・∞ なら受理しない (比較が偽になるので自然にそうなる)。</summary>
        [TestMethod]
        public void NotANumberIsNeverAccepted()
        {
            Assert.IsFalse(VerticalLoadTransferMethod.AcceptAtIterationLimit(double.NaN, Tol));
            Assert.IsFalse(VerticalLoadTransferMethod.AcceptAtIterationLimit(double.PositiveInfinity, Tol));
        }
    }
}
