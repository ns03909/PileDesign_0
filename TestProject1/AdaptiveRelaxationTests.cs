using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 適応緩和係数 ω の更新規則 (<see cref="HorizontalCalculationViewModel.UpdateAdaptiveRelaxation"/>)。
    ///
    /// <para>ω は NR の変位増分にかける係数で、残差比 (今回/前回) を見て毎反復決め直す。
    /// 反復回数にそのまま効くため、規則を書き換えると収束スナップショットが動く。
    /// ここで 4 つの分岐と上下限を固定し、意図しない書き換えを落とす。</para>
    ///
    /// <para>規則は 2026-09-19 に反復本体から純粋関数として切り出した。切り出す前は
    /// 1,500 行のケース本体の中にあり、規則だけを確かめる手段がなかった。</para>
    /// </summary>
    [TestClass]
    public class AdaptiveRelaxationTests
    {
        private static (double Omega, int Count) Step(double ratio, double omega, int count)
            => HorizontalCalculationViewModel.UpdateAdaptiveRelaxation(ratio, 1.0, omega, count);

        /// <summary>
        /// 2 割以上減った反復が 1 回だけでは ω を上げない。上げるのは 2 回続いたときで、
        /// 1 回ごとに上げると減少が偶然だった場合に増分が大きすぎて跳ね返る。
        /// </summary>
        [TestMethod]
        public void OneGoodIterationDoesNotRaiseOmega()
        {
            var (omega, count) = Step(0.5, 0.5, 0);
            Assert.AreEqual(0.5, omega, 1e-12, "1 回目は ω を据え置く");
            Assert.AreEqual(1, count, "連続改善回数だけ進む");
        }

        /// <summary>2 回続いたら ω を 1.3 倍し、カウントを戻す。</summary>
        [TestMethod]
        public void TwoGoodIterationsRaiseOmegaBy30Percent()
        {
            var (omega, count) = Step(0.5, 0.5, 1);
            Assert.AreEqual(0.65, omega, 1e-12);
            Assert.AreEqual(0, count, "上げた反復でカウントを戻す");
        }

        /// <summary>ω の上限は 1.0。1.3 倍が 1.0 を越える状態でも越えない。</summary>
        [TestMethod]
        public void OmegaNeverExceedsOne()
        {
            var (omega, _) = Step(0.5, 0.9, 1);
            Assert.AreEqual(1.0, omega, 1e-12);
        }

        /// <summary>ω が既に 1.0 なら、改善が続いても触らない (カウントは進む)。</summary>
        [TestMethod]
        public void OmegaAtOneStaysAndKeepsCounting()
        {
            var (omega, count) = Step(0.5, 1.0, 1);
            Assert.AreEqual(1.0, omega, 1e-12);
            Assert.AreEqual(2, count, "1.0 のときはカウントを戻さない");
        }

        /// <summary>
        /// 残差が増えたら即座に ω を下げる。下げ幅は増え方で 3 段階
        /// (1.5 倍超で 0.25、1.1 倍超で 0.5、それ以下で 0.7)。
        /// </summary>
        [TestMethod]
        public void GrowingResidualCutsOmegaByHowMuchItGrew()
        {
            Assert.AreEqual(0.20, Step(2.0, 0.8, 0).Omega, 1e-12, "1.5 倍超は 0.25 倍");
            Assert.AreEqual(0.40, Step(1.2, 0.8, 0).Omega, 1e-12, "1.1 倍超は 0.5 倍");
            Assert.AreEqual(0.56, Step(1.05, 0.8, 0).Omega, 1e-12, "わずかな増加は 0.7 倍");
        }

        /// <summary>増加を検出した反復では連続改善回数を 0 に戻す。</summary>
        [TestMethod]
        public void GrowingResidualResetsTheCount()
        {
            Assert.AreEqual(0, Step(2.0, 0.8, 5).Count);
        }

        /// <summary>ω の下限は 0.1。何度増加しても 0 には落ちない。</summary>
        [TestMethod]
        public void OmegaNeverFallsBelowOneTenth()
        {
            double omega = 0.8;
            for (int i = 0; i < 20; i++)
                omega = Step(2.0, omega, 0).Omega;
            Assert.AreEqual(0.1, omega, 1e-12);
        }

        /// <summary>
        /// 微減 (0〜2 割) が 3 回続いたら ω を 1.1 倍する。上限は 0.7 で、
        /// 大幅減少のときの 1.0 とは別に置いてある (微減のまま上げ切らない)。
        /// </summary>
        [TestMethod]
        public void SmallDecreasesRaiseOmegaOnlyUpToZeroPointSeven()
        {
            var (omega, count) = Step(0.9, 0.5, 2);
            Assert.AreEqual(0.55, omega, 1e-12);
            Assert.AreEqual(0, count);

            Assert.AreEqual(0.7, Step(0.9, 0.65, 2).Omega, 1e-12, "上限 0.7 で止まる");
            Assert.AreEqual(0.7, Step(0.9, 0.7, 2).Omega, 1e-12, "既に 0.7 なら触らない");
        }

        /// <summary>微減 1〜2 回目は ω を据え置き、カウントだけ進める。</summary>
        [TestMethod]
        public void SmallDecreaseCountsBeforeItRaises()
        {
            Assert.AreEqual(0.5, Step(0.9, 0.5, 0).Omega, 1e-12);
            Assert.AreEqual(1, Step(0.9, 0.5, 0).Count);
        }

        /// <summary>
        /// 停滞 (残差比 1.0〜1.02) では ω を 0.85 倍に下げる。下限は 0.15。
        /// 動かないときに増分を小さくするのは、ほぼ同じ点を往復する状態を崩すため。
        /// </summary>
        [TestMethod]
        public void StagnationShrinksOmegaWithItsOwnFloor()
        {
            var (omega, count) = Step(1.01, 0.4, 3);
            Assert.AreEqual(0.34, omega, 1e-12);
            Assert.AreEqual(0, count, "停滞ではカウントを戻す");

            double small = 0.4;
            for (int i = 0; i < 30; i++)
                small = Step(1.01, small, 0).Omega;
            Assert.AreEqual(0.15, small, 1e-12, "停滞側の下限は 0.15 (増加側の 0.1 とは別)");
        }

        /// <summary>
        /// 残差比がちょうど 1.0 のときは停滞として扱う (0.8・1.02・1.0 の境界の取り方)。
        /// </summary>
        [TestMethod]
        public void RatioOfExactlyOneIsStagnation()
        {
            Assert.AreEqual(0.85, Step(1.0, 1.0, 0).Omega, 1e-12);
        }

        /// <summary>
        /// 前回残差が 0 に近いときは 0 で割らず、残差比 1.0 (停滞) として扱う。
        /// 最初のステップで外力が 0 のケースがここに来る。
        /// </summary>
        [TestMethod]
        public void ZeroPreviousResidualIsTreatedAsStagnationNotDivideByZero()
        {
            var (omega, _) = HorizontalCalculationViewModel.UpdateAdaptiveRelaxation(1.0, 0.0, 0.6, 0);
            Assert.AreEqual(0.51, omega, 1e-12);
            Assert.IsTrue(double.IsFinite(omega), "0 除算で無限大にならない");
        }

        /// <summary>
        /// 境界の取り方そのもの。0.8 は「大幅減少」に入らず微減側、1.02 は「増加」に入らず停滞側。
        /// 不等号を <c>&lt;=</c> に書き換えると落ちる。
        /// </summary>
        [TestMethod]
        public void TheBoundariesBelongToTheMilderBranch()
        {
            // 比 0.8 ちょうど: 大幅減少 (2 回で 1.3 倍) ではなく微減 (3 回で 1.1 倍)
            Assert.AreEqual(0.5, Step(0.8, 0.5, 1).Omega, 1e-12, "0.8 は大幅減少に入らない");
            Assert.AreEqual(0.55, Step(0.8, 0.5, 2).Omega, 1e-12, "0.8 は微減として扱う");

            // 比 1.02 ちょうど: 増加 (0.7 倍) ではなく停滞 (0.85 倍)
            Assert.AreEqual(0.34, Step(1.02, 0.4, 0).Omega, 1e-12, "1.02 は増加に入らない");
        }
    }
}
