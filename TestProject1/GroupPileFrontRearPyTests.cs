using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;

namespace TestProject1
{
    /// <summary>
    /// 前方杭・後方杭で塑性地盤反力 py がどれだけ違うかを固定する。
    ///
    /// 砂質土の py は <c>κ · Kp · σz'</c> で、群杭効果の κ が前後で分かれる。
    ///
    /// <list type="bullet">
    /// <item>前方杭: κ = 3.0 (固定)</item>
    /// <item>後方杭: κ = min((0.55 − 0.007·φ)·(r/B − 1) + 0.4, 3.0)</item>
    /// </list>
    ///
    /// <b>この差は杭間隔で大きく変わる。</b>φ = 30° のとき:
    ///
    /// <list type="bullet">
    /// <item>r/B = 10 … 後方 3.46 → 頭打ちで 3.0。<b>前後で差が無い</b></item>
    /// <item>r/B = 4 … 後方 1.42。前方の 2.1 倍の差</item>
    /// <item>r/B = 3 … 後方 1.08。2.8 倍</item>
    /// <item>r/B = 2.5 … 後方 0.91。<b>3.3 倍</b></item>
    /// </list>
    ///
    /// 同梱の計算例は r/B = 10 なので<b>前後の差が出ない</b>。2026-09-10 に前後の
    /// 割り当てを反転して解析したが、応答は全桁一致した。つまり例題を回すだけでは
    /// この式は検査されない。実務の杭間隔 (2.5〜4B) では 2〜3 倍違うので、
    /// 式そのものを直接固定しておく。
    ///
    /// φ が大きいと後方の κ の伸びが鈍る ((0.55 − 0.007φ) が小さくなる) のも見ておく。
    /// 符号を取り違えると、密な配置で後方杭の抵抗が過大になる。
    /// </summary>
    [TestClass]
    public class GroupPileFrontRearPyTests
    {
        private const string Sand = "砂質土";

        /// <summary>砂質土の py。z・cu は砂質土では効かないので固定値で渡す。</summary>
        private static double Py(bool isFront, double rOnB, double phi)
            => HorizontalSoilReactionItem.GetPy(Sand, isFront, b: 1.0, z: -5.0,
                rOnB: rOnB, phi: phi, cu: 0.0, sigmaZPrime: 100.0);

        /// <summary>前方杭の κ は 3.0 固定。杭間隔にも φ にも依らない。</summary>
        [TestMethod]
        public void TheFrontPile_UsesAFixedKappaOfThree()
        {
            double kp30 = (1 + Math.Sin(30 * Math.PI / 180)) / (1 - Math.Sin(30 * Math.PI / 180));
            double expected = 3.0 * kp30 * 100.0;

            foreach (double rOnB in new[] { 2.5, 3.0, 4.0, 10.0 })
                Assert.AreEqual(expected, Py(isFront: true, rOnB, 30.0), expected * 1e-12,
                    $"前方杭の py が杭間隔 (r/B = {rOnB}) で変わっています。κ は 3.0 固定のはず");
        }

        /// <summary>
        /// 後方杭の κ が式どおりで、3.0 で頭打ちになること。
        /// 頭打ちを忘れると、広い間隔で後方杭の抵抗が前方杭を超える。
        /// </summary>
        [DataTestMethod]
        [DataRow(2.5, 30.0, 0.91)]
        [DataRow(3.0, 30.0, 1.08)]
        [DataRow(4.0, 30.0, 1.42)]
        [DataRow(10.0, 30.0, 3.00)]   // 3.46 → 頭打ち
        [DataRow(3.0, 40.0, 0.94)]    // φ が大きいと伸びが鈍る
        public void TheRearPile_FollowsTheFormulaAndIsCappedAtThree(
            double rOnB, double phi, double expectedKappa)
        {
            double kp = (1 + Math.Sin(phi * Math.PI / 180)) / (1 - Math.Sin(phi * Math.PI / 180));
            double actualKappa = Py(isFront: false, rOnB, phi) / (kp * 100.0);

            Assert.AreEqual(expectedKappa, actualKappa, 0.005,
                $"後方杭の κ が式と違います (r/B = {rOnB}, φ = {phi})。"
                + "密な配置では前方杭との差が 3 倍になるので、ここがずれると"
                + "群杭の水平抵抗を取り違えます");

            Assert.IsTrue(actualKappa <= 3.0 + 1e-9,
                $"後方杭の κ が 3.0 を超えています ({actualKappa:F3})。頭打ちが効いていません");
        }

        /// <summary>
        /// 前方杭の抵抗が後方杭を下回らないこと。
        /// 実務の杭間隔で前後が入れ替わっていたら、群杭効果の向きが逆になっている。
        /// </summary>
        [TestMethod]
        public void TheFrontPile_IsNeverWeakerThanTheRear()
        {
            foreach (double rOnB in new[] { 1.5, 2.5, 3.0, 4.0, 6.0, 10.0, 20.0 })
                foreach (double phi in new[] { 20.0, 30.0, 40.0 })
                {
                    double front = Py(isFront: true, rOnB, phi);
                    double rear = Py(isFront: false, rOnB, phi);
                    Assert.IsTrue(front >= rear - front * 1e-12,
                        $"r/B = {rOnB}, φ = {phi} で後方杭の py ({rear:F1}) が"
                        + $"前方杭 ({front:F1}) を超えています。群杭効果の向きが逆です");
                }
        }
    }
}
