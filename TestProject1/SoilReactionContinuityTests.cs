using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// 塑性水平地盤反力 py (基礎指針'19 表6.6.3・表6.6.4) が、式の切り替わりで跳ばないこと。
    ///
    /// <para>粘性土の後方杭 (R/B &lt; 3) の λ が 3.0 になっていた。文献 (表6.6.4) とヘルプ・計算書は
    /// λ = 3.0(R/B)。py は z/B ≤ 2.5 で 2(1 + μz/B)cu、それより深いと λcu で、
    /// μ = 0.6(R/B) − 0.4 を入れると z/B = 2.5 でちょうど 3.0(R/B)·cu になる。
    /// λ = 3.0 だと、たとえば R/B = 2.5 で 7.5cu から 3.0cu へ跳んでいた (2026-09-11)。</para>
    /// </summary>
    [TestClass]
    public class SoilReactionContinuityTests
    {
        private const double B = 1.0;     // m
        private const double Cu = 100.0;  // kN/m²

        private static double ClayPy(bool isFront, double rOnB, double zOverB)
            => HorizontalSoilReactionItem.GetPy("粘性土", isFront, B, -zOverB * B, rOnB, phi: 0.0, cu: Cu, sigmaZPrime: 0.0);

        [DataTestMethod]
        [DataRow(true, 10.0)]
        [DataRow(false, 10.0)]
        [DataRow(false, 3.0)]
        [DataRow(false, 2.5)]
        [DataRow(false, 1.5)]
        public void ClayPyIsContinuousAtTwoAndAHalfDiameters(bool isFront, double rOnB)
        {
            double above = ClayPy(isFront, rOnB, 2.5);
            double below = ClayPy(isFront, rOnB, 2.5 + 1e-9);
            Assert.AreEqual(above, below, 1e-6 * above,
                $"粘性土の py が z/B = 2.5 で跳んでいます (前方={isFront}, R/B={rOnB}: {above} → {below})");
        }

        /// <summary>
        /// 粘性土の py は<b>地表からの深さ</b>で決まること (平行移動不変)。
        ///
        /// <para>2026-09-12 まで <c>GetPy</c> は標高の絶対値 |z| を深さとして使っていた。
        /// 地表が Z = 0 でない地盤 (設計例集3.1 は +2.40 m、八重洲2 は +3.836 m) では
        /// 深さを取り違え、z/B ≤ 2.5 の枝と λcu の枝の境目も式の値もずれていた。
        /// σz′ 側は <c>GetEffectiveStress</c> が標高基準で正しく、py の深さだけが取り残されていた。</para>
        /// </summary>
        [DataTestMethod]
        [DataRow(0.5)]
        [DataRow(1.5)]
        [DataRow(2.5)]
        [DataRow(4.0)]
        public void ClayPyFollowsDepthFromGroundSurfaceNotAltitude(double zOverB)
        {
            const double lift = 3.0; // m。地盤ごと杭ごと持ち上げる

            double atZero = HorizontalSoilReactionItem.GetPy(
                "粘性土", isFront: false, B, -zOverB * B, rOnB: 1.5,
                phi: 0.0, cu: Cu, sigmaZPrime: 0.0, groundTopAltitude: 0.0);
            double lifted = HorizontalSoilReactionItem.GetPy(
                "粘性土", isFront: false, B, lift - zOverB * B, rOnB: 1.5,
                phi: 0.0, cu: Cu, sigmaZPrime: 0.0, groundTopAltitude: lift);

            Assert.AreEqual(atZero, lifted, 1e-9 * atZero,
                $"地盤と杭を {lift} m 持ち上げると粘性土の py が変わります (深さ z/B = {zOverB})。"
                + "標高を深さとして使っていないか確認してください");
        }

        /// <summary>
        /// 地表が Z = 0 の地盤では従来と同じ値であること (既定引数の後方互換)。
        /// </summary>
        [TestMethod]
        public void ClayPyKeepsTheOldValuesWhenTheSurfaceIsAtZero()
        {
            // 深さ 1.5B、cu = 100、R/B = 1.5 → µ = 0.6·1.5 − 0.4 = 0.5 → 2(1 + 0.5·1.5)·100 = 350
            Assert.AreEqual(350.0, ClayPy(false, 1.5, 1.5), 1e-9);
            // 深さ 4B (> 2.5B) → λ = 3.0·1.5 = 4.5 → 450
            Assert.AreEqual(450.0, ClayPy(false, 1.5, 4.0), 1e-9);
        }

        [TestMethod]
        public void ClayPyOfRearPileIsContinuousInSpacing()
        {
            // R/B = 3 を境に後方杭の式が前方杭と同じ式へ切り替わる。深い位置で跳ばないこと
            double below3 = ClayPy(false, 3.0 - 1e-9, 5.0);
            double at3 = ClayPy(false, 3.0, 5.0);
            Assert.AreEqual(at3, below3, 1e-6 * at3, "後方杭の py が R/B = 3 で跳んでいます");
        }
    }
}
