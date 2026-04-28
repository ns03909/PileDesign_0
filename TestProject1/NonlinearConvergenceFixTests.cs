using PileDesign.FEM;
using System;
using System.Collections.Generic;

namespace TestProject1
{
    /// <summary>
    /// v22-v26 非線形収束修正の最小再現テスト群。
    ///
    /// 既に EdgeCaseTests.cs に v22 #3 / v23 A-2 / v25 GetP 対称化 のテストが
    /// 存在するため、ここではそれ以外の (主に v24 合成 M-φ 対角ブレンド) の
    /// 数学的不変条件を検証する。
    ///
    /// 単体テスト不可な修正 (v25 G+ ラインサーチ・v26 適応 nStep など FEM 全体の
    /// 反復挙動に依存するもの) は memory/reference_warning_baseline.md と同様
    /// 別途結合テストで扱う。
    /// </summary>
    [TestClass]
    public class NonlinearConvergenceFixTests
    {
        // --- v24: Beam.EvaluateEIeff の対角 Jacobian ブレンド ---
        //
        // 数式:
        //   φres = √(φy² + φz²),  cosθ = φy/φres,  sinθ = φz/φres
        //   EIy = EI_tan·cos²θ + EI_sec·sin²θ
        //   EIz = EI_tan·sin²θ + EI_sec·cos²θ
        //
        // 不変条件:
        //  V24-1. Pure-Y bending (φz=0): EIy = EI_tan, EIz = EI_sec (主軸が EI_tan)
        //  V24-2. Pure-Z bending (φy=0): EIy = EI_sec, EIz = EI_tan
        //  V24-3. 45° (φy=φz>0):  EIy = EIz = (EI_tan + EI_sec)/2
        //  V24-4. φres ≈ 0: 等方 (EI_sec, EI_sec) フォールバック (方向未定)
        //  V24-5. EIy + EIz = EI_tan + EI_sec (cos²+sin²=1 から導出される sum 不変)

        /// <summary>
        /// 折れ線 M-φ 曲線を持つ Beam を作る簡便ヘルパー。
        /// 弾性域 (0→0.001, 0→K0) と塑性域 (0.001→0.01, K0→K_pl) を持つ二段折れ線。
        /// </summary>
        private static Beam BuildBeamWithCurve(double K0 = 1e8, double K_pl = 1e6)
        {
            var beam = new Beam();
            // φ: 0, 0.001, 0.01
            // M: 0, K0·0.001=1e5, K0·0.001 + K_pl·0.009 = 1e5 + 9e3 = 109e3
            var phis = new List<double> { 0.0, 0.001, 0.01 };
            var moments = new List<double>
            {
                0.0,
                K0 * 0.001,
                K0 * 0.001 + K_pl * 0.009
            };
            beam.SetResolvedCombinedMPhi(phis, moments);
            return beam;
        }

        [TestMethod]
        public void EvaluateEIeff_PureYBending_ReturnsTanForY_SecForZ()
        {
            var beam = BuildBeamWithCurve();
            // 弾性域内 (φ < 0.001) で φz=0 → 主軸は Y 方向
            var (EIy, EIz) = beam.EvaluateEIeff(phiY: 0.0005, phiZ: 0.0);

            // 弾性域では tangent ≈ secant ≈ K0 なので両軸とも同じはず
            Assert.IsTrue(EIy > 0, $"EIy should be positive (got {EIy})");
            Assert.IsTrue(EIz > 0, $"EIz should be positive (got {EIz})");

            // 塑性域内 (φ > 0.001) で φz=0 → 主軸は Y → EIy=EI_tan(=K_pl), EIz=EI_sec(>K_pl)
            var (EIyPlas, EIzPlas) = beam.EvaluateEIeff(phiY: 0.005, phiZ: 0.0);
            Assert.IsTrue(EIzPlas > EIyPlas,
                $"In plastic regime, secant > tangent: EIy(tan)={EIyPlas:E3}, EIz(sec)={EIzPlas:E3}");
        }

        [TestMethod]
        public void EvaluateEIeff_45Degrees_BothAxesEqual()
        {
            var beam = BuildBeamWithCurve();
            // 45° 曲げ (φy = φz) では cos²θ = sin²θ = 0.5 → EIy = EIz
            var (EIy, EIz) = beam.EvaluateEIeff(phiY: 0.005, phiZ: 0.005);

            Assert.AreEqual(EIy, EIz, 1e-6 * Math.Max(1.0, Math.Abs(EIy)),
                $"45° bending should produce EIy == EIz (got {EIy:E3} vs {EIz:E3})");
        }

        [TestMethod]
        public void EvaluateEIeff_Symmetric_PureYAndPureZSwap()
        {
            // φy<->φz 入れ替えで EIy<->EIz が入れ替わる
            var beam = BuildBeamWithCurve();
            var (EIy_Y, EIz_Y) = beam.EvaluateEIeff(phiY: 0.005, phiZ: 0.0);
            var (EIy_Z, EIz_Z) = beam.EvaluateEIeff(phiY: 0.0, phiZ: 0.005);

            // 対称性: pure-Y で得た (EIy, EIz) と pure-Z で得た (EIz, EIy) が一致
            Assert.AreEqual(EIy_Y, EIz_Z, 1e-9 * Math.Max(1.0, Math.Abs(EIy_Y)),
                "EIy(pure-Y) should equal EIz(pure-Z)");
            Assert.AreEqual(EIz_Y, EIy_Z, 1e-9 * Math.Max(1.0, Math.Abs(EIz_Y)),
                "EIz(pure-Y) should equal EIy(pure-Z)");
        }

        [TestMethod]
        public void EvaluateEIeff_SumInvariant_EIyPlusEIzEqualsEItanPlusEIsec()
        {
            // sum 不変条件: 同じ φres であれば cos²θ + sin²θ = 1 から
            // EIy + EIz = EI_tan + EI_sec で角度に依存しない
            var beam = BuildBeamWithCurve();

            // 同一 φres = 0.005 の 3 角度を厳密に生成
            const double phiRes = 0.005;
            var angles = new[] { 0.0, Math.PI / 6.0, Math.PI / 4.0 };  // 0°, 30°, 45°
            var sums = new double[angles.Length];
            for (int i = 0; i < angles.Length; i++)
            {
                var phiY = phiRes * Math.Cos(angles[i]);
                var phiZ = phiRes * Math.Sin(angles[i]);
                var (EIy, EIz) = beam.EvaluateEIeff(phiY, phiZ);
                sums[i] = EIy + EIz;
            }

            for (int i = 1; i < sums.Length; i++)
            {
                Assert.AreEqual(sums[0], sums[i], 1e-9 * Math.Max(1.0, Math.Abs(sums[0])),
                    $"Sum should be invariant across angles: " +
                    $"sum[0°]={sums[0]:E6}, sum[{angles[i] * 180 / Math.PI:F0}°]={sums[i]:E6}");
            }
        }

        [TestMethod]
        public void EvaluateEIeff_NearZeroPhi_FallsBackToIsotropicSecant()
        {
            // φres ≈ 0 (両方ほぼ 0) では方向未定なので isotropic (EIy == EIz) を返す
            var beam = BuildBeamWithCurve();
            var (EIy, EIz) = beam.EvaluateEIeff(phiY: 1e-20, phiZ: 1e-20);

            Assert.AreEqual(EIy, EIz, 1e-12,
                $"Near-zero phi should be isotropic: EIy={EIy:E3}, EIz={EIz:E3}");
            Assert.IsTrue(EIy > 0, "isotropic fallback should be positive");
        }

        // --- v22: HorizontalSoilReaction.GetkhTan の降伏境界連続性 ---
        // (EdgeCaseTests.GetkhTan_ElasticSqrtBoundary_IsContinuous で 0.10/0.20 区間は既存)
        // ここでは「降伏境界 |y/y0|=1 付近」での挙動を検証 (v22 #2 修正対象)。

        [TestMethod]
        public void GetkhTan_AtYieldBoundary_ContinuousFromBelow()
        {
            // 降伏境界の前後で接線が無限大ジャンプしないことを確認
            const double kh0 = 50.0;
            const double py = 10.0;
            // y0 = py / kh0 = 0.2 (kh0·y0 = py の点が降伏境界)
            const double y0 = py / kh0;

            var khJustBelow = PileDesign.Models.InputData.HorizontalSoilReactionItem.GetkhTan(kh0, y0 * 0.999, py);
            var khJustAbove = PileDesign.Models.InputData.HorizontalSoilReactionItem.GetkhTan(kh0, y0 * 1.001, py);
            var khAtBoundary = PileDesign.Models.InputData.HorizontalSoilReactionItem.GetkhTan(kh0, y0, py);

            // すべて有限・正値
            Assert.IsTrue(double.IsFinite(khJustBelow) && khJustBelow > 0,
                $"kh just below yield should be finite positive: {khJustBelow}");
            Assert.IsTrue(double.IsFinite(khJustAbove) && khJustAbove > 0,
                $"kh just above yield should be finite positive: {khJustAbove}");
            Assert.IsTrue(double.IsFinite(khAtBoundary) && khAtBoundary > 0,
                $"kh at yield should be finite positive: {khAtBoundary}");

            // v22 修正の主旨: 接線比が 100 倍以下 (旧実装は ~500 倍)
            var ratio = khJustBelow / khJustAbove;
            Assert.IsTrue(ratio < 100.0 && ratio > 0.01,
                $"Tangent ratio across yield should be < 100×: {ratio:F2} " +
                $"(below={khJustBelow:E3}, above={khJustAbove:E3})");
        }
    }
}
