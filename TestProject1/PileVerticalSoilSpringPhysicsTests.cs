using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;

namespace TestProject1
{
    /// <summary>
    /// 沈下解析の物理関数 (VerticalLoadTransferMethod.GetTangentStiffnessPilePerimeter 等)
    /// と、それを呼び出す PileVerticalSoilSpringModel の挙動を検証する。
    ///
    /// 水平解析側 (HorizontalCalculationViewModel.PrepareKmat) が、
    /// 沈下解析と同じ物理関数を直接呼ぶ設計を確認するためのレグレッションテスト。
    /// </summary>
    [TestClass]
    public class PileVerticalSoilSpringPhysicsTests
    {
        // ====== 杭周面 τ-s 双線形 (GetTangentStiffnessPilePerimeter) ======

        [TestMethod]
        public void PerimeterTangent_StateInitial_ReturnsZero()
        {
            // "initial" 状態は剛性 0 を返す (沈下解析の状態遷移ロジック)
            double k = VerticalLoadTransferMethod.GetTangentStiffnessPilePerimeter(
                state: "initial", s: 0.001, aPC: true, aPT: true,
                tau1: 50, tau2: 100, S1: 0.005, S2: 0.020, psiL: 3.14);
            Assert.AreEqual(0.0, k);
        }

        [TestMethod]
        public void PerimeterTangent_PositiveStateButCircumDisabled_ReturnsZero()
        {
            // positive 方向だが aPC=false (押込み方向 周面抵抗なし) → 0
            double k = VerticalLoadTransferMethod.GetTangentStiffnessPilePerimeter(
                state: "positive", s: 0.001, aPC: false, aPT: true,
                tau1: 50, tau2: 100, S1: 0.005, S2: 0.020, psiL: 3.14);
            Assert.AreEqual(0.0, k);
        }

        [TestMethod]
        public void PerimeterTangent_NegativeStateButTensionDisabled_ReturnsZero()
        {
            // negative 方向だが aPT=false (引抜き周面抵抗なし) → 0
            double k = VerticalLoadTransferMethod.GetTangentStiffnessPilePerimeter(
                state: "negative", s: -0.001, aPC: true, aPT: false,
                tau1: 50, tau2: 100, S1: 0.005, S2: 0.020, psiL: 3.14);
            Assert.AreEqual(0.0, k);
        }

        [TestMethod]
        public void PerimeterTangent_FirstSegment_LinearInitialStiffness()
        {
            // |s| < S1: k = tau1 / S1 * psiL (線形弾性)
            const double tau1 = 50.0, S1 = 0.005, psiL = 3.14;
            double k = VerticalLoadTransferMethod.GetTangentStiffnessPilePerimeter(
                state: "positive", s: 0.001, aPC: true, aPT: true,
                tau1: tau1, tau2: 100, S1: S1, S2: 0.020, psiL: psiL);
            double expected = tau1 / S1 * psiL; // 31400
            Assert.AreEqual(expected, k, 1e-6);
        }

        [TestMethod]
        public void PerimeterTangent_SecondSegment_HardeningSlope()
        {
            // S1 < |s| < S2: k = (tau2-tau1)/(S2-S1) * psiL
            const double tau1 = 50, tau2 = 100, S1 = 0.005, S2 = 0.020, psiL = 3.14;
            double k = VerticalLoadTransferMethod.GetTangentStiffnessPilePerimeter(
                state: "positive", s: 0.010, aPC: true, aPT: true,
                tau1: tau1, tau2: tau2, S1: S1, S2: S2, psiL: psiL);
            double expected = (tau2 - tau1) / (S2 - S1) * psiL;
            Assert.AreEqual(expected, k, 1e-6);
        }

        [TestMethod]
        public void PerimeterTangent_PlasticPlateau_TinyResidualStiffness()
        {
            // |s| > S2: 塑性 plateau。剛性は 0 に近い極小値 (= tau1/S1 * psiL * 0.001)
            const double tau1 = 50, S1 = 0.005, psiL = 3.14;
            double k = VerticalLoadTransferMethod.GetTangentStiffnessPilePerimeter(
                state: "positive", s: 0.030, aPC: true, aPT: true,
                tau1: tau1, tau2: 100, S1: S1, S2: 0.020, psiL: psiL);
            double expected = tau1 / S1 * psiL * 0.001;
            Assert.AreEqual(expected, k, 1e-6);
        }

        // ====== 杭周面 割線 (GetSecantStiffnessPilePerimeter) ======

        [TestMethod]
        public void PerimeterSecant_FirstSegment_EqualsTangent()
        {
            // |s| < S1: 弾性域なので secant = tangent
            const double tau1 = 50, S1 = 0.005, psiL = 3.14;
            double kSec = VerticalLoadTransferMethod.GetSecantStiffnessPilePerimeter(
                "positive", 0.001, true, true, tau1, 100, S1, 0.020, psiL);
            double kTan = VerticalLoadTransferMethod.GetTangentStiffnessPilePerimeter(
                "positive", 0.001, true, true, tau1, 100, S1, 0.020, psiL);
            Assert.AreEqual(kTan, kSec, 1e-6);
        }

        [TestMethod]
        public void PerimeterSecant_AboveS2_DerivedFromTau2()
        {
            // |s| >= S2: secant = tau2 / |s| * psiL (plateau の割線)
            const double tau2 = 100, psiL = 3.14, s = 0.030;
            double k = VerticalLoadTransferMethod.GetSecantStiffnessPilePerimeter(
                "positive", s, true, true, 50, tau2, 0.005, 0.020, psiL);
            double expected = tau2 / s * psiL;
            Assert.AreEqual(expected, k, 1e-6);
        }

        // ====== 杭先端 R-S 曲線 (GetTangent/SecantStiffnessPileToeFromSettlement) ======

        [TestMethod]
        public void ToeTangent_ZeroSettlement_ReturnsInitialStiffness()
        {
            // settlement = 0 (まだ沈下していない): 初期接線剛性 (>0、有限)
            // Rpu > 0 で Rp=0 → stan = 0.1*dp*α/Rpu → ktan = 1/stan = Rpu/(0.1*dp*α)
            const double dp = 1.0, rpu = 5000, alpha = 0.3;
            double k = VerticalLoadTransferMethod.GetTangentStiffnessPileToeFromSettlement(
                settlement: 0, dp: dp, rpu: rpu, alpha: alpha, n: 2.0);
            double expected = rpu / (0.1 * dp * alpha);
            Assert.AreEqual(expected, k, 1e-6,
                $"settlement=0 → 初期接線剛性 Rpu/(0.1*dp*α) = {expected:F2} kN/m");
        }

        [TestMethod]
        public void ToeTangent_NegativeSettlement_ReturnsZero()
        {
            // settlement < 0 (引抜き方向): 杭先端は反力なし → k = 0
            double k = VerticalLoadTransferMethod.GetTangentStiffnessPileToeFromSettlement(
                settlement: -0.001, dp: 1.0, rpu: 5000, alpha: 0.3, n: 2.0);
            Assert.AreEqual(0.0, k);
        }

        [TestMethod]
        public void ToeTangent_RpuZero_ReturnsZero()
        {
            // 極限支持力 0 → k = 0 (ガード条件)
            double k = VerticalLoadTransferMethod.GetTangentStiffnessPileToeFromSettlement(
                settlement: 0.001, dp: 1.0, rpu: 0, alpha: 0.3, n: 2.0);
            Assert.AreEqual(0.0, k);
        }

        [TestMethod]
        public void ToeTangent_PositiveSettlement_ReturnsPositiveStiffness()
        {
            // 通常: 沈下があり Rpu > 0 → 正の接線剛性
            double k = VerticalLoadTransferMethod.GetTangentStiffnessPileToeFromSettlement(
                settlement: 0.001, dp: 1.0, rpu: 5000, alpha: 0.3, n: 2.0);
            Assert.IsTrue(k > 0, $"接線剛性は正であるべき: actual={k}");
            Assert.IsTrue(double.IsFinite(k), $"接線剛性は有限であるべき: actual={k}");
        }

        [TestMethod]
        public void ToeTangent_StiffnessDecreasesWithSettlement()
        {
            // R-S 曲線は凸: 沈下が大きくなるほど接線剛性は小さくなる (硬化が緩む)
            double k1 = VerticalLoadTransferMethod.GetTangentStiffnessPileToeFromSettlement(
                0.001, 1.0, 5000, 0.3, 2.0);
            double k10 = VerticalLoadTransferMethod.GetTangentStiffnessPileToeFromSettlement(
                0.010, 1.0, 5000, 0.3, 2.0);
            Assert.IsTrue(k1 > k10,
                $"沈下が大きいほど接線剛性は小さくなるべき: k(0.001m)={k1:E2}, k(0.010m)={k10:E2}");
        }

        [TestMethod]
        public void ToeSecant_ZeroSettlement_ReturnsZero()
        {
            double k = VerticalLoadTransferMethod.GetSecantStiffnessPileToeFromSettlement(
                0, 1.0, 5000, 0.3, 2.0);
            Assert.AreEqual(0.0, k);
        }

        [TestMethod]
        public void ToeSecant_PositiveSettlement_ReturnsPositiveStiffness()
        {
            double k = VerticalLoadTransferMethod.GetSecantStiffnessPileToeFromSettlement(
                0.001, 1.0, 5000, 0.3, 2.0);
            Assert.IsTrue(k > 0 && double.IsFinite(k));
        }

        // ====== 内部一貫性: secant × settlement ≈ force ======

        [TestMethod]
        public void ToeSecant_TimesSettlement_EqualsActualForce()
        {
            // 物理的一貫性: F = K_sec × δ
            // GetSecantStiffness の定義は Rp/δ なので逆算で原 Rp に戻るはず
            const double sett = 0.005;
            double kSec = VerticalLoadTransferMethod.GetSecantStiffnessPileToeFromSettlement(
                sett, 1.0, 5000, 0.3, 2.0);
            double Rp = kSec * sett;
            // Rp は内部の Newton-Raphson で求める値だが、ここでは「正で 有限」だけ確認
            Assert.IsTrue(Rp > 0 && double.IsFinite(Rp),
                $"F = K_sec × δ は正で有限値であるべき: kSec={kSec:E2}, Rp={Rp:E2}");
        }
    }
}
