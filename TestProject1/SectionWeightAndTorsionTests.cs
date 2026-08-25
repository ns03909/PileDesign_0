using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 断面の自重 W とねじり剛性 GJ。
    ///
    /// どちらも式そのものが誤ったまま長く残っていた。カタログに突き合わせられる値が無い
    /// (既製杭のカタログで重量を持つのは Hi-SC105 のみ、そこは鉄筋・テンドンが無く
    /// この誤りが現れない) ため、<b>定義から導いた式との一致</b>で固定する。
    /// </summary>
    [TestClass]
    public class SectionWeightAndTorsionTests
    {
        private const double SteelGamma = 78.5;   // kN/m3

        private static PileSection Precast(string sectionType, string name)
        {
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = sectionType,
                PipeGrade = "SKK490",
            };
            s.SelectedPrecastPile.Name = name;
            s.RecalculateSelectedPrecastPile();
            return s;
        }

        // ── 自重 ───────────────────────────────────────────

        /// <summary>
        /// <c>Ac</c> は定義の時点で主筋・テンドンを控除済み。
        /// 自重の式で再度引くと鋼材ぶんを<b>二重に控除</b>することになる。
        /// </summary>
        [TestMethod]
        public void ConcreteArea_IsAlreadyNetOfSteel()
        {
            var s = Precast(PileTypeNames.Prc, PileSection.PRCs[0].Name);

            double grossAnnulus = (s.ConcreteOutDia - s.ConcreteThickness)
                                  * Math.PI * s.ConcreteThickness;

            Assert.AreEqual(grossAnnulus - s.MainBarAg - s.TendonAp, s.Ac, grossAnnulus * 1e-12,
                "Ac の定義が変わっている。自重の式もあわせて見直すこと");
            Assert.IsTrue(s.MainBarAg + s.TendonAp > 0, "鋼材の無い断面では二重控除が現れない");
        }

        /// <summary>自重 = 鋼材 × 78.5 + コンクリート (= Ac) × γ。</summary>
        [TestMethod]
        public void Weight_CountsTheConcreteAreaOnce()
        {
            foreach (var (type, list) in new (string, System.Collections.Generic.List<PileDesign.Models.PileLibrary.PrecastPile>)[]
            {
                (PileTypeNames.Phc, PileSection.PHCs),
                (PileTypeNames.Prc, PileSection.PRCs),
            })
            {
                foreach (var product in list.Take(30))
                {
                    var s = Precast(type, product.Name);
                    if (s.IsNodularPile) continue;   // 節杭はカタログ質量をそのまま使う

                    double expected = ((s.MainBarAg + s.TendonAp + s.PipeAs) * SteelGamma
                                       + s.Ac * s.ConcreteGamma) * 1e-6;

                    Assert.AreEqual(expected, s.W, Math.Max(expected * 1e-9, 1e-12),
                        $"{product.Name}: 自重が定義とずれている");
                }
            }
        }

        /// <summary>
        /// 二重控除していた頃の式より<b>重くなる</b>こと。
        /// 押込みでは軸力の過小評価 (危険側)、引抜きでは抵抗の過小評価 (安全側) だった。
        /// </summary>
        [TestMethod]
        public void Weight_IsHeavierThanTheOldDoubleDeduction()
        {
            var s = Precast(PileTypeNames.Prc, PileSection.PRCs.First(p => !p.Name.Contains("節")).Name);

            double steel = s.MainBarAg + s.TendonAp;
            double old = ((steel + s.PipeAs) * SteelGamma + (s.Ac - steel) * s.ConcreteGamma) * 1e-6;

            Assert.IsTrue(s.W > old, "自重が旧式より重くなっていない");
            Assert.AreEqual(steel * s.ConcreteGamma * 1e-6, s.W - old, s.W * 1e-9,
                "差が鋼材断面積 × コンクリート単位体積重量になっていない");
        }

        // ── ねじり ─────────────────────────────────────────

        /// <summary>
        /// 円形断面のねじり定数は<b>断面二次極モーメント</b> $J = \pi(D^4-d^4)/32$。
        /// 曲げの断面二次モーメント $I = \pi(D^4-d^4)/64$ を使っており 2 倍過小だった。
        /// </summary>
        [TestMethod]
        public void Torsion_UsesThePolarMomentNotTheBendingOne()
        {
            var s = Precast(PileTypeNames.Phc, PileSection.PHCs[0].Name);

            double di = s.ConcreteOutDia - 2 * s.ConcreteThickness;
            double polar = Math.PI * (Math.Pow(s.ConcreteOutDia, 4) - Math.Pow(di, 4)) / 32.0;
            double pipePolar = Math.PI * (Math.Pow(s.PipeDia, 4) - Math.Pow(s.PipeDia - 2 * s.PipeTs, 4)) / 32.0;

            // G = E / (2(1+nu))  — 実装の GetG は private なのでここで書き下す
            static double G(double e, double nu) => e / (2.0 * (1.0 + nu));
            double expected = (G(s.ConcreteE, 0.2) * polar + G(s.PipeEs, 0.3) * pipePolar) * 1e-9;

            Assert.AreEqual(expected, s.GJ, Math.Abs(expected) * 1e-12,
                "ねじり剛性が断面二次極モーメントで計算されていない");
        }

        /// <summary>
        /// 中実断面では $J = 2I$ が厳密に成り立つ。中空でも同じ関係になる。
        /// この 2 倍の取りこぼしが、そのまま杭のねじり剛性の誤りだった。
        /// </summary>
        [TestMethod]
        public void PolarMoment_IsTwiceTheBendingMoment()
        {
            double d = 600.0, di = 400.0;
            double bending = Math.PI * (Math.Pow(d, 4) - Math.Pow(di, 4)) / 64.0;
            double polar = Math.PI * (Math.Pow(d, 4) - Math.Pow(di, 4)) / 32.0;

            Assert.AreEqual(2.0, polar / bending, 1e-12);
        }
    }
}
