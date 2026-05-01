using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// PileSection.GetMPhiRelationship の入力軸力 N が **kN 単位** であることを保証する
    /// 単位回帰テスト群。場所打ち鉄筋コンクリート杭 D=1500mm / Fc=27 / 30-D29 を題材とする
    /// (計算例10 と同じ断面)。
    ///
    /// 背景: HorizontalCalculationViewModel が pile.AxialForce (kN) を /1000.0 で「N→kN」と
    /// 誤変換していた古いバグがあり、M-φ が約 24% 過小評価されていた。修正後は
    /// pile.AxialForce をそのまま kN として渡すようにした。本テストはその回帰を検出する。
    /// </summary>
    [TestClass]
    public class PileSectionMPhiUnitTests
    {
        private static PileSection CreateExample10Section()
        {
            return new PileSection
            {
                PileBodyType = "場所打ち鉄筋コンクリート杭",
                PileSectionType = "鉄筋コンクリート部",
                ConcreteOutDia = 1500.0,    // D [mm]
                ConcreteFc = 27.0,          // Fc [N/mm2]
                ConcreteGsi = 1.0,          // ξ
                MainBarNum = 30,            // 本数
                MainBarSize = "D29",
                MainBarSpec = "SD390",
                MainBarDr = 200.0,          // 重心かぶり [mm]
                HoopSize = "D13",
                HoopSpacing = 150.0,
                HoopSpec = "SD295",
                HoopCenterCover = 150.0,
                PileDiameter = 1500.0,
            };
        }

        private static double MaxMoment((System.Collections.Generic.List<double> Phis, System.Collections.Generic.List<double> Moments) curve)
        {
            double max = 0;
            for (int i = 0; i < curve.Moments.Count; i++)
                if (curve.Moments[i] > max) max = curve.Moments[i];
            return max;
        }

        [TestMethod]
        public void MPhi_AtRealisticAxialForce_ProducesExpectedMagnitude()
        {
            // 計算例10 杭頭の地震時軸力 N = 4591 kN を kN として渡せば、M_u は 5000 kN·m 程度
            // (旧バグでは 1/1000 にされ M_u ≈ 4000 kN·m になっていた)
            var section = CreateExample10Section();
            var curve = section.GetMPhiRelationship(4591.0);
            double mu = MaxMoment(curve);
            Assert.IsTrue(mu > 5000.0,
                $"M_u at N=4591 kN (correct unit) should be > 5000 kN·m. Got {mu:F1}. " +
                "If this fails, the kN→/1000 unit bug may have regressed.");
        }

        [TestMethod]
        public void MPhi_IsMonotonicallyIncreasingWithCompression()
        {
            // 圧縮軸力が増えれば M_u も増える単調性。バグ (常に N≈0) では崩れる。
            var section = CreateExample10Section();
            double m0 = MaxMoment(section.GetMPhiRelationship(0.0));
            double m4591 = MaxMoment(section.GetMPhiRelationship(4591.0));
            Assert.IsTrue(m4591 > m0 * 1.10,
                $"M_u should increase with compression. m0={m0:F1}, m4591={m4591:F1} (ratio {m4591/Math.Max(m0,1e-9):F2})");
        }
    }
}
