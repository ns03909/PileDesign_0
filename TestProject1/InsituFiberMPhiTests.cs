using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;

namespace TestProject1
{
    /// <summary>
    /// 場所打ち鉄筋コンクリート杭のファイバーモデル M-φ
    /// (<see cref="InsituReinforcedConcreteSection.GetMPhiRelationshipFiber"/>) の検証。
    /// 計算例10 と同じ断面 (D=1500 / Fc=27 / 30-D29 SD390 / PCD=1100) を題材とする。
    ///
    /// ファイバー曲線は既存の閉形式ソルバと同一の構成則・軸力つり合いを解くため、
    /// 以下のアンカー点で整合するはず:
    ///  - 終点: (φu, Mu0)  … GetUltimateMomentForSpecificN と同一状態 (εc=0.003)
    ///  - 降伏点: φy でのファイバー M ≈ My … GetSteelYieldPoint と同一状態
    ///  - ひび割れ点: φcr でのファイバー M ≈ Mcr（引張ひずみ閾値の算定差があるため緩め）
    /// </summary>
    [TestClass]
    public class InsituFiberMPhiTests
    {
        private static InsituReinforcedConcreteSection CreateSection()
        {
            var concrete = new InsituConcrete(1500.0, 1.0, 27.0);
            // PCD = D - 2×かぶり = 1500 - 2×200 = 1100
            var bars = new MainBars(1100.0, 30, "SD390", "D29");
            return new InsituReinforcedConcreteSection(concrete, bars);
        }

        private static void ResetOptions()
        {
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
            ConcreteModelOptions.UseNotification1113Compression = false;
            ConcreteModelOptions.UseNotification1113Shear = false;
            ConcreteModelOptions.IgnoreTensileStrength = false;
            ConcreteModelOptions.UseReducedCompression = false;
        }

        [TestInitialize]
        public void Init() => ResetOptions();

        [TestCleanup]
        public void Cleanup() => ResetOptions();

        // σ0/(ξFc) 比 → 軸力 N [N]
        private static double AxialN(double ratio)
        {
            const double Fc = 27.0, xi = 1.0, D = 1500.0;
            return ratio * xi * Fc * Math.PI * D * D / 4.0;
        }

        // 昇順 φ の折線を線形補間で評価
        private static double Interpolate(List<double> phis, List<double> ms, double phi)
        {
            if (phi <= phis[0]) return ms[0];
            for (int i = 0; i < phis.Count - 1; i++)
            {
                if (phi >= phis[i] && phi <= phis[i + 1])
                {
                    double denom = phis[i + 1] - phis[i];
                    if (Math.Abs(denom) < 1e-30) return 0.5 * (ms[i] + ms[i + 1]);
                    double r = (phi - phis[i]) / denom;
                    return ms[i] + r * (ms[i + 1] - ms[i]);
                }
            }
            return ms[^1];
        }

        /// <summary>基本形状: (0,0) 始点・φ 昇順・全点有限・十分な点数。</summary>
        [TestMethod]
        public void FiberMPhi_BasicShape()
        {
            var section = CreateSection();
            double[] ratios = { -0.02, 0.0, 0.05, 0.10, 1.0 / 6.0, 0.25, 1.0 / 3.0, 0.37 };

            foreach (double r in ratios)
            {
                double n = AxialN(r);
                var fiber = section.GetMPhiRelationshipFiber(n);
                Assert.IsNotNull(fiber, $"ファイバー M-φ が null (σ0/ξFc={r:F3})");

                var (phis, ms) = fiber.Value;
                Assert.AreEqual(0.0, phis[0], 1e-30, "始点 φ が 0 でない");
                Assert.AreEqual(0.0, ms[0], 1e-30, "始点 M が 0 でない");
                Assert.IsTrue(phis.Count >= 20, $"点数不足 ({phis.Count}) σ0/ξFc={r:F3}");

                for (int i = 1; i < phis.Count; i++)
                {
                    Assert.IsTrue(phis[i] > phis[i - 1], $"φ が昇順でない (i={i}, σ0/ξFc={r:F3})");
                    Assert.IsTrue(double.IsFinite(ms[i]), $"M が非有限 (i={i}, σ0/ξFc={r:F3})");
                    Assert.IsTrue(ms[i] > 0.0, $"M が非正 (i={i}, σ0/ξFc={r:F3})");
                }
            }
        }

        /// <summary>終点アンカー: 掃引終点は安全限界状態 (φu, Mu0) と一致する。</summary>
        [TestMethod]
        public void FiberMPhi_EndPointMatchesUltimate()
        {
            var section = CreateSection();
            double[] ratios = { 0.0, 0.10, 0.25, 1.0 / 3.0 };

            foreach (double r in ratios)
            {
                double n = AxialN(r);
                var fiber = section.GetMPhiRelationshipFiber(n);
                Assert.IsNotNull(fiber, $"ファイバー M-φ が null (σ0/ξFc={r:F3})");
                var (phis, ms) = fiber.Value;

                (double mu0, double phiU) = section.GetUltimateMomentForSpecificN(n);
                Assert.IsTrue(mu0 > 0 && phiU > 0, $"終局点が不正 (σ0/ξFc={r:F3})");

                Assert.IsTrue(phis[^1] >= 0.999 * phiU,
                    $"終点 φ が φu に達していない: φ_last={phis[^1]:E4}, φu={phiU:E4} (σ0/ξFc={r:F3})");
                Assert.AreEqual(mu0, ms[^1], 0.02 * mu0,
                    $"終点 M が Mu0 と不一致: M_last={ms[^1]:E4}, Mu0={mu0:E4} (σ0/ξFc={r:F3})");
            }
        }

        /// <summary>降伏点アンカー: φy でのファイバー M は My と整合する（同一構成則・同一つり合い）。</summary>
        [TestMethod]
        public void FiberMPhi_MatchesSteelYieldPoint()
        {
            var section = CreateSection();
            double[] ratios = { 0.0, 0.05, 0.10, 1.0 / 6.0 };

            foreach (double r in ratios)
            {
                double n = AxialN(r);
                var fiber = section.GetMPhiRelationshipFiber(n);
                Assert.IsNotNull(fiber, $"ファイバー M-φ が null (σ0/ξFc={r:F3})");
                var (phis, ms) = fiber.Value;

                (bool hasYield, double my, double phiY) = section.GetSteelYieldPoint(n);
                if (!hasYield || my <= 0 || phiY <= 0) continue; // 高軸力等で降伏点なし → スキップ

                if (phiY > phis[^1]) continue; // 掃引範囲外（想定外だが安全側スキップ）

                // 降伏折れ点をまたぐ線形補間の弦誤差を見込んで 5%
                double mFiber = Interpolate(phis, ms, phiY);
                Assert.AreEqual(my, mFiber, 0.05 * my,
                    $"φy でのファイバー M が My と不一致: M_fiber={mFiber:E4}, My={my:E4} (σ0/ξFc={r:F3})");
            }
        }

        /// <summary>
        /// ひび割れ点アンカー: φcr でのファイバー M は Mcr と概ね整合する。
        /// GetCrackMoment の引張ひずみ閾値は e関数逆算 (GetEFuncEpsilon(Ft))、ファイバーの
        /// バイリニア引張脱落は EpsilonCr_bilinear で、閾値の算定に差があるため許容は 10% と緩め。
        /// </summary>
        [TestMethod]
        public void FiberMPhi_NearCrackPoint()
        {
            var section = CreateSection();
            double[] ratios = { 0.0, 0.10, 0.25 };

            foreach (double r in ratios)
            {
                double n = AxialN(r);
                var fiber = section.GetMPhiRelationshipFiber(n);
                Assert.IsNotNull(fiber, $"ファイバー M-φ が null (σ0/ξFc={r:F3})");
                var (phis, ms) = fiber.Value;

                (double mcr, double phiCr) = section.GetCrackMoment(n, false);
                if (mcr <= 0 || phiCr <= 0) continue;

                double mFiber = Interpolate(phis, ms, phiCr);
                Assert.AreEqual(mcr, mFiber, 0.10 * mcr,
                    $"φcr でのファイバー M が Mcr と不一致: M_fiber={mFiber:E4}, Mcr={mcr:E4} (σ0/ξFc={r:F3})");
            }
        }

        /// <summary>
        /// e関数オプション隔離: ファイバー M-φ は _forceBilinearUltimate ガードにより
        /// e関数オプション ON/OFF で不変（解析用と同じく常にバイリニア、軟化を持ち込まない）。
        /// </summary>
        [TestMethod]
        public void FiberMPhi_UnaffectedByEFunctionOption()
        {
            double n = AxialN(0.10);

            ResetOptions();
            var off = CreateSection().GetMPhiRelationshipFiber(n);

            ResetOptions();
            ConcreteModelOptions.UseInsituUltimateEFunction = true;
            var on = CreateSection().GetMPhiRelationshipFiber(n);

            Assert.IsNotNull(off, "OFF 時のファイバー M-φ が null");
            Assert.IsNotNull(on, "ON 時のファイバー M-φ が null");

            Assert.AreEqual(off.Value.Phis.Count, on.Value.Phis.Count, "点数不一致");
            for (int i = 0; i < off.Value.Phis.Count; i++)
            {
                Assert.AreEqual(off.Value.Phis[i], on.Value.Phis[i],
                    Math.Max(1e-15, Math.Abs(off.Value.Phis[i]) * 1e-6), $"φ 不一致 (i={i})");
                Assert.AreEqual(off.Value.Moments[i], on.Value.Moments[i],
                    Math.Max(1.0, Math.Abs(off.Value.Moments[i]) * 1e-6), $"M 不一致 (i={i})");
            }
        }

        /// <summary>
        /// ポリリニアとの相対関係: ファイバー最大 M は「素の」断面応答なので、
        /// β1(・β2) 低減後のポリリニア最大 M 以上、かつ極端に乖離しない範囲に収まる。
        /// </summary>
        [TestMethod]
        public void FiberMPhi_MaxMomentVsPolyline()
        {
            var section = CreateSection();
            double[] ratios = { 0.0, 0.10, 0.25, 1.0 / 3.0, 0.37 };

            foreach (double r in ratios)
            {
                double n = AxialN(r);
                var fiber = section.GetMPhiRelationshipFiber(n);
                Assert.IsNotNull(fiber, $"ファイバー M-φ が null (σ0/ξFc={r:F3})");

                var (phisPoly, msPoly) = section.GetMPhiRelationship(n);
                double maxPoly = 0, maxFiber = 0;
                foreach (double m in msPoly) if (m > maxPoly) maxPoly = m;
                foreach (double m in fiber.Value.Moments) if (m > maxFiber) maxFiber = m;

                Assert.IsTrue(maxPoly > 0, $"ポリリニア最大 M が非正 (σ0/ξFc={r:F3})");
                double ratio = maxFiber / maxPoly;
                // ポリリニア終点は β1·Mu0 (=0.95·Mu0) または β1·β2·Mu0 (=0.52·Mu0)。
                // ファイバー最大は ≈Mu0 なので比は約 1.05〜1.92 のはず。余裕を見て [1.0, 2.5]。
                Assert.IsTrue(ratio >= 1.0 && ratio <= 2.5,
                    $"ファイバー/ポリリニア最大 M 比が想定外: {ratio:F3} (σ0/ξFc={r:F3})");
            }
        }
    }
}
