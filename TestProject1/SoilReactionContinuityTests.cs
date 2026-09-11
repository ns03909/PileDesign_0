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
