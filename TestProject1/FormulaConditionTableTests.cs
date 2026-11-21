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

        /// <summary>
        /// 周面摩擦力度 τ の表 (ヘルプ) が実装 (SoilPile.UpdatePileCircumVerticalProperties) の係数・上限と一致し、
        /// 鉛直支持力の計算例が表の式どおりである。
        /// </summary>
        [TestMethod]
        public void TheBearingTablesMatchTheImplementation()
        {
            string help = Help();
            string friction = Regex.Match(help, "<table class=\"table-doc\" id=\"shaft-friction-table\".*?</table>", RegexOptions.Singleline).Value;
            Assert.IsFalse(string.IsNullOrEmpty(friction), "周面摩擦力度の表がありません");
            string code = TestSource.Read("Graphics_r1", "Models", "InputData", "SoilPile.cs");

            // (工法の行の見出し, 砂質土の係数, 上限, 粘性土の係数 (1 は Cu そのもの), 上限)
            var rows = new (string Method, string Sand, string SandMax, string Clay, string ClayMax)[]
            {
                ("場所打ちコンクリート杭", "3.3", "165", "", "100"),
                ("埋込み杭 (プレボーリング杭)", "2.5", "125", "", "125"),
                ("埋込み杭 (中掘り杭)", "1.5", "75", "0.4", "50"),
                ("回転貫入杭", "2.0", "100", "0.5", "62.5"),
                ("打込み杭", "2.0", "100", "0.8", "100"),
            };
            foreach (var r in rows)
            {
                string expected = $"<tr><td>{r.Method}</td><td>${r.Sand}N$ (上限 {r.SandMax})</td><td>${r.Clay}C_u$ (上限 {r.ClayMax})</td></tr>";
                StringAssert.Contains(friction, expected, $"{r.Method} の行が違います");
                StringAssert.Contains(code, $"\"砂質土\" => Math.Min({r.Sand} * nValue, {r.SandMax})", $"{r.Method}: 実装の砂質土の式が表と違います");
                string clayCode = r.Clay == "" ? $"Math.Min(cohesive, {r.ClayMax})" : $"Math.Min({r.Clay} * cohesive, {r.ClayMax})";
                StringAssert.Contains(code, $"\"粘性土\" => {clayCode}", $"{r.Method}: 実装の粘性土の式が表と違います");
            }
            TestSource.AssertScanned(Regex.Matches(friction, "<tr><td>").Count, 6, "周面摩擦力度の表の行");

            // 計算例: 場所打ち杭 D = 1.2 m、先端 N̄ = 50、砂質土 N = 20 × 10 m、粘性土 Cu = 60 × 8 m
            string table = Tables()["cond-vertical-bearing"];
            double a = Math.PI * 1.2 * 1.2 / 4, psi = Math.PI * 1.2;
            double rp = Math.Min(120 * 50, 7500) * a, rfs = Math.Min(3.3 * 20, 165) * 10 * psi, rfc = Math.Min(60.0, 100) * 8 * psi;
            StringAssert.Contains(table, $"$R_p = 6{{,}}000 \\times {a:0.000} = {N0(rp)}$ kN");
            StringAssert.Contains(table, $"で ${N0(rfs)}$ kN、粘性土");
            StringAssert.Contains(table, $"で ${N0(rfc)}$ kN。$R_u = {N0(rp + rfs + rfc)}$ kN (使用限界 ${N0((rp + rfs + rfc) / 3)}$ kN)");
        }

        /// <summary>
        /// 地盤変位 Dmax の表: a1(b1) が式 (4.5.2) (C2・R_Z0 を含む)、a2(b2) が式 (4.5.1) であることを実装の分岐で確かめ、
        /// 計算例を同じ式で計算する (2026-11-21 に利用者が指針の式番号と算定法の対応を確認)。
        /// </summary>
        [TestMethod]
        public void TheGroundDisplacementTableMatchesTheImplementation()
        {
            string code = TestSource.Read("Graphics_r1", "ViewModels", "GroundLayerViewModel.cs");
            var a1 = Regex.Match(code, @"if \(calculationMethod == ""a1\(b1\)""\)\s*\{\s*Dmax = ([^;]+);");
            var a2 = Regex.Match(code, @"else if \(calculationMethod == ""a2\(b2\)""[^)]*\)\s*\{[^}]*?Dmax = ([^;]+);");
            Assert.IsTrue(a1.Success && a2.Success, "(前提) 地盤変位の算定法の分岐を読めない");
            StringAssert.Contains(a1.Groups[1].Value, "C2", "a1(b1) (地表で設定) は C2・R_Z0 を含む式 (4.5.2) のはず");
            StringAssert.Contains(a1.Groups[1].Value, "Rz0");
            Assert.IsFalse(a2.Groups[1].Value.Contains("C2"), "a2(b2) (工学的基盤で設定) は式 (4.5.1) のはず");
            StringAssert.Contains(code, "double Z = RegionalFactor;", "地域係数 Z を基本設定の入力から取っていない (表の「採用値」と合わない)");
            StringAssert.Contains(code, "?? InputModel?.FundamentalInput?.RegionalFactor ??", "地域係数 Z の出どころが基本設定でない");

            // 計算例: 砂質土、ΣH = 20、T0 = 0.5、レベル2、R_Z0 = 0.3
            double alpha = Math.Min(1 + 1.0 * 1.0 * 40 * 0.5 / 20, 4.0), fA = Math.Min(1.6 * alpha * 0.5, 1);
            double dA2 = 0.0015 * (alpha * alpha - 1) * fA * 20;
            double factor = 0.66 * (1 - 1 / (alpha * alpha)) + 2.0 * 0.3 / alpha;
            string table = Tables()["cond-ground-displacement"];
            StringAssert.Contains(table, $"= {alpha:0.0}$、$f_A = \\min(1.6 \\times 2.0 \\times 0.5, 1) = {fA:0.0}$");
            StringAssert.Contains(table, $"$D_{{max}} = 0.0015 \\times 3 \\times 1.0 \\times 20 = {dA2:0.000}$ m");
            StringAssert.Contains(table, $"= {factor:0.000}$ を掛けて ${dA2 * factor:0.000}$ m");
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
