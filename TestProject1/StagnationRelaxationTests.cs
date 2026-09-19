using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 停滞検出と、それによる収束基準の緩和
    /// (<see cref="HorizontalCalculationViewModel.UpdateStagnationRelaxation"/>)。
    ///
    /// <para>残差比 (今回/前回) がほぼ 1 の反復を数え、15 回続いて残差が緩和基準以下なら基準を
    /// 緩和基準まで、30 回続いたら現在残差 ×1.1 まで緩める。反復回数にそのまま効くので、
    /// 規則を書き換えると収束スナップショットが動く。</para>
    ///
    /// <para><b>緩和は緩める方向にしか動かさない。</b> 2026-09-19 まで代入だったため、先に他の判定
    /// (長期未改善・改善率不足) が大きく緩めた基準を引き戻し得た。引き戻すと、緩めて抜けるつもりだった
    /// ステップが抜けられずに再試行へ回る。他の 4 つの緩和判定は元から「今より緩いときだけ動かす」形で、
    /// ここだけが代入だった。</para>
    /// </summary>
    [TestClass]
    public class StagnationRelaxationTests
    {
        private const double Alpha = 1e-6;         // 本来の収束基準 (ログ表示用)
        private const double RelaxedAlpha = 1e-5;  // 緩和収束基準

        private static (int Count, double EffectiveAlpha, string? Log) Step(
            double currentResidual, double prevResidual, int count, double effectiveAlpha = Alpha)
            => HorizontalCalculationViewModel.UpdateStagnationRelaxation(
                currentResidual, prevResidual, count, effectiveAlpha, Alpha, RelaxedAlpha);

        /// <summary>改善した反復 (残差比 0.98 未満) はカウントを 0 に戻す。基準は動かさない。</summary>
        [TestMethod]
        public void AnImprovingIterationResetsTheCount()
        {
            var (count, effectiveAlpha, log) = Step(0.5, 1.0, 12);
            Assert.AreEqual(0, count);
            Assert.AreEqual(Alpha, effectiveAlpha, 1e-30, "改善しているので緩めない");
            Assert.IsNull(log);
        }

        /// <summary>停滞は数えるが、閾値に届くまでは何もしない。</summary>
        [TestMethod]
        public void StagnationCountsUpQuietlyUntilTheThreshold()
        {
            var (count, effectiveAlpha, log) = Step(1e-3, 1e-3, 5);
            Assert.AreEqual(6, count);
            Assert.AreEqual(Alpha, effectiveAlpha, 1e-30);
            Assert.IsNull(log);
        }

        /// <summary>
        /// 15 回続いて残差が緩和基準以下なら、基準を緩和基準まで緩める。
        /// </summary>
        [TestMethod]
        public void FifteenStagnantIterationsBelowTheRelaxedLevelRelaxToIt()
        {
            var (count, effectiveAlpha, log) = Step(5e-6, 5e-6, 14);
            Assert.AreEqual(15, count);
            Assert.AreEqual(RelaxedAlpha, effectiveAlpha, 1e-30);
            StringAssert.Contains(log, "停滞検出");
        }

        /// <summary>
        /// 残差が緩和基準より大きいときは 15 回では緩めない。30 回まで待つ。
        /// </summary>
        [TestMethod]
        public void AboveTheRelaxedLevelFifteenIsNotEnough()
        {
            var (count, effectiveAlpha, log) = Step(1e-3, 1e-3, 14);
            Assert.AreEqual(15, count);
            Assert.AreEqual(Alpha, effectiveAlpha, 1e-30, "まだ緩めない");
            Assert.IsNull(log);
        }

        /// <summary>30 回続いたら、現在残差 ×1.1 まで緩める (今この反復で抜けられる値)。</summary>
        [TestMethod]
        public void ThirtyStagnantIterationsRelaxToTheCurrentResidual()
        {
            var (count, effectiveAlpha, log) = Step(1e-3, 1e-3, 29);
            Assert.AreEqual(30, count);
            Assert.AreEqual(1.1e-3, effectiveAlpha, 1e-15);
            StringAssert.Contains(log, "長期停滞検出");
        }

        /// <summary>
        /// <b>本題その 1。</b> 既に基準が現在残差 ×1.1 より緩いなら、長期停滞の枝は引き戻さない。
        /// </summary>
        [TestMethod]
        public void LongStagnationNeverTightensAnAlreadyRelaxedCriterion()
        {
            var (count, effectiveAlpha, log) = Step(1e-3, 1e-3, 29, effectiveAlpha: 1e-2);
            Assert.AreEqual(30, count);
            Assert.AreEqual(1e-2, effectiveAlpha, 1e-30, "1.1e-3 に引き戻してはいけない");
            Assert.IsNull(log, "動かないときはログも出さない");
        }

        /// <summary>
        /// <b>本題その 2。</b> 15 回の枝も同じ。基準が既に緩和基準より緩いなら引き戻さない。
        /// </summary>
        [TestMethod]
        public void StagnationNeverTightensPastTheRelaxedLevel()
        {
            var (count, effectiveAlpha, log) = Step(5e-6, 5e-6, 14, effectiveAlpha: 1e-4);
            Assert.AreEqual(15, count);
            Assert.AreEqual(1e-4, effectiveAlpha, 1e-30, "1e-5 に引き戻してはいけない");
            Assert.IsNull(log);
        }

        /// <summary>
        /// 緩和は何度通しても単調。停滞を 60 回流しても基準が下がる瞬間はない。
        /// </summary>
        [TestMethod]
        public void RelaxationIsMonotoneAcrossManyIterations()
        {
            double effectiveAlpha = Alpha;
            int count = 0;
            for (int i = 0; i < 60; i++)
            {
                double before = effectiveAlpha;
                (count, effectiveAlpha, _) = Step(1e-4, 1e-4, count, effectiveAlpha);
                Assert.IsTrue(effectiveAlpha >= before, $"{i} 回目で基準が厳しくなった ({before:E3} → {effectiveAlpha:E3})");
            }
        }

        /// <summary>
        /// 5% を超える増加は「改善」ではないのでカウントを戻さない。据え置いて発散側の判定に任せる。
        /// </summary>
        [TestMethod]
        public void AClearIncreaseNeitherCountsNorResets()
        {
            var (count, effectiveAlpha, log) = Step(2.0, 1.0, 7);
            Assert.AreEqual(7, count, "据え置く (数えも戻しもしない)");
            Assert.AreEqual(Alpha, effectiveAlpha, 1e-30);
            Assert.IsNull(log);
        }

        /// <summary>
        /// 境界の取り方。残差比 0.98 ちょうどは停滞側、それ未満は改善側。1.05 ちょうどは停滞側。
        /// </summary>
        [TestMethod]
        public void TheBoundariesOfTheStagnantBand()
        {
            Assert.AreEqual(4, Step(0.98, 1.0, 3).Count, "0.98 は停滞として数える");
            Assert.AreEqual(0, Step(0.9799, 1.0, 3).Count, "0.98 未満は改善としてリセット");
            Assert.AreEqual(4, Step(1.05, 1.0, 3).Count, "1.05 は停滞として数える");
            Assert.AreEqual(3, Step(1.0501, 1.0, 3).Count, "1.05 超は据え置き");
        }

        /// <summary>前回残差が 0 に近いときは 0 で割らず、残差比 1.0 (停滞) として扱う。</summary>
        [TestMethod]
        public void ZeroPreviousResidualIsTreatedAsStagnation()
        {
            Assert.AreEqual(1, Step(1e-3, 0.0, 0).Count);
        }
    }
}
