using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 曲げ剛性 EI の中身（換算断面二次モーメント Ie）をカタログ値で固定する。
    ///
    /// EA には <c>Ep·Ap</c> が入っているのに EI には PC鋼棒の換算項が無く、
    /// カタログの Ie より小さい値になっていた（2026-08-20 修正）。
    /// EI は <c>AnalysisModelling</c> で梁要素の曲げ剛性になるため、水平解析の
    /// 変位・断面力に直接効く。落とすと静かに全モデルの結果がずれる。
    ///
    /// <code>
    /// Ie = I + (1/2)(np - 1)·Ap·(Dp/2)^2 + (1/2)(nr - 1)·Ag·(Dr/2)^2
    /// </code>
    /// 鋼材ごとに PCD も換算比も分ける。JP-NPRC は 232 行すべてで PC鋼棒と異形棒鋼の PCD が異なる。
    /// </summary>
    [TestClass]
    public class SectionFlexuralRigidityTests
    {
        /// <summary>EI [kNm²] から換算断面二次モーメント Ie [mm⁴] を逆算する。</summary>
        private static double IeFrom(PileSection s) => s.EI * 1e9 / s.ConcreteE;

        private static double ConcreteI(PileSection s) =>
            Math.PI * (Math.Pow(s.ConcreteOutDia, 4) - Math.Pow(s.ConcreteOutDia - 2 * s.ConcreteThickness, 4)) / 64.0;

        // ── PHC: PC鋼棒の項が入っていること ────────────────────────

        [TestMethod]
        public void Phc_IncludesTheTendonEquivalentTerm()
        {
            var s = MakePhc();

            double expected = ConcreteI(s)
                + 0.5 * (s.TendonEp / s.ConcreteE - 1) * s.TendonAp * Math.Pow(s.TendonDp, 2) / 4.0;

            Assert.AreEqual(expected, IeFrom(s), expected * 1e-9,
                "PHC杭の EI に PC鋼棒の換算項が入っていない");

            // コンクリートだけの値より確実に大きい
            Assert.IsTrue(IeFrom(s) > ConcreteI(s) * 1.01,
                $"換算項が効いていない (Ie={IeFrom(s):N0}, I={ConcreteI(s):N0})");
        }

        // ── PRC: PC鋼棒と異形棒鋼を別々の PCD・換算比で足すこと ──────

        [TestMethod]
        public void Prc_AddsTendonAndRebarWithTheirOwnPcdAndModularRatio()
        {
            var s = MakePrc();

            double tendon = 0.5 * (s.TendonEp / s.ConcreteE - 1) * s.TendonAp * Math.Pow(s.TendonDp, 2) / 4.0;
            double rebar = 0.5 * (s.MainBarEr / s.ConcreteE - 1) * s.MainBarAg
                           * Math.Pow(s.ConcreteOutDia - 2 * s.MainBarCenterCover, 2) / 4.0;

            Assert.AreEqual(ConcreteI(s) + tendon + rebar, IeFrom(s), IeFrom(s) * 1e-9,
                "PRC杭の EI が「コンクリート + PC鋼棒 + 異形棒鋼」になっていない");

            // 鉄筋項は鉄筋配置直径 MainBarDr を使う (PC鋼棒の PCD ではない)
            Assert.AreNotEqual(s.TendonDp, s.ConcreteOutDia - 2 * s.MainBarCenterCover,
                "テスト条件が不適切: PC鋼棒と異形棒鋼の PCD を変えてください");
            Assert.AreEqual(s.MainBarDr, s.ConcreteOutDia - 2 * s.MainBarCenterCover, 1e-9,
                "鉄筋項の配置直径が MainBarDr と一致していない");
        }

        [TestMethod]
        public void Prc_SeparatingThePcdMattersNumerically()
        {
            var s = MakePrc();

            // PC鋼棒の PCD で一括した簡略式 ((Ap+Ag)·rp²) とは有意に違う
            double lumped = ConcreteI(s)
                + 0.5 * (s.TendonEp / s.ConcreteE - 1) * (s.TendonAp + s.MainBarAg)
                  * Math.Pow(s.TendonDp, 2) / 4.0;

            double diff = Math.Abs(IeFrom(s) - lumped) / IeFrom(s);
            Assert.IsTrue(diff > 1e-4,
                $"分離と一括の差が出ていない ({diff:P3})。テスト条件を見直すこと");
        }

        // ── SC杭: PC鋼材が無いので影響を受けないこと ────────────────

        [TestMethod]
        public void Sc_IsUnaffectedBecauseItHasNoTendon()
        {
            var s = MakeSc();

            double expected = (s.ConcreteE * ConcreteI(s)
                + s.PipeEs * Math.PI * (Math.Pow(s.PipeDia, 4) - Math.Pow(s.PipeDia - 2 * s.PipeTs, 4)) / 64.0) * 1e-9;

            Assert.AreEqual(expected, s.EI, expected * 1e-9,
                "SC杭の EI がコンクリート + 鋼管になっていない");
            Assert.AreEqual(0.0, s.TendonAp, 1e-9, "SC杭に PC鋼材が入っている");
        }

        // ── 腐食考慮側も同じ換算項を持つこと ──────────────────────

        [TestMethod]
        public void CorrodedVariant_KeepsTheSameEquivalentTerms()
        {
            var s = MakeSc();
            s.CorrosionDepth = 1.0;

            // 腐食で変わるのは鋼管項だけ。コンクリート + 換算項は共通
            double concreteAndEquivalent = s.ConcreteE * ConcreteI(s) * 1e-9;
            double pipeNominal = s.EI - concreteAndEquivalent;
            double pipeCorroded = s.EICorroded - concreteAndEquivalent;

            Assert.IsTrue(pipeCorroded < pipeNominal,
                "腐食考慮の鋼管項が公称より小さくなっていない");
            Assert.IsTrue(pipeCorroded > 0, "腐食考慮の鋼管項が消えている");
        }

        // ── カタログ実データとの照合 ──────────────────────────────

        /// <summary>
        /// 製品ライブラリから実際に選択し、カタログの Ie と突き合わせる。
        /// JP-NPH の節杭は軸部基準の中空断面なので、断面性能はストレート PHC と同じ扱いになる。
        /// </summary>
        [TestMethod]
        public void NodularPhc_MatchesTheCatalogueEquivalentInertia()
        {
            var products = PileSection.NodularPiles;
            Assert.IsTrue(products.Count > 0, "節杭ライブラリが読めていない");

            int checkedCount = 0;
            double worst = 0;
            string worstName = "";

            foreach (var p in products.Where(p => p.Ie > 0).Take(60))
            {
                var s = new PileSection
                {
                    PileBodyType = PileTypeNames.PrecastConcrete,
                    PileSectionType = PileTypeNames.PhcNodular,
                };
                s.SelectedPrecastPile.Name = p.DisplayName;
                s.RecalculateSelectedPrecastPile();

                if (s.ConcreteE <= 0 || s.TendonAp <= 0) continue;

                double d = Math.Abs(IeFrom(s) - p.Ie) / p.Ie;
                if (d > worst) { worst = d; worstName = p.Name; }
                checkedCount++;
            }

            Assert.IsTrue(checkedCount > 0, "照合できた製品が無い");
            Assert.IsTrue(worst < 0.01,
                $"EI から逆算した Ie がカタログ値と {worst:P2} ずれている (最悪: {worstName})");
        }

        // ── ヤング係数の出所オプション ────────────────────────────

        /// <summary>
        /// 「基礎部材の強度と変形性能」を選ぶと、既製杭は E ではなく
        /// ヤング係数比 n = 5 が固定される。Ec が 40,000 以外でも n が 5 に保たれること。
        /// </summary>
        [TestMethod]
        public void GuideYoungsModulus_FixesTheModularRatioForPrecastPiles()
        {
            var product = PileSection.NodularPiles.First(p => p.Ie > 0);

            try
            {
                ConcreteModelOptions.UseGuideYoungsModulus = true;

                var s = new PileSection
                {
                    PileBodyType = PileTypeNames.PrecastConcrete,
                    PileSectionType = PileTypeNames.PhcNodular,
                };
                s.SelectedPrecastPile.Name = product.DisplayName;
                s.RecalculateSelectedPrecastPile();

                Assert.AreEqual(ConcreteModelOptions.GuideModularRatioForPrecast,
                    s.TendonEp / s.ConcreteE, 1e-9,
                    "既製杭の PC鋼材のヤング係数比が n = 5 に固定されていない");
            }
            finally
            {
                ConcreteModelOptions.UseGuideYoungsModulus = false;
            }
        }

        [TestMethod]
        public void CatalogueYoungsModulus_IsTheDefault()
        {
            var product = PileSection.NodularPiles.First(p => p.Ep > 0);

            var s = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = PileTypeNames.PhcNodular,
            };
            s.SelectedPrecastPile.Name = product.DisplayName;
            s.RecalculateSelectedPrecastPile();

            Assert.IsFalse(ConcreteModelOptions.UseGuideYoungsModulus, "既定はカタログ値であること");
            Assert.AreEqual(product.Ep, s.TendonEp, 1e-9, "カタログの Ep がそのまま入っていない");
        }

        // ── ヘルパー ──────────────────────────────────────────────

        // PHC杭・SC杭には異形棒鋼が無い。PileSection の既定値には主筋が入っているので、
        // 製品ライブラリ適用時と同じく明示的に 0 にしないと鉄筋項が紛れ込む。
        private static PileSection MakePhc() => new()
        {
            PileBodyType = PileTypeNames.PrecastConcrete,
            PileSectionType = PileTypeNames.Phc,
            ConcreteOutDia = 1000,
            ConcreteThickness = 120,
            ConcreteE = 40000,
            TendonAp = 1200,
            TendonDp = 800,
            TendonEp = 200000,
            MainBarNum = 0,
            MainBarAg = 0,
        };

        private static PileSection MakePrc()
        {
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = PileTypeNames.Prc,
                ConcreteOutDia = 1000,
                ConcreteThickness = 120,
                ConcreteE = 40000,
                TendonAp = 1200,
                TendonDp = 800,
                TendonEp = 200000,
                MainBarAg = 1500,
                MainBarEr = 205000,
            };
            // 異形棒鋼は PC鋼棒と別の配置円に置く (JP-NPRC は全行で異なる)
            s.MainBarDr = 760;
            s.MainBarCenterCover = (s.ConcreteOutDia - s.MainBarDr) * 0.5;
            return s;
        }

        private static PileSection MakeSc() => new()
        {
            PileBodyType = PileTypeNames.PrecastConcrete,
            PileSectionType = PileTypeNames.Sc,
            PipeDia = 800,
            PipeTs = 12,
            PipeEs = 205000,
            ConcreteOutDia = 776,
            ConcreteThickness = 100,
            ConcreteE = 40000,
            MainBarNum = 0,
            MainBarAg = 0,
            TendonAp = 0,
        };
    }
}
