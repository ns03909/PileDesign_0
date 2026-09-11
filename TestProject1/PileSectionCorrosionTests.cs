using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 鋼管を含む断面の曲げ剛性 EI が、腐食後の鋼管寸法で計算されること。
    ///
    /// <para>M-φ・耐力は元から腐食後の寸法 (外径 − 2×腐食代、管厚 − 腐食代) だったが、FEM の
    /// 弾性剛性に使う <c>PileSection.EI</c> だけ公称寸法で、1 本の杭の中で初期剛性と M-φ が
    /// 食い違っていた。杭頭部 (コンクリート充填鋼管部) の合成 EI も、別の簡略式
    /// (Ec = 33500 (Fc/60)^(1/3)、γ・ξ なし、主筋なし) で組んでいた。
    /// 2026-09-12 に利用者の判断で EI を腐食後にし、杭頭部の合成 EI はそれを参照するようにした。</para>
    /// </summary>
    [TestClass]
    public class PileSectionCorrosionTests
    {
        private const double Es = 205000.0;

        private static PileSection Cft(double corrosion) => new PileSection
        {
            PileBodyType = PileTypeNames.SteelPipe,
            PileSectionType = PileTypeNames.CftSection,
            PipeGrade = "SKK400",
            PipeDia = 1000.0,
            PipeTs = 12.0,
            CorrosionDepth = corrosion,
            ConcreteOutDia = 976.0,
            ConcreteGsi = 1.0,
            ConcreteFc = 27.0,
            MainBarNum = 0,
            MainBarSize = "D25",
            MainBarSpec = "SD390",
            MainBarDr = 150.0,
            PileDiameter = 1000.0 - 2 * corrosion,
            CorrodedPipeTs = 12.0 - corrosion,
            PipeEs = Es,
            ConcreteE = 25000.0,
        };

        [TestMethod]
        public void EIUsesTheCorrodedPipe()
        {
            var s = Cft(1.0);
            Assert.AreEqual(s.EICorroded, s.EI, 0.0, "EI が腐食後の値になっていません");

            // 公称との差は鋼管の外側 2×腐食代ぶんだけ (内径 = コンクリート外径は腐食で変わらない)
            double steelDiff = Es * Math.PI * (Math.Pow(1000.0, 4) - Math.Pow(998.0, 4)) / 64.0 * 1e-9;
            Assert.AreEqual(steelDiff, s.EINominal - s.EI, steelDiff * 1e-9, "公称と腐食後の差が鋼管項と合いません");
            Assert.IsTrue(steelDiff > 0);
        }

        [TestMethod]
        public void EAUsesTheCorrodedPipeButWeightDoesNot()
        {
            var s = Cft(1.0);
            Assert.AreEqual(s.EACorroded, s.EA, 0.0, "EA が腐食後の値になっていません");

            // 公称との差は鋼管の断面積の差だけ
            double steelDiff = Es * Math.PI / 4.0 * (Math.Pow(1000.0, 2) - Math.Pow(998.0, 2)) * 0.001;
            Assert.AreEqual(steelDiff, s.EANominal - s.EA, steelDiff * 1e-9, "公称と腐食後の差が鋼管項と合いません");

            // 単位長さ重量は公称のまま (腐食考慮の値は諸元表の比較用)
            Assert.IsTrue(s.W > s.WCorroded, "重量の腐食考慮の値が公称より小さくなっていません");
        }

        [TestMethod]
        public void CorrosionDoesNotTouchSectionsWithoutPipe()
        {
            var rc = new PileSection { PipeDia = 0.0, PipeTs = 0.0, CorrosionDepth = 1.0, PipeEs = Es, ConcreteE = 25000.0 };
            Assert.AreEqual(rc.EINominal, rc.EI, 0.0, "鋼管の無い断面で腐食代が EI を変えています");
            Assert.AreEqual(rc.EANominal, rc.EA, 0.0, "鋼管の無い断面で腐食代が EA を変えています");
            Assert.AreEqual(0.0, rc.PipeAsCorroded, 0.0, "鋼管の無い断面に腐食後の鋼管断面積が出ています");
        }

        [TestMethod]
        public void HeadCompositeEIIsTheSectionEI()
        {
            var s = Cft(1.0);
            var sps = s.TryCreateSteelPipeSection();
            Assert.IsNotNull(sps, "コンクリート充填鋼管部の鋼管断面が組めません");
            Assert.AreEqual(s.EI * 1e9, sps!.CompositeHeadEI!.Value, s.EI * 1e9 * 1e-12,
                "杭頭部の合成 EI が杭断面の EI (腐食後) と違います");
        }

        [TestMethod]
        public void TheSectionWindowUsesTheSameFactory()
        {
            string src = Regex.Replace(TestSource.Read("Graphics_r1", "ViewModels", "PileSectionViewModel.cs"), "//.*", "");
            int at = src.IndexOf("SteelPipeSection? CreateSteelPipeSectionHead(", StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, "CreateSteelPipeSectionHead が見つかりません (名前が変わった?)");
            int end = src.IndexOf("\n        }", at, StringComparison.Ordinal);
            string body = src[at..end];
            StringAssert.Contains(body, "TryCreateSteelPipeSection()", "杭断面ウィンドウの杭頭部 M-φ 図が PileSection の組み立てを通っていません");
            Assert.IsFalse(body.Contains("new SteelPipeSection(", StringComparison.Ordinal),
                "杭断面ウィンドウが杭頭部の鋼管断面を自前で組んでいます (合成 EI が渡らない)");
        }
    }
}
