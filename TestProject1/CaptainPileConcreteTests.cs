using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// キャプテンパイル工法は杭体 (最上段の断面) のコンクリートを 2 か所で使う (2026-11-24 利用者の判断)。
    /// <list type="bullet">
    /// <item>σmax = Fc/ν² の Fc … マニュアルどおり杭体とパイルキャップの設計基準強度の小さい方。以前はパイルキャップの Fc だけ</item>
    /// <item>杭体部分の回転剛性 Kp = Ep·Ip/Hp の Ep … 杭体のヤング係数。以前は既製コンクリート杭の 40,000 で固定 (本工法は場所打ち杭用)</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class CaptainPileConcreteTests
    {
        private const double CapFc = 24.0;
        private static readonly double CapEc = 33_500.0 * Math.Pow(23.0 / 24.0, 2.0) * Math.Pow(CapFc / 60.0, 1.0 / 3.0);

        private static CaptainPile Ring1200(double nu = 1.0)
        {
            var cp = new CaptainPile(CapFc, CapEc);
            var ring = cp.PCRings.First(r => r.Name == "1200-N");
            cp.PCRing = ring;
            cp.D = ring.D;
            cp.SelectedPCRingName = ring.Name!;
            cp.Nu = nu;
            cp.CTPTensionRebars.HasTensionRebars = false;
            cp.Update();
            return cp;
        }

        [TestMethod]
        public void TheSmallerOfThePileAndCapFcIsUsed()
        {
            var cp = Ring1200();
            double muCapOnly = cp.GetMu(3000e3);
            Assert.AreEqual(CapFc, cp.CTPConcrete.SigmaMax, 1e-12, "(前提) 杭体の値が無ければパイルキャップの Fc");

            cp.SetPileBodyConcrete(30.0, 25_000.0);   // 杭体の方が強い → パイルキャップのまま
            Assert.AreEqual(CapFc, cp.DesignFc, 0.0);
            Assert.AreEqual(muCapOnly, cp.GetMu(3000e3), Math.Abs(muCapOnly) * 1e-9, "杭体の方が強ければ Mu は変わらない");

            cp.SetPileBodyConcrete(21.0, 21_000.0);   // 杭体の方が弱い → 杭体の Fc
            Assert.AreEqual(21.0, cp.DesignFc, 0.0);
            Assert.AreEqual(21.0, cp.CTPConcrete.SigmaMax, 1e-12, "ν = 1 なので σmax = Fc");
            Assert.IsTrue(cp.GetMu(3000e3) < muCapOnly, "弱い杭体の Fc が Mu に届いていない");
        }

        [TestMethod]
        public void KpUsesThePileYoungsModulus()
        {
            var cp = Ring1200(0.85);
            static double Ke(CaptainPile c, double ep) => CaptainPile.GetKe(
                CaptainPile.GetK(ep, c.Ip, c.Hp), CaptainPile.GetK(c.Ec, c.Ic, c.Hc), CaptainPile.GetK(c.Eb, c.Ib, c.Hb));
            Assert.AreEqual(Ke(cp, CaptainPile.FallbackPileEp), cp.Ke, cp.Ke * 1e-12, "(前提) 配られる前は以前の 40,000");

            cp.SetPileBodyConcrete(24.0, 23_000.0);
            Assert.AreEqual(23_000.0, cp.Ep, 0.0);
            Assert.AreEqual(Ke(cp, 23_000.0), cp.Ke, cp.Ke * 1e-12);
            Assert.AreEqual(Math.PI * Math.Pow(0.85 * 1200.0, 4) / 64.0, cp.Ip, 1.0, "Ip は絞り部径で");
        }

        /// <summary>
        /// マニュアル 式 3.4.3・3.4.4: Ic は絞りによらず杭径 D の円、Ib は絞りによらず PC リング内径 rD1 の円。
        /// 以前は Ic も rD1 の円 (Ib = Ic) で、Kc を大きく見ていた。
        /// </summary>
        [TestMethod]
        public void IcIsThePileDiameterAndIbIsTheRingInnerDiameter()
        {
            var cp = Ring1200(0.85);
            Assert.AreEqual(1300.0, cp.PCRing.RD1, 0.0, "(前提) 1200-N の PC リング内径");
            Assert.AreEqual(Math.PI * Math.Pow(1200.0, 4) / 64.0, cp.Ic, 1.0, "Ic は杭径の円 (絞りによらない)");
            Assert.AreEqual(Math.PI * Math.Pow(1300.0, 4) / 64.0, cp.Ib, 1.0, "Ib は PC リング内径の円 (絞りによらない)");
            Assert.AreEqual(600.0, cp.Hb, 0.0, "Hb = D/2");
        }

        /// <summary>解析の入口 (杭体の M-θ) が最上段の断面の Fc・Ec を配る。</summary>
        [TestMethod]
        public void TheAnalysisEntryHandsOverTheTopSection()
        {
            var input = new MainWindowViewModel().CurrentInputModel!;
            var pb = input.PileBodies[0];
            pb.PileTopType = "キャプテンパイル工法";
            pb.PileTop.CaptainPile = Ring1200();
            var section = pb.PileBodySegments.First().PileSection;
            section.ConcreteFc = 21.0;

            pb.GetMThetaRelationship(1000.0);

            Assert.AreEqual(21.0, pb.PileTop.CaptainPile.DesignFc, 0.0, "杭体の Fc が届いていない");
            Assert.AreEqual(section.ConcreteE, pb.PileTop.CaptainPile.Ep, 0.0, "杭体の Ec が届いていない");
        }

        /// <summary>
        /// Mu を「最大曲げモーメント」(全塑性) にしたとき。定着筋なし・中立軸が中心 (u = 0) なら、
        /// 半円に一様に σmax で N = σmax·πr²/2、M = σmax·(2/3)r³ (閉じた式)。その N で Mu が一致する。
        /// </summary>
        [TestMethod]
        public void TheMaximumMomentIsThePlasticLimit_WithoutBars()
        {
            var cp = Ring1200(0.85);
            double atStrain = cp.GetMu(3000e3);
            cp.UseMaximumUltimateMoment = true;
            double r = 0.85 * 1200.0 / 2.0, smax = cp.CTPConcrete.SigmaMax;
            double n = smax * Math.PI * r * r / 2.0;
            Assert.AreEqual(smax * 2.0 / 3.0 * r * r * r, cp.GetMu(n), smax * r * r * r * 1e-9, "半円の全塑性");

            double plastic = cp.GetMu(3000e3);
            Assert.IsTrue(plastic > atStrain, $"全塑性 {plastic / 1e6:F1} は圧縮縁 0.003 の {atStrain / 1e6:F1} kN·m より大きいはず");
            Assert.AreEqual(0.0, cp.GetMu(-1e3), 0.0, "定着筋なしの引張は 0");
        }

        /// <summary>
        /// 定着筋あり (円形 8 本) の全塑性。中立軸が圧縮縁の外 (u = −r) なら全筋が引張降伏で N = −Ag·σy・M = 0、
        /// 引張縁の外 (u = r) なら全断面圧縮で N = σmax·πr² + (σy − σmax)·Ag・M = 0。
        /// 軸力の釣り合いを解いた Mu は、曲率を大きくするほど (εcu を上げるほど) 近づく上限で、0.003 の値より大きい。
        /// </summary>
        [TestMethod]
        public void TheMaximumMomentIsThePlasticLimit_WithBars()
        {
            var cp = Ring1200(1.0);
            cp.CTPTensionRebars.HasTensionRebars = true;
            cp.CTPTensionRebars.IsCircleArrangement = true;
            cp.CTPTensionRebars.IsSquareArrangement = false;
            cp.CTPTensionRebars.SelectedBarNumberCircle = 8;
            cp.CTPTensionRebars.TDorTB = 600.0;
            cp.Update();
            double atStrain = cp.GetMu(3000e3);

            cp.UseMaximumUltimateMoment = true;
            double r = 600.0, smax = cp.CTPConcrete.SigmaMax, fy = cp.CTPTensionRebars.SigmaY, ag = cp.CTPTensionRebars.Ag;
            var allTension = cp.GetPlasticForceAndMoment(-r * 1.000001);
            Assert.AreEqual(-ag * fy, allTension.N, 1e-6);
            Assert.AreEqual(0.0, allTension.M, 1e-3);
            var allCompression = cp.GetPlasticForceAndMoment(r * 1.000001);
            Assert.AreEqual(smax * Math.PI * r * r + (fy - smax) * ag, allCompression.N, 1e-3);

            double plastic = cp.GetMu(3000e3);
            Assert.IsTrue(plastic > atStrain, $"全塑性 {plastic / 1e6:F1} は圧縮縁 0.003 の {atStrain / 1e6:F1} kN·m より大きいはず");
            Assert.IsTrue(cp.GetMu(-1000e3) > 0, "定着筋ありの引張でも曲げを負担する");
            Assert.AreEqual(0.0, cp.GetMu(-ag * fy * 1.01), 0.0, "全筋の降伏を超える引張は 0");
        }

        [TestMethod]
        public void TheUltimateMomentChoiceSurvivesDeepCopy()
        {
            var cp = Ring1200();
            cp.UseMaximumUltimateMoment = true;
            Assert.IsTrue(cp.DeepCopy().UseMaximumUltimateMoment, "Undo の写しで残る");
        }

        [TestMethod]
        public void ThePileConcreteSurvivesDeepCopy()
        {
            var cp = Ring1200();
            cp.SetPileBodyConcrete(21.0, 21_000.0);
            var copy = cp.DeepCopy();
            Assert.AreEqual(21.0, copy.DesignFc, 0.0, "Undo の写しで残る");
        }
    }
}
