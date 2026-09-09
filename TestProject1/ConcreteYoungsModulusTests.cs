using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// コンクリートのヤング係数が 1 つに揃っていること。
    ///
    /// 断面計算の材料 (<c>InsituConcrete</c>) は強度から単位体積重量を表引きして Ec を求め、
    /// FEM の曲げ剛性 (<c>PileSection.ConcreteE</c>) は<b>利用者が入力した</b>単位体積重量で
    /// 求めていた。式は同じで γ だけ違う。
    ///
    /// そのため単位体積重量を変えても N-M 曲線・ひび割れモーメント・M-φ の初期勾配は動かず、
    /// 曲げ剛性だけが動いていた。<c>RecalculateConcreteE</c> のコメントは
    /// 「Ec はひび割れ・降伏の曲線に効く」と書いてあり、実態と逆だった。
    ///
    /// 差の大きさ (Gsi=1.0): Fc≤36 は表引きが既定の 23.0 と一致するので 0。
    /// Fc=40/45 で 4.4%、Fc=60 で 8.9%。利用者が γc を変えれば全域でずれる。
    /// </summary>
    [TestClass]
    public class ConcreteYoungsModulusTests
    {
        private static PileSection MakeInsituRc(double gamma, double fc = 27.0)
        {
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.InsituRc,
                PileSectionType = PileTypeNames.RcSection,
            };
            s.ConcreteOutDia = 1000;
            s.ConcreteGsi = 1.0;
            s.ConcreteFc = fc;
            s.ConcreteGamma = gamma;
            s.MainBarNum = 20;
            s.MainBarSize = "D25";
            s.MainBarSpec = "SD390";
            s.MainBarDr = 850;
            s.PileDiameter = 1000;
            return s;
        }

        private static InsituConcrete ConcreteOf(PileSection s)
        {
            var section = (InsituReinforcedConcreteSection)s.CreateSectionCalculator()!;
            return section.InsituConcrete;
        }

        /// <summary>
        /// 断面が使う Ec が、FEM の曲げ剛性と同じ Ec であること。
        /// </summary>
        [TestMethod]
        public void TheSectionUsesTheSameEcAsTheStiffness()
        {
            foreach (double gamma in new[] { 22.0, 23.0, 24.0, 25.0 })
            {
                var s = MakeInsituRc(gamma);

                Assert.AreEqual(s.ConcreteE, ConcreteOf(s).Ec, s.ConcreteE * 1e-9,
                    $"γ={gamma} で断面の Ec と曲げ剛性の Ec が食い違っている");
            }
        }

        /// <summary>
        /// 単位体積重量を変えると、断面の Ec も動くこと。
        ///
        /// 動かないと、入力を変えても N-M 曲線とひび割れモーメントが変わらない。
        /// </summary>
        [TestMethod]
        public void ChangingTheUnitWeight_MovesTheSectionEc()
        {
            double at23 = ConcreteOf(MakeInsituRc(23.0)).Ec;
            double at24 = ConcreteOf(MakeInsituRc(24.0)).Ec;

            Assert.AreNotEqual(at23, at24,
                "単位体積重量を変えても断面の Ec が動かない");

            // 式は Ec ∝ (γ/24)^2
            Assert.AreEqual(at24 * (23.0 / 24.0) * (23.0 / 24.0), at23, at23 * 1e-9,
                "Ec が (γ/24)² で効いていない");
        }

        /// <summary>
        /// ひび割れモーメントも単位体積重量に追従すること。
        /// Ec は断面の σcr/εcr に入るので、曲線側にも効く。
        /// </summary>
        [TestMethod]
        public void ChangingTheUnitWeight_MovesTheCrackStrain()
        {
            double at23 = ConcreteOf(MakeInsituRc(23.0)).EpsilonCr_bilinear;
            double at24 = ConcreteOf(MakeInsituRc(24.0)).EpsilonCr_bilinear;

            Assert.AreNotEqual(at23, at24,
                "単位体積重量を変えてもひび割れひずみが動かない");
        }

        /// <summary>
        /// 高強度でも食い違わないこと。
        ///
        /// Fc≤36 では表引きの γ が既定の 23.0 と偶然一致するため差が出ない。
        /// 見逃さないよう、表引きが 23.5 / 24.0 になる強度で確かめる。
        /// </summary>
        [TestMethod]
        public void HighStrengthConcrete_IsConsistentToo()
        {
            foreach (double fc in new[] { 40.0, 45.0, 60.0 })
            {
                var s = MakeInsituRc(23.0, fc);

                Assert.AreEqual(s.ConcreteE, ConcreteOf(s).Ec, s.ConcreteE * 1e-9,
                    $"Fc={fc} で断面の Ec と曲げ剛性の Ec が食い違っている");
            }
        }

        /// <summary>
        /// M-φ のキャッシュの鍵に単位体積重量が入っていること。
        ///
        /// Ec が曲線に効くようになったので、鍵に入っていないと
        /// 単位体積重量を変えても古い曲線が返る。
        /// </summary>
        [TestMethod]
        public void TheMphiCacheKey_IncludesTheUnitWeight()
        {
            string at23 = MakeInsituRc(23.0).GetMPhiCacheKey(1000.0);
            string at24 = MakeInsituRc(24.0).GetMPhiCacheKey(1000.0);

            Assert.AreNotEqual(at23, at24,
                "M-φ キャッシュの鍵に単位体積重量が入っていない。変更しても古い曲線が返る");
        }

        /// <summary>
        /// 単位体積重量を渡さない呼び出し (パイルキャップなど) は従来どおり表引きであること。
        /// </summary>
        [TestMethod]
        public void WithoutAGamma_TheTableLookupIsStillUsed()
        {
            var withoutGamma = new InsituConcrete(1000.0, 1.0, 60.0);
            var withTableGamma = new InsituConcrete(1000.0, 1.0, 60.0, gamma: 24.0);

            Assert.AreEqual(withTableGamma.Ec, withoutGamma.Ec, withoutGamma.Ec * 1e-9,
                "γ を渡さないときの表引きが変わっている (Fc=60 の表引きは 24.0)");
        }
    }
}
