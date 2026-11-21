using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの式の直下に置く「適用条件の小表」(外部レビュー #31 2026-11-21)。
    ///
    /// <para>式のほかに、適用する範囲・入力・採用値・適用外が本文に散らばっていて、式と一緒に読みにくかった。
    /// 行を 適用対象・入力・採用値・適用外・参照箇所 (・計算例) の順にそろえた表を式の直下に置く。
    /// 計算例の数値は、実装の式で計算した値と突き合わせる (式を直しても表が古いまま残らないように)。</para>
    /// </summary>
    [TestClass]
    public class FormulaConditionTableTests
    {
        private static readonly string[] Rows = ["適用対象", "入力", "採用値", "適用外", "参照箇所"];

        private static string Help() => TestSource.Read("Graphics_r1", "Help", "help.html");

        private static Dictionary<string, string> Tables()
        {
            var tables = Regex.Matches(Help(), "<table class=\"formula-conditions\" id=\"([^\"]+)\">(.*?)</table>", RegexOptions.Singleline)
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            Assert.IsTrue(tables.Count >= 3, $"(前提) 適用条件の表を読めていない: {tables.Count}");
            return tables;
        }

        [TestMethod]
        public void EveryTableHasTheRowsInTheSameOrder()
        {
            Assert.AreEqual(Regex.Matches(Help(), "class=\"formula-conditions\"").Count, Tables().Count,
                "id の無い (または重複した) 適用条件の表があります");
            foreach (var (id, body) in Tables())
            {
                var heads = Regex.Matches(body, "<th>([^<]+)</th>").Select(m => m.Groups[1].Value).ToList();
                CollectionAssert.AreEqual(Rows, heads.Take(Rows.Length).ToList(), $"{id}: 行の並びが 適用対象・入力・採用値・適用外・参照箇所 ではありません");
                Assert.IsTrue(heads.Count == Rows.Length || (heads.Count == Rows.Length + 1 && heads[^1] == "計算例"),
                    $"{id}: 決まった行のあとに置けるのは「計算例」だけです: {string.Join(", ", heads)}");
            }
        }

        [TestMethod]
        public void TheLinksResolve()
        {
            string help = Regex.Replace(Help(), "<!--.*?-->", "", RegexOptions.Singleline);
            var ids = new HashSet<string>(Regex.Matches(help, "\\bid=\"([^\"]+)\"").Select(m => m.Groups[1].Value));
            var broken = Tables().Values
                .SelectMany(b => Regex.Matches(b, "href=\"#([^\"]+)\"").Select(m => m.Groups[1].Value))
                .Where(t => !ids.Contains(t)).Distinct().ToList();
            Assert.AreEqual(0, broken.Count, "適用条件の表のリンク先が見つかりません: " + string.Join(", ", broken));
        }

        private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>鋼管杭の柱座屈・局部座屈の計算例が、実装の式の値と一致する。</summary>
        [TestMethod]
        public void TheSteelPipeExamplesMatchTheImplementation()
        {
            var tables = Tables();
            string buckling = tables["cond-steel-pipe-buckling"];
            string local = tables["cond-steel-pipe-local-buckling"];

            static SteelPipeSection Pipe(double t, double lk) => new(600, t, 235, 1.0, 0.0, 0.0, 205000.0, lk);

            // 局部座屈 sfc1
            foreach (double t in new[] { 24.0, 12.0, 6.0 })
                StringAssert.Contains(local, F1(Pipe(t, 0).Sfc1), $"t = {t} の sfc1 が表と違います");
            Assert.AreEqual(235.0 / 1.5, Pipe(24, 0).Sfc1, 1e-9, "D/t = 25 で低減なしになっていない");

            // 柱座屈 sfc2: 0 m・5 m・10 m
            StringAssert.Contains(buckling, $"${{}}_sf_{{c1}} = {F1(Pipe(12, 0).Sfc1)}$");
            StringAssert.Contains(buckling, $"F/1.5 = {F1(Pipe(12, 0).Sfc2)}");
            StringAssert.Contains(buckling, $"{{}}_sf_{{c2}} = {F1(Pipe(12, 5).Sfc2)}$ → まだ");
            StringAssert.Contains(buckling, $"{{}}_sf_{{c2}} = {F1(Pipe(12, 10).Sfc2)}$ → <strong>柱座屈系");
            Assert.IsTrue(Pipe(12, 5).Sfc2 > Pipe(12, 5).Sfc1 && Pipe(12, 10).Sfc2 < Pipe(12, 10).Sfc1, "(前提) 5 m と 10 m で支配が入れ替わる");

            // 「約 9 m が境」: 8.5 m では局部座屈系、9.5 m では柱座屈系
            Assert.IsTrue(Pipe(12, 8.5).Sfc2 > Pipe(12, 8.5).Sfc1 && Pipe(12, 9.5).Sfc2 < Pipe(12, 9.5).Sfc1, "境が約 9 m ではありません");
        }

        private static string N0(double v) => v.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", "{,}");

        /// <summary>基準水平地盤反力係数 kh0 の計算例が、地盤ばねの組立と同じ式・同じ掛け方 (ξ・βLq) と一致する。</summary>
        [TestMethod]
        public void TheKh0ExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-kh0"];
            var item = new HorizontalSoilReactionItem();
            item.SetParameters("砂", "砂質土", 18, b: 0.6, e0: 700 * 10, zTop: -1, zBtm: -2,
                xi: 1.0, rOnB: 0, nValue: 10, phi: 30, cu: 0, sigmaZPrimeTop: 18, sigmaZPrimeBtm: 36);

            double kh0 = item.GetKh0For(GroupPileEffect.None);
            double xi08 = item.GetKh0For(new GroupPileEffect { Xi = 0.8, ROnB = 0, BetaL = 1.0 });
            double liq = item.GetKh0For(new GroupPileEffect { Xi = 0.8, ROnB = 0, BetaL = 1.0 }.WithLiquefaction(0.5));
            StringAssert.Contains(table, $"= {N0(kh0)}$ kN/m³", "ξ = 1 の kh0 が表と違います");
            StringAssert.Contains(table, $"$\\xi = 0.8$ で ${N0(xi08)}$", "ξ = 0.8 の kh0 が表と違います");
            StringAssert.Contains(table, $"$\\beta_{{Lq}} = 0.5$ なら ${N0(liq)}$ kN/m³", "液状化ケースの kh0 が表と違います");

            // 手入力の kh0 にも ξ・βLq が掛かる (表の「採用値」の行)
            item.Kh0 = 10000; item.IsKh0Manual = true;
            Assert.AreEqual(10000 * 0.8 * 0.5, item.GetKh0For(new GroupPileEffect { Xi = 0.8, ROnB = 0, BetaL = 0.5 }), 1e-9);
        }

        /// <summary>
        /// PHC 杭のせん断耐力の計算例が、検定と同じ関数の値と一致する。
        /// あわせて、ヘルプの肉厚が t = r_o − r_i であること (2026-10-01 に実装の「肉厚の半分」を直したが、
        /// ヘルプの式は (r_o − r_i)/2 のまま 2026-11-21 まで残っていた)。
        /// </summary>
        [TestMethod]
        public void ThePrecastShearExampleMatchesTheImplementation()
        {
            string help = Help();
            Assert.IsFalse(help.Contains(@"t = \frac{r_o - r_i}{2}"), "ヘルプの PHC 杭のせん断の肉厚が半分のままです (t = r_o − r_i)");
            StringAssert.Contains(help, @"t = r_o - r_i, \quad");

            string table = Tables()["cond-precast-shear"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.Phc]();
            Assert.AreEqual((600.0, 90.0, 85.0, 8.0), (s.PileDiameter, s.ConcreteThickness, s.ConcreteFc, s.SelectedPrecastPile.SigmaE), "(前提) 代表断面が変わった");
            if (s.CreateSectionCalculator() is not PrecastPileSection calc) { Assert.Fail("(前提) PHC の断面計算オブジェクトを作れない"); return; }

            var (diagonal, web) = calc.GetDamageLimitShearComponents(3.0, 0.0);
            double level2 = s.DescribeShearLimit(SectionLimitState.Damage, 2, true, 3.0, 0.0)!.ValueN;
            StringAssert.Contains(table, $"斜めひび割れ ${F1(diagonal / 1000)}$ kN・縦ひび割れ ${F1(web / 1000)}$ kN で ${F1(Math.Min(diagonal, web) / 1000)}$ kN (低減前)");
            StringAssert.Contains(table, $"$\\times 0.65$ で ${F1(level2 / 1000)}$ kN");

            // α: M/(Q·d) ≤ 1 で 2 倍、≥ 3 で 1 倍
            Assert.AreEqual(2.0, calc.GetDamageLimitShearComponents(1.0, 0.0).DiagonalTension / diagonal, 1e-9);
            Assert.AreEqual(1.0, calc.GetDamageLimitShearComponents(5.0, 0.0).DiagonalTension / diagonal, 1e-9);
        }

        /// <summary>塑性水平地盤反力度 py の計算例が、実装の式 (表 6.6.3・6.6.4) と一致する。</summary>
        [TestMethod]
        public void ThePyExamplesMatchTheImplementation()
        {
            string table = Tables()["cond-py"];
            // 砂質土 φ = 30°、σz' = 50
            double sandFront = HorizontalSoilReactionItem.GetPy("砂質土", true, 0.6, -5, 2.5, 30, 0, 50);
            double sandRear = HorizontalSoilReactionItem.GetPy("砂質土", false, 0.6, -5, 2.5, 30, 0, 50);
            StringAssert.Contains(table, $"前方杭 $p_y = 3 \\times 3 \\times 50 = {F1(sandFront)}$");
            StringAssert.Contains(table, $"$p_y = {F1(sandRear)}$ kN/m²");
            // 粘性土 cu = 50、B = 1 (深さは面 0 から測る)
            double F(bool front, double rOnB, double z) => HorizontalSoilReactionItem.GetPy("粘性土", front, 1.0, -z, rOnB, 0, 50, 0);
            StringAssert.Contains(table, $"$z = 1$ m で ${F1(F(true, 0, 1))}$、$z = 3$ m で ${F1(F(true, 0, 3))}$");
            StringAssert.Contains(table, $"は ${F1(F(false, 2, 1))}$ と ${F1(F(false, 2, 3))}$ kN/m²");
            // R/B 未入力の後方杭は前方杭と同じ
            Assert.AreEqual(sandFront, HorizontalSoilReactionItem.GetPy("砂質土", false, 0.6, -5, 0, 30, 0, 50), 1e-9);
        }
    }
}
