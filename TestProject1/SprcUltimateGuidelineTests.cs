using PileDesign.Models.InputData;
using System.Collections.Generic;

namespace TestProject1
{
    /// <summary>
    /// 場所打ち鋼管コンクリート杭（鋼管コンクリート部）の安全限界を指針(案)準拠で算定する
    /// オプション（<see cref="ConcreteModelOptions.UseInsituUltimateEFunction"/>）の検証。
    ///  (A) 解析用 M-φ はオプションON/OFFで一致（解析側は常にバイリニアへ隔離＝収束不変）。
    ///  (B) 安全限界 NM 曲線（検定の耐力側）はON時に有限で、OFF（バイリニア）と相違。
    ///  (C) 安全限界せん断 QN（scMu/sMu 係数）が全域で有限。
    /// </summary>
    [TestClass]
    public class SprcUltimateGuidelineTests
    {
        private static PileSection CreateSprcSection()
        {
            return new PileSection
            {
                PileBodyType = "場所打ち鋼管コンクリート杭",
                PileSectionType = "鋼管コンクリート部",
                PipeGrade = "SKK400",
                PipeDia = 1000.0,
                PipeTs = 12.0,
                CorrosionDepth = 1.0,
                ConcreteOutDia = 1000.0,
                ConcreteGsi = 1.0,
                ConcreteFc = 27.0,
                MainBarNum = 20,
                MainBarSize = "D25",
                MainBarSpec = "SD390",
                MainBarDr = 150.0,
                HoopSize = "D13",
                HoopSpacing = 150.0,
                HoopSpec = "SD295",
                HoopCenterCover = 150.0,
                PileDiameter = 1000.0,
            };
        }

        private static void ResetOptions()
        {
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
            ConcreteModelOptions.UseNotification1113Compression = false;
            ConcreteModelOptions.UseNotification1113Shear = false;
            ConcreteModelOptions.RebarYieldAt11F = false;
            ConcreteModelOptions.SteelPipeYieldAt11F = false;
            ConcreteModelOptions.IgnoreTensileStrength = false;
            ConcreteModelOptions.UseReducedCompression = false;
        }

        [TestCleanup]
        public void Cleanup() => ResetOptions();

        private static IEnumerable<double> AxialSweepKN()
        {
            double[] kn = { -3000, -1500, 0, 1500, 3000, 5000, 7000 };
            foreach (double n in kn) yield return n;
        }

        private static double MaxMoment((List<double> Phis, List<double> Moments) c)
        {
            double max = 0;
            foreach (double m in c.Moments) if (m > max) max = m;
            return max;
        }

        /// <summary>(A) 解析 M-φ はオプションON/OFFで全点一致（解析側は常にバイリニア＝収束不変）。</summary>
        [TestMethod]
        public void GuidelineOption_AnalysisMPhi_IdenticalToBilinear()
        {
            foreach (double nkN in AxialSweepKN())
            {
                ResetOptions();
                var off = CreateSprcSection().GetMPhiRelationship(nkN);

                ResetOptions();
                ConcreteModelOptions.UseInsituUltimateEFunction = true;
                var on = CreateSprcSection().GetMPhiRelationship(nkN);

                Assert.AreEqual(off.Moments.Count, on.Moments.Count, $"点数不一致 N={nkN:F0}kN");
                for (int i = 0; i < off.Moments.Count; i++)
                {
                    // 実質同一（求根初期値が容量曲線経由でわずかに変わるため相対1e-6で判定）。
                    // これは FEM が見る M-φ が不変＝収束不変であることの担保。
                    Assert.AreEqual(off.Moments[i], on.Moments[i], System.Math.Max(1e-3, System.Math.Abs(off.Moments[i]) * 1e-6),
                        $"解析M-φ Mが不一致（N={nkN:F0}kN, i={i}）: off={off.Moments[i]:F3}, on={on.Moments[i]:F3}");
                }
            }
        }

        /// <summary>(B) 安全限界 NM 曲線（検定の耐力側）はON時に有限で、バイリニアと相違。</summary>
        [TestMethod]
        public void GuidelineOption_UltimateNM_IsFiniteAndDiffers()
        {
            ResetOptions();
            var nmBl = CreateSprcSection().FactoredUltimateNM;

            ResetOptions();
            ConcreteModelOptions.UseInsituUltimateEFunction = true;
            var nmEf = CreateSprcSection().FactoredUltimateNM;

            Assert.IsTrue(nmEf.N != null && nmEf.M != null && nmEf.M.Count > 0, "指針NM曲線が空");
            for (int i = 0; i < nmEf.M.Count; i++)
                Assert.IsFalse(double.IsNaN(nmEf.M[i]) || double.IsInfinity(nmEf.M[i]) ||
                               double.IsNaN(nmEf.N[i]) || double.IsInfinity(nmEf.N[i]),
                    $"指針NM曲線 index {i} が非有限");

            double mBl = 0, mEf = 0;
            foreach (double m in nmBl.M) if (m > mBl) mBl = m;
            foreach (double m in nmEf.M) if (m > mEf) mEf = m;
            Assert.IsTrue(mBl > 100.0 && mEf > 100.0, $"耐力が小さすぎ bl={mBl:F0}, ef={mEf:F0}");
            Assert.AreNotEqual(mBl, mEf, mBl * 1e-3, $"指針NMがバイリニアと同一（効いていない）: bl={mBl:F1}, ef={mEf:F1}");
        }

        /// <summary>(C) 安全限界せん断 QN（scMu/sMu）が全域で有限。</summary>
        [TestMethod]
        public void GuidelineOption_UltimateShearQN_IsFinite()
        {
            ResetOptions();
            ConcreteModelOptions.UseInsituUltimateEFunction = true;
            var nq = CreateSprcSection().FactoredUltimateNQ;

            Assert.IsTrue(nq.N != null && nq.Q != null && nq.Q.Count > 0, "せん断QNが空");
            for (int i = 0; i < nq.Q.Count; i++)
                Assert.IsFalse(double.IsNaN(nq.Q[i]) || double.IsInfinity(nq.Q[i]),
                    $"せん断QN Q[{i}]={nq.Q[i]} が非有限");
        }
    }
}
