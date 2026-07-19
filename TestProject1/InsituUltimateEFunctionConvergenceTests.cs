using PileDesign.Models.InputData;
using System.Collections.Generic;

namespace TestProject1
{
    /// <summary>
    /// 場所打ち鉄筋コンクリート杭の安全限界曲げを e関数法（指針(案)5.4.1 準拠オプション
    /// <see cref="ConcreteModelOptions.UseInsituUltimateEFunction"/>）で算定する機能の検証。
    ///
    /// 設計上の分離を保証する:
    ///  (A) 解析用 M-φ 端点 Mu0 は、オプションON時でも常にバイリニアで算定する
    ///      （e関数の軟化 ε>εM で β1·Mu0 &lt; MY となり M-φ が非単調＝負勾配ばねになると FEM が
    ///       収束不能になるため）。よって M-φ はオプションON/OFFで不変・単調でなければならない。
    ///  (B) 一方、安全限界 NM 曲線（検定の耐力側）はオプションON時に e関数で算定し、
    ///      有限（NaN/∞なし）で、かつバイリニアと異なる（＝実際に e関数が効いている）こと。
    /// </summary>
    [TestClass]
    public class InsituUltimateEFunctionConvergenceTests
    {
        private static PileSection CreateExample10Section()
        {
            return new PileSection
            {
                PileBodyType = "場所打ち鉄筋コンクリート杭",
                PileSectionType = "鉄筋コンクリート部",
                ConcreteOutDia = 1500.0,
                ConcreteFc = 27.0,
                ConcreteGsi = 1.0,
                MainBarNum = 30,
                MainBarSize = "D29",
                MainBarSpec = "SD390",
                MainBarDr = 200.0,
                HoopSize = "D13",
                HoopSpacing = 150.0,
                HoopSpec = "SD295",
                HoopCenterCover = 150.0,
                PileDiameter = 1500.0,
            };
        }

        // σ0/(ξFc) = -0.05 .. 0.40 に対応する軸力 N[kN] の掃引点
        private static IEnumerable<double> AxialSweepKN()
        {
            const double Fc = 27.0, xi = 1.0, D = 1500.0;
            double Ag = System.Math.PI * D * D / 4.0;
            double xiFc = xi * Fc;
            double[] ratios = { -0.05, -0.02, 0.0, 0.05, 0.10, 1.0 / 6.0, 0.25, 1.0 / 3.0, 0.37, 0.40 };
            foreach (double r in ratios)
                yield return r * xiFc * Ag / 1000.0;
        }

        private static void ResetOptions()
        {
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
            ConcreteModelOptions.UseNotification1113Compression = false;
            ConcreteModelOptions.UseNotification1113Shear = false;
            ConcreteModelOptions.IgnoreTensileStrength = false;
            ConcreteModelOptions.UseReducedCompression = false;
        }

        [TestCleanup]
        public void Cleanup() => ResetOptions();

        private static double MaxMoment((List<double> Phis, List<double> Moments) curve)
        {
            double max = 0;
            foreach (double m in curve.Moments) if (m > max) max = m;
            return max;
        }

        /// <summary>
        /// (A) 解析側の隔離保証: e関数オプションON時の解析 M-φ は、OFF時（＝現行本番のバイリニア）と
        /// 全点で完全一致する。これにより e関数を導入しても FEM が見る M-φ は不変で、収束挙動は
        /// 現行と同一（新たな収束リスクなし）であることを保証する。
        /// （注: 本番バイリニア M-φ 自体、特定軸力で β1·Mu0 がわずかに MY を下回る緩い降下を持つが、
        ///  これは既存挙動であり e関数導入で悪化させないことをこの一致テストが担保する。）
        /// </summary>
        [TestMethod]
        public void EFunctionOption_AnalysisMPhi_IdenticalToBilinear()
        {
            foreach (double nkN in AxialSweepKN())
            {
                ResetOptions();
                var off = CreateExample10Section().GetMPhiRelationship(nkN);

                ResetOptions();
                ConcreteModelOptions.UseInsituUltimateEFunction = true;
                var on = CreateExample10Section().GetMPhiRelationship(nkN);

                Assert.AreEqual(off.Moments.Count, on.Moments.Count, $"点数不一致 N={nkN:F0}kN");
                for (int i = 0; i < off.Moments.Count; i++)
                {
                    Assert.AreEqual(off.Moments[i], on.Moments[i], System.Math.Max(1e-6, System.Math.Abs(off.Moments[i]) * 1e-9),
                        $"解析M-φ Mが不一致（N={nkN:F0}kN, i={i}）: off={off.Moments[i]:F3}, on={on.Moments[i]:F3}");
                    Assert.AreEqual(off.Phis[i], on.Phis[i], System.Math.Max(1e-15, System.Math.Abs(off.Phis[i]) * 1e-9),
                        $"解析M-φ φが不一致（N={nkN:F0}kN, i={i}）");
                }
            }
        }

        /// <summary>(B) e関数ON時、安全限界 NM 曲線（検定の耐力側）は有限で、バイリニアと異なる。</summary>
        [TestMethod]
        public void EFunctionOption_UltimateNMCapacity_IsFiniteAndDiffersFromBilinear()
        {
            ResetOptions();
            var nmBl = CreateExample10Section().FactoredUltimateNM;

            ResetOptions();
            ConcreteModelOptions.UseInsituUltimateEFunction = true;
            var nmEf = CreateExample10Section().FactoredUltimateNM;

            Assert.IsTrue(nmEf.N != null && nmEf.M != null && nmEf.M.Count > 0, "e関数NM曲線が空");
            for (int i = 0; i < nmEf.M.Count; i++)
            {
                Assert.IsFalse(double.IsNaN(nmEf.M[i]) || double.IsInfinity(nmEf.M[i]),
                    $"e関数NM曲線 M[{i}] が非有限");
                Assert.IsFalse(double.IsNaN(nmEf.N[i]) || double.IsInfinity(nmEf.N[i]),
                    $"e関数NM曲線 N[{i}] が非有限");
            }

            // e関数はバイリニアと異なる耐力を与える（＝実際に効いている）。最大曲げで比較。
            double mMaxBl = 0, mMaxEf = 0;
            foreach (double m in nmBl.M) if (m > mMaxBl) mMaxBl = m;
            foreach (double m in nmEf.M) if (m > mMaxEf) mMaxEf = m;
            Assert.IsTrue(mMaxBl > 100.0 && mMaxEf > 100.0, $"耐力が小さすぎ bl={mMaxBl:F0}, ef={mMaxEf:F0}");
            Assert.AreNotEqual(mMaxBl, mMaxEf, mMaxBl * 1e-3,
                $"e関数NM曲線がバイリニアと同一（e関数が効いていない）: bl={mMaxBl:F1}, ef={mMaxEf:F1}");
        }
    }
}
