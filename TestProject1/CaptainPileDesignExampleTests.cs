using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// キャプテンパイル工法 設計・施工マニュアル第 6 版 3.5 設計例 (D2000・絞り ν0.7 = φ1400・Fc30・SD490・定着筋配置径 φ1260)
    /// の終局時曲げモーメント Mu との照合。
    ///
    /// <para>設計例の Q-α 図 (p065 / 3-27) の各曲線は、右側が α·Q 一定の双曲線になる (杭頭モーメントが Mu で頭打ち)。
    /// Chang の式 M0 = α·Q/(2β) (β = (kh·B/4EI)^¼ = 0.1069 /m、kh = 0.005 N/mm³・B = 2000・EI = 24400×7.85×10¹¹) から
    /// Mu = α·Q/(2β)。この読み方は設計例の表 (p069 / 3-31) の P1 圧縮側 α = 0.68・Q = 2069 → M0 = 6582 kN·m で確かめた。
    /// 下の値は、図を画素で読んだ Q = 8649 kN での α から求めた (読み取りの精度 ±0.0014 → ±60 kN·m 程度)。</para>
    ///
    /// <para>本文には Mu の限界ひずみも定着筋の強度の割増しも書いていないが、「圧縮縁ひずみ 0.005・定着筋 1.1σy・σmax = 60
    /// (= min(Fc/ν², 2Fc))」で 38 本とも 1% か読み取りの精度 (60 kN·m) の内で合った (0.003・1.0 倍では最大 30% 小さい)。
    /// 2026-11-24 に利用者の判断で既定をこれに合わせた。一番下の曲線は読み取りが詰まって合わないので外している。</para>
    /// </summary>
    [TestClass]
    public class CaptainPileDesignExampleTests
    {
        private static CaptainPile Example(int barCount, string barDia)
        {
            var cp = new CaptainPile(30.0, 24_400.0);
            var ring = cp.PCRings.First(r => r.Name == "2000-N");
            cp.PCRing = ring;
            cp.D = ring.D;
            cp.SelectedPCRingName = ring.Name!;
            cp.Nu = 0.7;
            var tb = cp.CTPTensionRebars;
            tb.HasTensionRebars = true;
            tb.IsCircleArrangement = true;
            tb.IsSquareArrangement = false;
            tb.SelectedBarNumberCircle = barCount;
            tb.SelectedTensionAnchorDia = barDia;
            tb.SelectedTensionAnchorGrade = "SD490";
            tb.TDorTB = 1260.0;
            cp.SetPileBodyConcrete(30.0, 24_400.0);
            cp.Update();
            return cp;
        }

        private static void AssertMatches(CaptainPile cp, double[] axialKN, double[] chartMu, string label)
        {
            Assert.AreEqual(CaptainPile.DefaultUltimateConcreteStrain, cp.UltimateConcreteStrain, "(前提) 既定の εcu");
            Assert.AreEqual(CaptainPile.DefaultUltimateRebarStrengthFactor, cp.UltimateRebarStrengthFactor, "(前提) 既定の強度倍率");
            Assert.AreEqual(60.0, cp.CTPConcrete.SigmaMax, 1e-9, "(前提) σmax = min(30/0.7², 2×30) = 60 (図の凡例も σmax60)");
            for (int i = 0; i < axialKN.Length; i++)
            {
                double mu = cp.GetMu(axialKN[i] * 1e3) / 1e6;
                Assert.AreEqual(chartMu[i], mu, Math.Max(chartMu[i] * 0.01, 60.0),   // 画素の読み取りの精度 ±60 kN·m 程度
                    $"{label} N = {axialKN[i]} kN: 設計例の図から読んだ Mu {chartMu[i]} kN·m に対し {mu:F0}");
            }
        }

        /// <summary>P1: 16-D41 (SD490)。</summary>
        [TestMethod]
        public void P1_MatchesTheDesignExampleChart()
        {
            double[] n = { 31176, 27472, 23948, 20605, 17443, 14462, 11661, 9041, 6601, 4343, 2265, 368, -1349, -2885, -4240, -5415, -6408, -7222, -7854 };
            double[] mu = { 16444, 15946, 15229, 14453, 13623, 12741, 11746, 10779, 9812, 8845, 7878, 6911, 6053, 5252, 4503, 3816, 3233, 2735, 2351 };
            AssertMatches(Example(16, "D41"), n, mu, "P1");
        }

        /// <summary>P3: 8-D38 (SD490)。</summary>
        [TestMethod]
        public void P3_MatchesTheDesignExampleChart()
        {
            double[] n = { 29184, 26128, 23221, 20463, 17854, 15394, 13084, 10922, 8910, 7046, 5332, 3767, 2351, 1084, -34, -1003, -1823, -2494, -3016 };
            double[] mu = { 13644, 13037, 12369, 11677, 10933, 10127, 9270, 8412, 7526, 6668, 5839, 5066, 4346, 3710, 3071, 2517, 2019, 1606, 1246 };
            AssertMatches(Example(8, "D38"), n, mu, "P3");
        }

        /// <summary>以前の取り方 (0.003・1.0 倍) に戻せば、以前の値になる (図より小さい)。</summary>
        [TestMethod]
        public void TheFormerDefinitionIsStillAvailable()
        {
            var cp = Example(16, "D41");
            double now = cp.GetMu(-4240e3);
            cp.UltimateConcreteStrain = 0.003;
            cp.UltimateRebarStrengthFactor = 1.0;
            double former = cp.GetMu(-4240e3);
            Assert.AreEqual(3827.0, former / 1e6, 15.0, "以前の Mu (N = −4,240 kN)");
            Assert.IsTrue(now > former * 1.1, $"既定 {now / 1e6:F0} は以前の {former / 1e6:F0} より 1 割以上大きい");

            var (my, _) = cp.GetMyCurvatureY(-4240e3);
            cp.UltimateRebarStrengthFactor = 1.1;
            var (my2, _) = cp.GetMyCurvatureY(-4240e3);
            Assert.AreEqual(my, my2, Math.Abs(my) * 1e-12, "強度倍率は My には掛けない");
        }
    }
}
