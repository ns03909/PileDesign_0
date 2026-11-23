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

        /// <summary>
        /// 引抜き抵抗力の表: τT の係数 (2/3・4/5)、R_TU・R_TY・R_TR の掛け方、自重 W は係数なしで全区間ぶん足すこと
        /// (「引抜側周面抵抗」のチェックによらない) を実装 (SoilPile.CalculateResistances) で確かめ、計算例を同じ式で計算する。
        /// </summary>
        [TestMethod]
        public void TheUpliftTableMatchesTheImplementation()
        {
            string code = TestSource.Read("Graphics_r1", "Models", "InputData", "SoilPile.cs");
            StringAssert.Contains(code, "\"砂質土\" => (0.8 * pileCircumVertical.Tau2, 5.0, 20.0, -2.0 / 3.0 * pileCircumVertical.Tau2)", "砂質土の τst が 2/3 τs でない");
            StringAssert.Contains(code, "\"礫質土\" => (0.7 * pileCircumVertical.Tau2, 10.0, 30.0, -2.0 / 3.0 * pileCircumVertical.Tau2)", "礫質土の τst が 2/3 τs でない");
            StringAssert.Contains(code, "\"粘性土\" => (0.8 * pileCircumVertical.Tau2, 3.0, 10.0, -4.0 / 5.0 * pileCircumVertical.Tau2)", "粘性土の τct が 4/5 τc でない");

            // 杭頭から 10 m 砂質土 (τst = 44)・8 m 粘性土 (τct = 48)・2 m はチェックなし。杭径 1.2 m
            var section = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.Phc]();
            section.PileDiameter = 1200;
            PileCircumVertical Layer(double top, double bottom, double tauT, bool uplift)
            {
                var seg = new PileBodySegment();
                seg.PileSection = section;
                return new PileCircumVertical { Top = top, Bottom = bottom, TauT = -tauT, IsNegativeCircumResistance = uplift, PileBodySegment = seg };
            }
            var pile = new SoilPile
            {
                PileCircumVerticals = [Layer(0, -10, 44, true), Layer(-10, -18, 48, true), Layer(-18, -20, 60, false)],
            };
            pile.CalculateResistances();
            double psi = Math.PI * 1.2, sand = 44 * 10 * psi, clay = 48 * 8 * psi, friction = sand + clay;
            double w = section.W * 20; // チェックの無い区間も含めて全長
            Assert.AreEqual(-(friction + w), pile.Rtu, 1e-6, "R_TU = 周面 + W でない");
            Assert.AreEqual(-(2.0 / 3.0 * friction + w), pile.Rty, 1e-6, "R_TY = 2/3 周面 + W でない");
            Assert.AreEqual(-(friction / 1.2 + w), pile.Rtr, 1e-6, "R_TR = 周面/1.2 + W でない");
            Assert.AreEqual(pile.Rtu / 3, pile.Rt_SLS, 1e-9);
            Assert.AreEqual((pile.Rty, pile.Rtr), (pile.Rt_DLS, pile.Rt_ULS));

            // 地下水位 −5 m: その下の 15 m は浮力 (γw = 10 × 材料の体積) を差し引く (基礎指針'19 式 6.5.1 の W の定義。
            // 2026-11-22 まで空中の重量のまま足していた)
            Assert.IsTrue(section.DisplacedVolumePerM > 0, "(前提) 材料の体積が 0");
            pile.GroundInput = new GroundInput { GroundWaterTableAltitude = -5 };
            pile.CalculateResistances();
            double wBuoyant = section.W * 20 - 10.0 * section.DisplacedVolumePerM * 15;
            Assert.AreEqual(-(friction + wBuoyant), pile.Rtu, 1e-6, "地下水位より下の浮力を差し引いていない");
            Assert.AreEqual(-(friction / 1.2 + wBuoyant), pile.Rtr, 1e-6);
            // 荷重伝達法の節点自重 (FEM へ入れる自重も同じ) も同じ浮力を差し引く (利用者の判断 2026-11-22)。
            // 区間 0〜−10 は水面 −5 で半分だけ沈む → 上の節点へ半分・下の節点へ半分
            var nodes = pile.NodeSelfWeights();
            Assert.AreEqual(4, nodes.Count, "節点は区間数 + 1");
            Assert.AreEqual(wBuoyant, nodes.Sum(), 1e-6, "節点自重の合計が引抜きの W と違う");
            double seg0 = section.W * 10 - 10.0 * section.DisplacedVolumePerM * 5;
            double seg1 = section.W * 8 - 10.0 * section.DisplacedVolumePerM * 8;
            Assert.AreEqual(seg0 / 2, nodes[0], 1e-9);
            Assert.AreEqual(seg0 / 2 + seg1 / 2, nodes[1], 1e-9);
            string vltm = TestSource.Read("Graphics_r1", "FEM", "VerticalLoadTransferMethod.cs");
            StringAssert.Contains(vltm, "Weights.AddRange(soilPile.NodeSelfWeights())", "沈下解析の節点自重が NodeSelfWeights を使っていない");
            StringAssert.Contains(vltm, "PileWeight += soilPile.EffectiveSegmentWeight(", "沈下解析の自重の合計が浮力を差し引いていない");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "FEM", "AnalysisModelling.cs"), "soilPile.NodeSelfWeights()", "FEM へ入れる自重が沈下解析と別の式");
            StringAssert.DoesNotMatch(vltm + TestSource.Read("Graphics_r1", "FEM", "AnalysisModelling.cs"),
                new System.Text.RegularExpressions.Regex(@"PileSection\.W \*"), "自重の写しが残っている");
            // 評定式の工法 (Smart-MAGNUM・Hybridニーディング) も同じ W を使う (利用者の判断 2026-11-22)。ヘルプがそう書いていること
            string help = Help();
            StringAssert.Contains(help, "$W_s$: 杭の自重。地下水位以下の部分は浮力を差し引きます", "Hybridニーディングの W_s の説明がない");
            StringAssert.Contains(help, "杭の自重（地下水位以下の部分は浮力を差し引く）を抵抗に足しています", "Smart-MAGNUM の引抜きの自重の説明がない");

            // 計算例 (W = 500 kN と置く)
            string table = Tables()["cond-uplift"];
            StringAssert.Contains(table, $"$\\psi = {psi:0.000}$ m");
            StringAssert.Contains(table, $"で ${N0(sand)}$ kN、粘性土");
            StringAssert.Contains(table, $"で ${N0(clay)}$ kN、周面の計 ${N0(friction)}$ kN");
            StringAssert.Contains(table, $"$R_{{TU}} = {N0(friction + 500)}$ kN (使用限界 ${N0((friction + 500) / 3)}$ kN)、"
                + $"$R_{{TY}} = {N0(2.0 / 3.0 * friction + 500)}$ kN、$R_{{TR}} = {N0(friction / 1.2 + 500)}$ kN");
        }

        /// <summary>場所打ち RC 杭のせん断の計算例が、検定と同じ関数 (DescribeShearLimit) の値と一致する。</summary>
        [TestMethod]
        public void TheInsituRcShearExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-insitu-rc-shear"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.RcSection]();
            Assert.AreEqual((1000.0, 27.0, 0.75, 20, "D25", "D13", 150.0), (s.PileDiameter, s.ConcreteFc, s.ConcreteGsi, s.MainBarNum, s.MainBarSize, s.HoopSize, s.HoopSpacing), "(前提) 代表断面が変わった");
            double Q(SectionLimitState limit, int level) => s.DescribeShearLimit(limit, level, true, 2.0, 3000.0)!.ValueN / 1000;
            StringAssert.Contains(table, $"$p_w = {s.HoopPw * 100:0.000}$%");
            StringAssert.Contains(table, $"$Q_s = {F1(Q(SectionLimitState.Service, 1))}$ kN");
            StringAssert.Contains(table, $"$Q_d = {F1(Q(SectionLimitState.Damage, 1))}$ kN (レベル1)・${F1(Q(SectionLimitState.Damage, 2))}$ kN (レベル2)");
            StringAssert.Contains(table, $"$Q_u = {F1(Q(SectionLimitState.Ultimate, 2))}$ kN");

            // 表の「採用値」: β2 は σ0 ≦ ξFc/3 で 0.75、レベル1 の損傷限界は β2 を掛けない、安全限界の β1 は 0.8
            Assert.AreEqual(0.75, Q(SectionLimitState.Damage, 2) / Q(SectionLimitState.Damage, 1), 1e-9);
            string code = TestSource.Read("Graphics_r1", "Models", "InputData", "InsituReinforcedConcreteSection.cs");
            StringAssert.Contains(code, "private const double ShearKc = 0.72;");
            StringAssert.Contains(code, "private const double ShearBeta1Ultimate = 0.8;");

            // M/(Q·d) は 1.0〜3.0 に収める (3 つの限界状態とも)。2026-11-22 まで頭打ちせず、1 未満で耐力を大きく見ていた
            foreach (var (limit, level) in new[] { (SectionLimitState.Service, 1), (SectionLimitState.Damage, 2), (SectionLimitState.Ultimate, 2) })
            {
                double At(double monQd) => s.DescribeShearLimit(limit, level, true, monQd, 3000.0)!.ValueN;
                Assert.AreEqual(At(1.0), At(0.5), 1e-9, $"{limit}: M/(Q·d) = 0.5 を 1.0 に収めていない");
                Assert.AreEqual(At(3.0), At(5.0), 1e-9, $"{limit}: M/(Q·d) = 5 を 3.0 に収めていない");
                Assert.IsTrue(At(1.5) > At(2.5), $"{limit}: 範囲内では M/(Q·d) が大きいほど耐力が小さいはず");
            }
        }

        /// <summary>
        /// 場所打ち鋼管コンクリート杭のせん断の計算例が、検定と同じ関数 (DescribeShearLimit) の値と一致する。
        /// 安全限界の p は鋼管の分担軸力 Ns から (ヘルプは 2026-11-22 まで N/(fcy·A) と書いていた)。
        /// </summary>
        [TestMethod]
        public void TheInsituSteelPipeShearExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-insitu-steel-pipe-shear"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.SteelPipeConcreteSection]();
            Assert.AreEqual((1000.0, 12.0, "SKK400", 1.0, 27.0, 1.0, 976.0), (s.PipeDia, s.PipeTs, s.PipeGrade, s.CorrosionDepth, s.ConcreteFc, s.ConcreteGsi, s.ConcreteOutDia), "(前提) 代表断面が変わった");
            double Q(SectionLimitState limit, double n) => s.DescribeShearLimit(limit, 2, true, 2.0, n)!.ValueN / 1000;
            StringAssert.Contains(table, $"$Q_s = {N1(Q(SectionLimitState.Service, 3000))}$ kN、$Q_d = {N1(Q(SectionLimitState.Damage, 3000))}$ kN");
            StringAssert.Contains(table, $"$Q_u = {N1(Q(SectionLimitState.Ultimate, 3000))}$ kN ($N = 0$ なら ${N1(Q(SectionLimitState.Ultimate, 0))}$ kN)");
            Assert.AreEqual(Q(SectionLimitState.Service, 0), Q(SectionLimitState.Service, 3000), 1e-9, "使用限界は軸力に依らない");

            // p は鋼管の分担軸力から: Ns = N·fcy·A/(ξFc·Ac + fcy·A)
            double area = Math.PI * 11 * (998 - 11), fcyA = 1.1 * 235 * area / 1000, fcAc = 27 * Math.PI / 4 * 976 * 976 / 1000;
            double ns = 3000 * fcyA / (fcAc + fcyA);
            StringAssert.Contains(table, $"$N_s = {ns:0.0}$ kN・$p = {ns / fcyA:0.000}$");
            double qu = 2.0 / 3.0 * Math.PI * 11 * (998 - 11) * 1.1 * 235 / Math.Sqrt(3) * Math.Sqrt(1 - Math.Pow(ns / fcyA, 2)) / 1000;
            Assert.AreEqual(qu, Q(SectionLimitState.Ultimate, 3000), 1e-6, "安全限界の p が鋼管の分担軸力から求まっていない");
        }

        /// <summary>
        /// PHC 杭の曲げの計算例が、検定と同じ N-M 曲線 (GetLimitNMCurve) の値と一致する。
        /// あわせて、低減後の曲線の軸力の範囲 (σe + σ0e が 4〜35 / 65 N/mm²) が表のとおりであること。
        /// </summary>
        [TestMethod]
        public void ThePhcBendingExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-phc-bending"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.Phc]();
            if (s.CreateSectionCalculator() is not PrecastPileSection c) { Assert.Fail("(前提) PHC の断面計算オブジェクトを作れない"); return; }
            Assert.AreEqual((600.0, 90.0, 85.0, 8.0), (s.PileDiameter, s.ConcreteThickness, c.Fc, c.SigmaE), "(前提) 代表断面が変わった");

            double M(bool factored, SectionLimitState limit, int level, double nKN) => CurveMomentAt(s, factored, limit, level, nKN);

            double s0e = 1000e3 / c.Ae, sum = c.SigmaE + s0e;
            double ms1 = c.Ze * (-c.Fts + sum) / 1e6, ms2 = c.Ze * (c.Fcs - sum) / 1e6;
            StringAssert.Contains(table, $"$M_{{s1}} = {ms1:0.0}$・$M_{{s2}} = {ms2:0.0}$ で $M_s = 0.9 \\times {ms2:0.0} = {M(true, SectionLimitState.Service, 1, 1000):0.0}$ kN·m");
            Assert.AreEqual(0.9 * Math.Min(ms1, ms2), M(true, SectionLimitState.Service, 1, 1000), 1e-6);
            StringAssert.Contains(table, $"$M_d = {M(true, SectionLimitState.Damage, 1, 1000):0.0}$ kN·m (レベル1)・${M(true, SectionLimitState.Damage, 2, 1000):0.0}$ kN·m (レベル2)");
            double mu0 = M(false, SectionLimitState.Ultimate, 2, 1000);
            StringAssert.Contains(table, $"$M_{{u0}} = {mu0:0.0}$ で $M_u = 0.8 \\times 0.65 \\times {mu0:0.0} = {M(true, SectionLimitState.Ultimate, 2, 1000):0.0}$ kN·m");
            Assert.AreEqual(0.8 * 0.65 * mu0, M(true, SectionLimitState.Ultimate, 2, 1000), 1e-6);

            // 低減後の曲線の範囲: σe + σ0e = 4 N/mm² から (使用限界・損傷限界)。損傷限界は 35 N/mm² まで
            double NAt(double stress) => (stress - c.SigmaE) * c.Ae / 1000;
            Assert.AreEqual(NAt(4), s.GetLimitNMCurve(true, SectionLimitState.Service, 1).N.Min(), 1e-6);
            Assert.AreEqual(NAt(4), s.GetLimitNMCurve(true, SectionLimitState.Damage, 2).N.Min(), 1e-6);
            Assert.AreEqual(NAt(35), s.GetLimitNMCurve(true, SectionLimitState.Damage, 2).N.Max(), 1e-6);
        }

        /// <summary>N-M 曲線から軸力 <paramref name="nKN"/> の曲げモーメントを読む (同じ軸力に区間が複数あれば大きい方)。</summary>
        private static double CurveMomentAt(PileSection s, bool factored, SectionLimitState limit, int level, double nKN)
        {
            var (ns, ms) = s.GetLimitNMCurve(factored, limit, level);
            double best = double.NaN;
            for (int i = 0; i + 1 < ns.Count; i++)
                if (Math.Min(ns[i], ns[i + 1]) <= nKN && nKN <= Math.Max(ns[i], ns[i + 1]))
                {
                    double t = ns[i + 1] == ns[i] ? 0 : (nKN - ns[i]) / (ns[i + 1] - ns[i]);
                    double v = ms[i] + t * (ms[i + 1] - ms[i]);
                    if (double.IsNaN(best) || v > best) best = v;
                }
            return best;
        }

        /// <summary>
        /// PRC 杭の曲げの計算例が、検定と同じ N-M 曲線の値と一致する。損傷限界はレベル1 にも β1 = 0.8 を掛ける (PHC は 1.0)。
        /// 低減後の曲線は範囲の外で M = 0 になり、検定はそれを「範囲外」とする (M &gt; 0 でないと判定しない)。
        /// </summary>
        [TestMethod]
        public void ThePrcBendingExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-prc-bending"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.Prc]();
            if (s.CreateSectionCalculator() is not PrecastPileSection c) { Assert.Fail("(前提) PRC の断面計算オブジェクトを作れない"); return; }
            Assert.AreEqual(("CPRC-300-85-Ⅰ", 300.0, 60.0, 85.0, 6.7), (s.SelectedPrecastPile?.Name, s.PileDiameter, s.ConcreteThickness, c.Fc, c.SigmaE), "(前提) 代表断面が変わった");
            double M(bool factored, SectionLimitState limit, int level) => CurveMomentAt(s, factored, limit, level, 500);
            static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

            double ms2 = c.Ze * (c.Fcs - c.SigmaE - 500e3 / c.Ae) / 1e6;
            StringAssert.Contains(table, $"$M_{{s2}} = {F2(ms2)}$ kN·m が小さく $M_s = 0.8 \\times {F2(ms2)} = {F2(M(true, SectionLimitState.Service, 1))}$ kN·m");
            double md0 = M(false, SectionLimitState.Damage, 2);
            StringAssert.Contains(table, $"$M_{{d0}} = {F2(md0)}$ で $M_d = {F2(M(true, SectionLimitState.Damage, 1))}$ kN·m (レベル1)・${F2(M(true, SectionLimitState.Damage, 2))}$ kN·m (レベル2)");
            Assert.AreEqual(0.8 * md0, M(true, SectionLimitState.Damage, 1), 1e-6, "PRC の損傷限界レベル1 は β1 = 0.8");
            double mu0 = M(false, SectionLimitState.Ultimate, 2);
            StringAssert.Contains(table, $"$M_{{u0}} = {F2(mu0)}$ で $M_u = {F2(M(true, SectionLimitState.Ultimate, 2))}$ kN·m");
            Assert.AreEqual(0.8 * 0.65 * mu0, M(true, SectionLimitState.Ultimate, 2), 1e-6);

            // σe + σ0e が 4 N/mm² 未満の損傷限界は 0 (検定は「範囲外」)
            Assert.AreEqual(0.0, CurveMomentAt(s, true, SectionLimitState.Damage, 2, (2.0 - c.SigmaE) * c.Ae / 1000), 1e-9);
        }

        /// <summary>
        /// SC 杭の曲げの計算例が、検定と同じ N-M 曲線の値と一致する。低減係数は 1.0 (範囲内では低減の前後で同じ)、
        /// 低減後の曲線は軸力比 −0.40〜0.50 の範囲だけ。ヘルプの換算断面 (Ae = Ac + n·As) も実装と合わせる
        /// (2026-11-22 まで (n − 1)·Ap·rp² の形で書かれていた)。
        /// </summary>
        [TestMethod]
        public void TheScBendingExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-sc-bending"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.Sc]();
            if (s.CreateSectionCalculator() is not SCSection c) { Assert.Fail("(前提) SC の断面計算オブジェクトを作れない"); return; }
            Assert.AreEqual(("SC-400-標準-80-4.5", "SKK400", 1.0), (s.SelectedPrecastPile?.Name, s.PipeGrade, s.CorrosionDepth), "(前提) 代表断面が変わった");
            static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

            foreach (var (limit, name) in new[] { (SectionLimitState.Service, "M_s"), (SectionLimitState.Damage, "M_d"), (SectionLimitState.Ultimate, "M_u") })
            {
                double factored = CurveMomentAt(s, true, limit, 2, 1000), unfactored = CurveMomentAt(s, false, limit, 2, 1000);
                StringAssert.Contains(table, $"${name} = {F2(factored)}$ kN·m", $"{name} が表と違います");
                Assert.AreEqual(unfactored, factored, 1e-6, $"{name}: SC 杭の低減係数は 1.0");
            }
            Assert.AreEqual(CurveMomentAt(s, true, SectionLimitState.Damage, 1, 1000), CurveMomentAt(s, true, SectionLimitState.Damage, 2, 1000), 1e-9, "損傷限界はレベル1・2 で同じ");
            StringAssert.Contains(table, $"$N = {N1(c.BendNMin / 1000)}$〜${N1(c.BendNMax / 1000)}$ kN");
            StringAssert.Contains(table, $"$N/N_0 = {1000e3 / (c.BendNMax / 0.5):0.000}$");
            Assert.AreEqual(c.Ae, c.Ac + 5 * c.Asp, 1e-6, "換算断面積は Ac + n·As (n = 5)");
            // 範囲の外は曲線が無い (NaN) か 0。検定はどちらも M > 0 でないので「範囲外」にする
            Assert.IsFalse(CurveMomentAt(s, true, SectionLimitState.Ultimate, 2, c.BendNMin / 1000 - 10) > 0, "範囲の外で曲げ耐力が残っている");
        }

        /// <summary>
        /// SC 杭のせん断の計算例が、検定と同じ関数 (DescribeShearLimit) の値と一致する。
        /// 使用限界・損傷限界は軸力に依らず低減の前後で同じ、安全限界は β = β1·β2 (基本設定の既定 1.0 × 0.75)、
        /// 鋼管だけの降伏軸力 sNy 以上で Qu = 0 (曲線の範囲の圧縮側より手前)。
        /// </summary>
        [TestMethod]
        public void TheScShearExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-sc-shear"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.Sc]();
            if (s.CreateSectionCalculator() is not SCSection c) { Assert.Fail("(前提) SC の断面計算オブジェクトを作れない"); return; }
            Assert.AreEqual(("SC-400-標準-80-4.5", 398.0, 3.5, 235.0), (s.SelectedPrecastPile?.Name, c.PrecastSteelPipe.OutDia, c.PrecastSteelPipe.T, c.PrecastSteelPipe.F), "(前提) 代表断面が変わった");

            double b1 = ConcreteModelOptions.ScUltimateShearBeta1, b2 = ConcreteModelOptions.ScUltimateShearBeta2;
            ConcreteModelOptions.ScUltimateShearBeta1 = ConcreteModelOptions.DefaultScUltimateShearBeta1;
            ConcreteModelOptions.ScUltimateShearBeta2 = ConcreteModelOptions.DefaultScUltimateShearBeta2;
            try
            {
                double Q(SectionLimitState limit, bool factored, double monQd, double n) => s.DescribeShearLimit(limit, 2, factored, monQd, n)!.ValueN / 1000;

                StringAssert.Contains(table, $"$Q_s = {N1(Q(SectionLimitState.Service, true, 3, 500))}$ kN、$Q_d = {N1(Q(SectionLimitState.Damage, true, 3, 500))}$ kN");
                foreach (var limit in new[] { SectionLimitState.Service, SectionLimitState.Damage })
                {
                    Assert.AreEqual(Q(limit, false, 3, 500), Q(limit, true, 3, 500), 1e-9, $"{limit}: 低減係数は 1.0");
                    Assert.AreEqual(Q(limit, true, 3, 500), Q(limit, true, 0.5, -300), 1e-9, $"{limit}: 軸力にも M/(Q·d) にも依らない");
                }
                Assert.AreEqual(Math.PI * 3.5 * (398 - 3.5) * 235 / (1.5 * Math.Sqrt(3)) / 2 / 1000, Q(SectionLimitState.Service, true, 3, 500), 1e-6, "Qs = fs·As/κs");

                double sNy = 1.1 * 235 * c.PrecastSteelPipe.As / 1000;
                StringAssert.Contains(table, $"${{}}_sN_y = {N1(sNy)}$ kN、$\\eta = {500 / sNy:0.000}$");
                foreach (var (monQd, aOverD) in new[] { (3.0, "2.7"), (1.0, "0.9") })
                {
                    double un = Q(SectionLimitState.Ultimate, false, monQd, 500), fa = Q(SectionLimitState.Ultimate, true, monQd, 500);
                    StringAssert.Contains(table, $"$M/(Q\\!\\cdot\\!d) = {monQd:0.0}$ ($a/D = {aOverD}$) なら ${{}}_sQ_{{un}} = {N1(un)}$ kN・$Q_u = {N1(fa)}$ kN");
                    Assert.AreEqual(0.75 * un, fa, 1e-9, "安全限界の β は既定 1.0 × 0.75");
                }
                double q0 = 2 * 3.5 * (398 - 3.5) * 1.1 * 235 / Math.Sqrt(3) / 1000;
                Assert.AreEqual(0.5 * q0 * Math.Sqrt(1 - Math.Pow(500 / sNy, 2)), Q(SectionLimitState.Ultimate, false, 3, 500), 1e-6, "a/D > 1.0 は (8.26) の 1/2");

                Assert.AreEqual(0.0, Q(SectionLimitState.Ultimate, true, 3, sNy + 1), 1e-9, "sNy 以上で Qu = 0");
                Assert.IsTrue(sNy < c.ShearNMaxUltimate / 1000, "(前提) sNy は曲線の範囲の圧縮側より手前");
                StringAssert.Contains(table, $"$N = {N1(c.ShearNMinUltimate / 1000)}$〜${N1(c.ShearNMaxUltimate / 1000)}$ kN");
            }
            finally
            {
                ConcreteModelOptions.ScUltimateShearBeta1 = b1;
                ConcreteModelOptions.ScUltimateShearBeta2 = b2;
            }
        }

        /// <summary>
        /// 液状化の判定の計算例を、地盤ウィンドウの計算 (GroundLayerViewModel.Update) で解いて一致すること。
        /// γcy は図3.2.6 のもっとも近い曲線 (実装の表は隣り合う曲線の中間の線。2026-11-23 に図と突き合わせて確認)、
        /// 対象外の条件 (地下水位より上・GL−20 m 以深・Fc &gt; 35%) が表のとおりであること。
        /// </summary>
        [TestMethod]
        public void TheLiquefactionExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-liquefaction"];
            var g = new GroundInput
            {
                GroundTopAltitude = 0.0, BedrockDensity = 19.0, BedrockShearWaveVelocity = 400.0,
                ShallowSoilType = "砂質土", CalculationMethod = "a1(b1)",
                GroundWaterGLDepth = -1.0, GroundAcceleration1 = 2.0, GroundAcceleration2 = 3.5,
            };
            g.GroundLayers = [new GroundLayerInput
            {
                No = 1, BottomGLDepth = -25.0, LayerThickness = 25.0, BottomAltitude = -25.0,
                Name = "砂質土", GranularityClass = "砂質土", Density = 18.0, NValue = 10.0, Vs = 180.0, Es = 7000.0,
            }];
            g.GroundMassesData = [];
            for (int i = 1; i <= 22; i++)
                g.GroundMassesData.Add(new GroundMassDataInput { No = i, GLDepth = -i, H = 1.0, NValue = 10.0, VS0 = 180.0, Fc = 10.0 });
            new PileDesign.ViewModels.GroundLayerViewModel(new PileDesign.ViewModels.MainWindowViewModel()) { GroundInput = g }.Update();

            static string F(double? v, string f) => v!.Value.ToString(f, CultureInfo.InvariantCulture);
            var m = g.GroundMassesData[4];
            Assert.AreEqual(-5.0, m.GLDepth, "(前提) 例の土質点");
            StringAssert.Contains(table, $"$\\sigma_z = {m.SigmaZ:0}$・$\\sigma_z' = {m.SigmaZPrime:0}$ kN/m²、$N_1 = {F(m.N1, "0.00")}$、$\\Delta N_f = {F(m.DeltaNf, "0")}$、$N_a = {F(m.NL, "0.00")}$、$\\tau_L/\\sigma_z' = {F(m.TauLonSigmaZPrime, "0.000")}$、$r_d = {F(m.RD, "0.000")}$");
            StringAssert.Contains(table, $"$\\tau_d/\\sigma_z' = {F(m.TauDonSigmaZPrime[1], "0.000")}$・$F_L = {F(m.FL[1], "0.000")}$・$\\gamma_{{cy}} = {F(m.GammaCy[1], "0")}\\%$");
            StringAssert.Contains(table, $"$\\tau_d/\\sigma_z' = {F(m.TauDonSigmaZPrime[0], "0.000")}$・$F_L = {F(m.FL[0], "0.000")}$・$\\gamma_{{cy}} = 0$");
            Assert.AreEqual(0.0, m.GammaCy[0]!.Value, "FL ≥ 1 なら γcy = 0");
            StringAssert.Contains(table, $"$D_{{cy}} = {F(g.GroundMassesData[0].SigmaGammaCyH[1], "0")}$ mm");

            // 手計算: σz' = 18·5 − 10·4、Na = √(100/σz')·N + ΔNf (Fc = 10 で 6)、τd/σz' = 0.65·amax/9.8·σz/σz'·(1 − 0.015z)
            Assert.AreEqual(50.0, m.SigmaZPrime, 1e-9);
            Assert.AreEqual(Math.Sqrt(2) * 10 + 6, m.NL!.Value, 1e-9);
            Assert.AreEqual(0.65 * 3.5 / 9.8 * 90 / 50 * (1 - 0.015 * 5), m.TauDonSigmaZPrime[1]!.Value, 1e-9);

            // γcy はもっとも近い曲線。実装の表 (Liquefaction.NaSet_0510 など) は曲線そのものではなく、隣り合う曲線の中間の線。
            // GL−8 m (τd/σz' = 0.398、Na = 17.62) は図の 1% (Na ≈ 20.5) と 2% (≈ 14.5) のほぼ中間で、中間の線 (17.2) よりわずかに右なので 1%
            Assert.AreEqual(1.0, g.GroundMassesData[7].GammaCy[1]!.Value, "1% と 2% の中間の線より右は 1%");
            Assert.AreEqual(2.0, Liquefaction.CalculateGammaCy(16.0, 0.4), "1% と 2% の中間の線より左は 2%");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Help", "help.html"), "もっとも近い曲線のひずみ</strong>を採ります");

            // 対象外: GL−20 m ちょうど、Fc > 35%、地下水位より上
            Assert.IsFalse(g.GroundMassesData[19].IsLiquefactionLayer, "GL−20 m ちょうどは対象外");
            Assert.IsFalse(Liquefaction.IsLiquefactionLayer(-1.0, -5.0, 35.1), "Fc > 35% は対象外");
            Assert.IsTrue(Liquefaction.IsLiquefactionLayer(-1.0, -5.0, 35.0));
            Assert.IsFalse(Liquefaction.IsLiquefactionLayer(-3.0, -2.0, 10.0), "地下水位より上は対象外");
        }

        /// <summary>
        /// 単杭沈下の表: 杭先端の曲線の計算例が実装 (VerticalLoadTransferMethod.GetSettlementPileToe) と一致し、
        /// 例に使った α・n がプリセットにあること、τ-s の折れ点が表のとおりであること。
        /// </summary>
        [TestMethod]
        public void TheSinglePileSettlementTableMatchesTheImplementation()
        {
            string table = Tables()["cond-single-pile-settlement"];
            var toe = typeof(PileDesign.FEM.VerticalLoadTransferMethod).GetMethod("GetSettlementPileToe",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(toe, "(前提) 杭先端の曲線の関数 GetSettlementPileToe が見つからない (名前を変えたらここも直す)");
            double Sp(double ratio) => (double)toe!.Invoke(null, [1.2, ratio * 1000.0, 1000.0, 0.23, 2.7])! * 1000.0; // mm
            StringAssert.Contains(table, $"= {Sp(0.5):0.00}$ mm");
            Assert.AreEqual(120.0, Sp(1.0), 1e-9, "Rp = Rpu で 0.1·dp");

            string csv = TestSource.Read("Graphics_r1", "Models", "PileLibrary", "PresetSettlementParameterSet.csv");
            StringAssert.Contains(csv, "\"(2019)場所打ちコンクリート杭\",\"砂質土\",0.23,2.7", "計算例の α・n がプリセットに無い");

            // τ-s の折れ点 (τ1/τ2・S1・S2 [mm])
            string code = TestSource.Read("Graphics_r1", "Models", "InputData", "SoilPile.cs");
            StringAssert.Contains(code, "\"砂質土\" => (0.8 * pileCircumVertical.Tau2, 5.0, 20.0,");
            StringAssert.Contains(code, "\"礫質土\" => (0.7 * pileCircumVertical.Tau2, 10.0, 30.0,");
            StringAssert.Contains(code, "\"粘性土\" => (0.8 * pileCircumVertical.Tau2, 3.0, 10.0,");
            StringAssert.Contains(table, "砂質土 $0.8\\tau$ ($s = 5$ mm)・$\\tau$ (20 mm)、礫質土 $0.7\\tau$ (10 mm)・$\\tau$ (30 mm)、粘性土 $0.8\\tau$ (3 mm)・$\\tau$ (10 mm)");
        }

        /// <summary>
        /// 群杭沈下 (土層の沈下) の表: 計算例を実装 (Steinnbrener.CalcSettlement) で解いて一致すること、
        /// 荷重面で土層を切ること (GetEffectiveLayersForAnalysis)、個別十字の 5 枚の面積が円と同じこと。
        /// </summary>
        [TestMethod]
        public void TheGroupSettlementTableMatchesTheImplementation()
        {
            string table = Tables()["cond-group-settlement"];
            var load = new System.Collections.ObjectModel.ObservableCollection<RectLoad>
            {
                new RectLoad { X1 = 0, X2 = 10, Y1 = 0, Y2 = 10, QA = 100 * 10 * 10 },
            };
            Assert.AreEqual(100.0, load[0].Q, 1e-9, "(前提) q = 荷重 ÷ 面積");
            SettlementSoilLayer L(double h, double e) => new() { Thickness = h, Ek = e, PoissonsRatio = 0.3 };
            var center = new System.Windows.Point(5, 5);
            double one = Steinnbrener.CalcSettlement(center, load, [L(20, 50000)]) * 1000;
            StringAssert.Contains(table, $"= {one:0.00}$ mm。");
            double two = Steinnbrener.CalcSettlement(center, load, [L(10, 30000), L(10, 80000)]) * 1000;
            StringAssert.Contains(table, $"= {two:0.00}$ mm</td>");

            // 外側の点は 4 枚の足し引き: 荷重を 2 つに割っても同じ沈下 (隣の矩形の寄与の和)
            var outside = new System.Windows.Point(15, 5);
            var split = new System.Collections.ObjectModel.ObservableCollection<RectLoad>
            {
                new RectLoad { X1 = 0, X2 = 5, Y1 = 0, Y2 = 10, QA = 100 * 5 * 10 },
                new RectLoad { X1 = 5, X2 = 10, Y1 = 0, Y2 = 10, QA = 100 * 5 * 10 },
            };
            Assert.AreEqual(Steinnbrener.CalcSettlement(outside, load, [L(20, 50000)]),
                Steinnbrener.CalcSettlement(outside, split, [L(20, 50000)]), 1e-12, "外側の点の足し引きが合わない");

            // 荷重面より上は除き、荷重面を含む層は荷重面から下だけ
            var layers = new System.Collections.ObjectModel.ObservableCollection<SettlementSoilLayer>
            {
                new() { BottomAltitude = -5, Thickness = 5, Ek = 1000, PoissonsRatio = 0.3 },
                new() { BottomAltitude = -15, Thickness = 10, Ek = 2000, PoissonsRatio = 0.3 },
            };
            var cut = PileGroupSettlement.GetEffectiveLayersForAnalysis(0, -8, layers);
            Assert.AreEqual(1, cut.Count);
            Assert.AreEqual(7.0, cut[0].Thickness, 1e-12, "荷重面を含む層を荷重面から下だけにしていない");

            // 個別十字: 5 枚の合計の面積は π r²、荷重は面積で配る
            var cross = PileGroupSettlement.GetCrossRectLoads(new System.Windows.Point(0, 0), 0.6, 1000);
            Assert.AreEqual(5, cross.Count);
            Assert.AreEqual(Math.PI * 0.36, cross.Sum(r => r.A), 1e-9, "十字の面積が円と違う");
            Assert.AreEqual(1000, cross.Sum(r => r.QA), 1e-9, "十字の荷重の合計が杭の荷重と違う");
            Assert.AreEqual(cross[0].Q, cross[1].Q, 1e-9, "十字の荷重度が一様でない");
        }

        /// <summary>
        /// PHC 杭の M-φ の表: 計算例の各点 (ひび割れ・降伏・圧壊・破断・終点) が実装 (PHCSection) と一致し、
        /// 折れ線が 3 点 (原点・ひび割れ・低減後の安全限界) で終わること、終点より先を解析が最後の区間の勾配で延ばすこと。
        /// </summary>
        [TestMethod]
        public void ThePhcMPhiExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-phc-mphi"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.Phc]();
            if (s.CreateSectionCalculator() is not PHCSection c) { Assert.Fail("(前提) PHC の断面計算オブジェクトを作れない"); return; }
            Assert.AreEqual(("PHC-600-標準-85-B", 8.0), (s.SelectedPrecastPile?.Name, c.SigmaE), "(前提) 代表断面が変わった");
            Assert.AreEqual(0.75, c.GetBeta2(0));
            var (mcr, pcr) = c.GetCrackMoment(0);
            var (my, py) = c.GetMomentCurvatureForN(0, "TendonYield");
            var (mcf, _) = c.GetMomentCurvatureForN(0, "ConcreteCompressiveFailure");
            var (mtf, _) = c.GetMomentCurvatureForN(0, "TendonTensileFailure");
            string K(double nmm) => (nmm / 1e6).ToString("0.0", CultureInfo.InvariantCulture);
            string Phi(double perMm) => (perMm * 1e3 * 1e3).ToString("0.000", CultureInfo.InvariantCulture); // 1/m を ×10⁻³ で
            StringAssert.Contains(table, $"$M_{{cr}} = {K(mcr)}$ kN·m・$\\phi_{{cr}} = {Phi(pcr)} \\times 10^{{-3}}$ 1/m");
            StringAssert.Contains(table, $"$M_y = {K(my)}$ kN·m・$\\phi_y = {Phi(py)} \\times 10^{{-3}}$ 1/m");
            StringAssert.Contains(table, $"圧壊 ${K(mcf)}$・破断 ${K(mtf)}$ で $M_{{u0}} = {K(Math.Min(mcf, mtf))}$ kN·m");
            Assert.IsTrue(pcr < py, "(前提) 計算例は (a) ひび割れ → 降伏の順");

            var (phis, ms) = s.GetMPhiRelationship(0); // 1/m・kN·m
            Assert.AreEqual(3, phis.Count, "折れ線は原点・ひび割れ・終点の 3 点");
            double mf = 0.8 * 0.75 * Math.Min(mcf, mtf) / 1e6;
            Assert.AreEqual(mf, ms[2], 1e-6 * mf, "終点は β1β2·Mu0");
            StringAssert.Contains(table, $"= {mf.ToString("0.0", CultureInfo.InvariantCulture)}$ kN·m、$\\phi_f = {(phis[2] * 1e3).ToString("0.000", CultureInfo.InvariantCulture)} \\times 10^{{-3}}$ 1/m");
            // 終点はひび割れ点と降伏点を結ぶ線上
            double onLine = (pcr + (py - pcr) * (mf * 1e6 - mcr) / (my - mcr)) * 1e3;
            Assert.AreEqual(onLine, phis[2], 1e-9 * onLine);

            // 終点より先: 解析は最後の区間の勾配で延ばす (頭打ちにしない)。直したら表の「適用外」も直す
            var curve = new PileDesign.FEM.MomentCurvatureCurve(phis.Zip(ms, (p, m) => (p, m)));
            double slope = (ms[2] - ms[1]) / (phis[2] - phis[1]);
            Assert.AreEqual(ms[2] + slope * phis[2], curve.EvaluateMoment(2 * phis[2]), 1e-6 * ms[2], "終点より先の延ばし方が変わった");
            StringAssert.Contains(table, "終点より先は最後の区間の勾配のまま延ばす");
        }

        /// <summary>
        /// PRC 杭の M-φ の表: 計算例の各点が実装 (PRCSection) と一致し、N = 0 は (a)・N = 800 kN は (b) の場合になること、
        /// 終点がひび割れ点と主筋の降伏点を結ぶ線上の β1β2·Mu0 にあること。
        /// </summary>
        [TestMethod]
        public void ThePrcMPhiExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-prc-mphi"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.Prc]();
            if (s.CreateSectionCalculator() is not PRCSection c) { Assert.Fail("(前提) PRC の断面計算オブジェクトを作れない"); return; }
            Assert.AreEqual(("CPRC-300-85-Ⅰ", 6.7, "SD390", "D13", 6), (s.SelectedPrecastPile?.Name, c.SigmaE, c.MainBars.Grade, c.MainBars.BarSize, c.MainBars.Number), "(前提) 代表断面が変わった");
            Assert.AreEqual(205000.0, c.MainBars.Er);
            string K(double nmm) => (nmm / 1e6).ToString("0.00", CultureInfo.InvariantCulture);
            string P3(double perMm) => (perMm * 1e6).ToString("0.000", CultureInfo.InvariantCulture); // ×10⁻³ 1/m
            string P2(double perMm) => (perMm * 1e6).ToString("0.00", CultureInfo.InvariantCulture);

            var (mcr, pcr) = c.GetCrackMoment(0);
            var (myt, pyt) = c.GetMomentCurvatureForN(0, "RebarTensionYield");
            var (_, pyc) = c.GetMomentCurvatureForN(0, "ReBarCompressionYield");
            var (mcu, _) = c.GetMomentCurvatureForN(0, "ConcreteCompressiveFailure");
            var (mtf, _) = c.GetMomentCurvatureForN(0, "TendonTensileFailure");
            Assert.IsTrue(pcr < pyt && pyt < pyc, "(前提) N = 0 は (a)");
            StringAssert.Contains(table, $"$M_{{cr}} = {K(mcr)}$ kN·m・$\\phi_{{cr}} = {P3(pcr)} \\times 10^{{-3}}$ 1/m");
            StringAssert.Contains(table, $"$M_{{yt}} = {K(myt)}$ kN·m・$\\phi_{{yt}} = {P2(pyt)} \\times 10^{{-3}}$ 1/m");
            StringAssert.Contains(table, $"$\\phi_{{yc}} = {P2(pyc)} \\times 10^{{-3}}$ 1/m なので (a)");
            StringAssert.Contains(table, $"圧壊 ${K(mcu)}$・破断 ${K(mtf)}$ で $M_{{u0}} = {K(Math.Min(mcu, mtf))}$ kN·m");

            var (phis, ms) = s.GetMPhiRelationship(0); // 1/m・kN·m
            Assert.AreEqual(3, phis.Count, "折れ線は原点・ひび割れ・終点の 3 点");
            double mf = 0.8 * 0.75 * Math.Min(mcu, mtf);
            Assert.AreEqual(mf / 1e6, ms[2], 1e-6 * ms[2], "終点は β1β2·Mu0");
            StringAssert.Contains(table, $"= {K(mf)}$ kN·m、$\\phi_f = {(phis[2] * 1e3).ToString("0.000", CultureInfo.InvariantCulture)} \\times 10^{{-3}}$ 1/m");
            double onLine = (pcr + (pyt - pcr) * (mf - mcr) / (myt - mcr)) * 1e3;
            Assert.AreEqual(onLine, phis[2], 1e-9 * onLine, "(a) の終点がひび割れ点と引張降伏点を結ぶ線上にない");

            // N = 800 kN は (b): 圧縮降伏が先
            var (myc8, pyc8) = c.GetMomentCurvatureForN(800e3, "ReBarCompressionYield");
            var (_, pyt8) = c.GetMomentCurvatureForN(800e3, "RebarTensionYield");
            var (mcr8, pcr8) = c.GetCrackMoment(800e3);
            Assert.IsTrue(pcr8 < pyc8 && pyc8 < pyt8, "N = 800 kN が (b) でない");
            var (phis8, ms8) = s.GetMPhiRelationship(800);
            double mf8 = ms8[^1] * 1e6;
            double onLine8 = (pcr8 + (pyc8 - pcr8) * (mf8 - mcr8) / (myc8 - mcr8)) * 1e3;
            Assert.AreEqual(onLine8, phis8[^1], 1e-6 * onLine8, "(b) の終点がひび割れ点と圧縮降伏点を結ぶ線上にない");
        }

        /// <summary>
        /// SC 杭の M-φ の表: (a)〜(d) の折れ点が「基礎部材の強度と変形性能」の骨格曲線 (利用者が示した O・A・B・D と φD の式) どおりで、
        /// 計算例の値が実装 (SCSection) と一致すること。
        /// (b) は 2026-11-22 まで B に鋼管の「引張」降伏点を使っていた (D は圧縮降伏点の線から求めていた)。
        /// </summary>
        [TestMethod]
        public void TheScMPhiFollowsTheSkeletonCurves()
        {
            string table = Tables()["cond-sc-mphi"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.Sc]();
            if (s.CreateSectionCalculator() is not SCSection c) { Assert.Fail("(前提) SC の断面計算オブジェクトを作れない"); return; }
            Assert.AreEqual("SC-400-標準-80-4.5", s.SelectedPrecastPile?.Name, "(前提) 代表断面が変わった");
            Assert.AreEqual((1.0, 1.0), (SCSection.MPhiBeta1, SCSection.MPhiBeta2));
            const double b1 = SCSection.MPhiBeta1;
            string P(double perM) => (perM * 1e3).ToString("0.000", CultureInfo.InvariantCulture);
            string M(double knm) => knm.ToString("0.0", CultureInfo.InvariantCulture);
            string Pt(List<double> phis, List<double> ms, int i) => $"({P(phis[i])}, {M(ms[i])})";

            (double M, double P) Cr(double n) => c.GetCrackMoment(n * 1e3);
            (double M, double P) Y1(double n) => c.GetMomentCurvatureForN(n * 1e3, "SteelPipeTensionYield");
            (double M, double P) Y2(double n) => c.GetMomentCurvatureForN(n * 1e3, "SteelPipeCompressionYield_b");
            (double M, double P) U(double n) => c.GetUltimateMomentForSpecificN(n * 1e3);
            const double toPerM = 1e3, toKnm = 1e-6;

            // (a) N = 0: O・A・B(φy1, My1)・D、φD = φcr + (φy1 − φcr)(β1Mu0 − Mcr)/(My1 − Mcr)
            {
                var (cr, y1, y2, u) = (Cr(0), Y1(0), Y2(0), U(0));
                Assert.IsTrue(cr.P < y1.P && y1.P < y2.P, "(前提) N = 0 は (a)");
                var (phis, ms) = s.GetMPhiRelationship(0);
                Assert.AreEqual(4, phis.Count);
                double phiD = cr.P + (y1.P - cr.P) * (b1 * u.M - cr.M) / (y1.M - cr.M);
                Assert.AreEqual(y1.P * toPerM, phis[2], 1e-9 * phis[2], "(a) の B が引張降伏点でない");
                Assert.AreEqual(phiD * toPerM, phis[3], 1e-9 * phis[3], "(a) の φD");
                Assert.AreEqual(b1 * u.M * toKnm, ms[3], 1e-9 * ms[3]);
                StringAssert.Contains(table, $"$N = 0$ は (a): A {Pt(phis, ms, 1)}・B {Pt(phis, ms, 2)}・D {Pt(phis, ms, 3)}");
            }
            // (b) N = 1,500 kN: O・A・B(φy2, My2)・D、φD = φcr + (φy2 − φcr)(β1Mu0 − Mcr)/(My2 − Mcr)
            {
                var (cr, y1, y2, u) = (Cr(1500), Y1(1500), Y2(1500), U(1500));
                Assert.IsTrue(cr.P < y2.P && y2.P < y1.P, "(前提) N = 1,500 kN は (b)");
                var (phis, ms) = s.GetMPhiRelationship(1500);
                Assert.AreEqual(4, phis.Count);
                double phiD = cr.P + (y2.P - cr.P) * (b1 * u.M - cr.M) / (y2.M - cr.M);
                Assert.AreEqual(y2.P * toPerM, phis[2], 1e-9 * phis[2], "(b) の B が圧縮降伏点でない");
                Assert.AreEqual(y2.M * toKnm, ms[2], 1e-9 * ms[2]);
                Assert.AreEqual(phiD * toPerM, phis[3], 1e-9 * phis[3], "(b) の φD");
                Assert.AreEqual(b1 * u.M * toKnm, ms[3], 1e-9 * ms[3]);
                StringAssert.Contains(table, $"$N = 1{{,}}500$ kN は (b): A {Pt(phis, ms, 1)}・B {Pt(phis, ms, 2)}・D {Pt(phis, ms, 3)}");
            }
            // (c) N = 3,000 kN: O・B(φy2, My2)・D、φD = β1Mu0 / (My2/φy2)
            {
                var (cr, y1, y2, u) = (Cr(3000), Y1(3000), Y2(3000), U(3000));
                Assert.IsTrue(y2.P < cr.P && y2.P < y1.P, "(前提) N = 3,000 kN は (c)");
                var (phis, ms) = s.GetMPhiRelationship(3000);
                Assert.AreEqual(3, phis.Count);
                Assert.AreEqual(y2.P * toPerM, phis[1], 1e-9 * phis[1]);
                Assert.AreEqual(b1 * u.M / (y2.M / y2.P) * toPerM, phis[2], 1e-9 * phis[2], "(c) の φD");
                StringAssert.Contains(table, $"$N = 3{{,}}000$ kN は (c): B {Pt(phis, ms, 1)}・D {Pt(phis, ms, 2)}");
            }
            // (d) N = 5,000 kN: O・D(φu, β1Mu0)
            {
                var u = U(5000);
                var (phis, ms) = s.GetMPhiRelationship(5000);
                Assert.AreEqual(2, phis.Count, "(d) は O と D の 2 点");
                Assert.AreEqual(u.P * toPerM, phis[1], 1e-9 * phis[1], "(d) の φD は φu");
                Assert.AreEqual(b1 * u.M * toKnm, ms[1], 1e-9 * ms[1]);
                StringAssert.Contains(table, $"$N = 5{{,}}000$ kN は (d): D {Pt(phis, ms, 1)}");
            }
        }

        /// <summary>
        /// 場所打ち RC 杭の曲げの表: 計算例が検定と同じ N-M 曲線の値と一致し、低減係数 (使用 1.0、損傷 L1 1.0・L2 1.0/0.65、
        /// 安全 0.95/0.52) と軸力の範囲 (−0.05〜0.4 ξFc·πD²/4) が表のとおりであること。
        /// </summary>
        [TestMethod]
        public void TheInsituRcBendingExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-insitu-rc-bending"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.RcSection]();
            if (s.CreateSectionCalculator() is not InsituReinforcedConcreteSection c) { Assert.Fail("(前提) RC の断面計算オブジェクトを作れない"); return; }
            Assert.AreEqual((1000.0, 27.0, 0.75, 23.0, "SD390", 20, "D25", 600.0), (s.PileDiameter, s.ConcreteFc, s.ConcreteGsi, s.ConcreteGamma, c.MainBars.Grade, s.MainBarNum, s.MainBarSize, c.MainBarPCD), "(前提) 代表断面が変わった");
            string K(double v) => v.ToString("#,0.0", CultureInfo.InvariantCulture).Replace(",", "{,}");
            StringAssert.Contains(table, $"$E_c = {c.InsituConcrete.Ec.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", "{,}")}$ N/mm²");
            Assert.AreEqual(0.003, c.InsituConcrete.EpsilonCu);

            double M(bool f, SectionLimitState l, int lv, double n) => CurveMomentAt(s, f, l, lv, n);
            // N = 3,000 kN: σ0 ≦ ξFc/3 (β は使用 1.0・損傷 1.0・安全 0.95)
            double ms = M(true, SectionLimitState.Service, 1, 3000), md1 = M(true, SectionLimitState.Damage, 1, 3000);
            double mu0 = M(false, SectionLimitState.Ultimate, 2, 3000), mu = M(true, SectionLimitState.Ultimate, 2, 3000);
            Assert.AreEqual(M(false, SectionLimitState.Service, 1, 3000), ms, 1e-9 * ms, "使用限界の β1 は 1.0");
            Assert.AreEqual(md1, M(true, SectionLimitState.Damage, 2, 3000), 1e-9 * md1, "σ0 ≦ ξFc/3 の損傷限界 L2 は β2 = 1.0");
            Assert.AreEqual(0.95 * mu0, mu, 1e-3 * mu, "安全限界は 0.95");
            StringAssert.Contains(table, $"$M_s = {K(ms)}$ kN·m、$M_d = {K(md1)}$ kN·m (レベル1・2 とも)、$M_{{u0}} = {K(mu0)}$ で $M_u = 0.95 \\times M_{{u0}} = {K(mu)}$ kN·m");

            // N = 6,000 kN: σ0 > ξFc/3 (損傷 L2 は 0.65、安全は 0.52)
            double d1 = M(true, SectionLimitState.Damage, 1, 6000), d2 = M(true, SectionLimitState.Damage, 2, 6000);
            double u0 = M(false, SectionLimitState.Ultimate, 2, 6000), u = M(true, SectionLimitState.Ultimate, 2, 6000);
            Assert.AreEqual(0.65 * d1, d2, 1e-3 * d2, "損傷限界 L2 は 0.65");
            Assert.AreEqual(0.52 * u0, u, 1e-3 * u, "安全限界は 0.80 × 0.65");
            Assert.IsTrue(double.IsNaN(M(true, SectionLimitState.Service, 1, 6000)), "N = 6,000 kN は使用限界の曲線の外のはず");
            StringAssert.Contains(table, $"$M_d = {K(d1)}$ (レベル1)・$0.65 \\times {K(d1)} = {K(d2)}$ kN·m (レベル2)、$M_{{u0}} = {K(u0)}$ で $M_u = {K(u)}$ kN·m");

            // 低減後の曲線の範囲: −0.05〜0.4 ξFc·πD²/4
            double ag = Math.PI * 1000 * 1000 / 4, xfc = 0.75 * 27;
            Assert.AreEqual(-0.05 * xfc * ag, c.UltimateLimitAxialForceThresholds[0], 1e-6);
            Assert.AreEqual(0.4 * xfc * ag, c.UltimateLimitAxialForceThresholds[^1], 1e-6);
            Assert.AreEqual(-0.05 * xfc * ag, c.DamageLimitAxialForceThresholds[0], 1e-6);
            Assert.AreEqual(0.4 * xfc * ag, c.DamageLimitAxialForceThresholds[^1], 1e-6);
            Assert.IsFalse(M(true, SectionLimitState.Ultimate, 2, 0.4 * xfc * ag / 1000 + 50) > 0, "範囲の外で曲げ耐力が残っている");
        }

        /// <summary>
        /// 場所打ち RC 杭の M-φ の表: 低い軸力は O・ひび割れ・降伏・C (式 3.12)、高い軸力はひび割れ点と C を結ぶ線上の β1β2·Mu0 で止める。
        /// 計算例の値が実装 (InsituReinforcedConcreteSection) と一致し、Mcr が式 3.10 とほぼ同じであること。
        /// </summary>
        [TestMethod]
        public void TheInsituRcMPhiExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-insitu-rc-mphi"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.RcSection]();
            if (s.CreateSectionCalculator() is not InsituReinforcedConcreteSection c) { Assert.Fail("(前提) RC の断面計算オブジェクトを作れない"); return; }
            Assert.AreEqual((1000.0, 27.0, 0.75, "SD390", 20, "D25"), (s.PileDiameter, s.ConcreteFc, s.ConcreteGsi, c.MainBars.Grade, s.MainBarNum, s.MainBarSize), "(前提) 代表断面が変わった");
            string P(double perMm) => (perMm * 1e6).ToString("0.000", CultureInfo.InvariantCulture); // ×10⁻³ 1/m
            string K(double nmm) => (nmm / 1e6).ToString("#,0.0", CultureInfo.InvariantCulture).Replace(",", "{,}"); // 数式の中
            string Kp(double nmm) => (nmm / 1e6).ToString("#,0.0", CultureInfo.InvariantCulture); // 括弧の点の中

            // 低い軸力 N = 3,000 kN: O・ひび割れ・降伏・C(φc, 0.95·Mu0)
            {
                var (mcr, pcr) = c.GetCrackMoment(3000e3, false);
                var (mcrLinear, _) = c.GetCrackMoment(3000e3, true);
                Assert.AreEqual(mcrLinear, mcr, 1e-3 * mcr, "断面解析の Mcr が式 3.10 と離れている");
                var (my, py) = c.GetSteelYieldMoment(3000e3);
                var (mu0, _) = c.GetUltimateMomentForSpecificN(3000e3);
                var (phis, ms) = s.GetMPhiRelationship(3000);
                Assert.AreEqual(4, phis.Count, "低い軸力は O・ひび割れ・降伏・C");
                double phiC = InsituReinforcedConcreteSection.GetPhiC(pcr, mcr, py, my, mu0, 0.95);
                Assert.AreEqual(pcr + (py - pcr) * (0.95 * mu0 - mcr) / (my - mcr), phiC, 1e-12, "式 3.12");
                Assert.AreEqual(phiC * 1e3, phis[3], 1e-9 * phis[3]);
                Assert.AreEqual(0.95 * mu0 / 1e6, ms[3], 1e-9 * ms[3]);
                StringAssert.Contains(table, $"ひび割れ ({P(pcr)}, {Kp(mcr)})・降伏 ({P(py)}, {Kp(my)})・$M_{{u0}} = {K(mu0)}$ で C ({P(phiC)}, {Kp(0.95 * mu0)})");
            }
            // 高い軸力 N = 6,000 kN: ひび割れ点と C を結ぶ線上の 0.52·Mu0 で止める
            {
                var (mcr, pcr) = c.GetCrackMoment(6000e3, false);
                var (my, py) = c.GetSteelYieldMoment(6000e3);
                var (mu0, _) = c.GetUltimateMomentForSpecificN(6000e3);
                var (phis, ms) = s.GetMPhiRelationship(6000);
                Assert.AreEqual(3, phis.Count, "高い軸力は O・ひび割れ・終点");
                double mEnd = 0.80 * 0.65 * mu0;
                double phiC = InsituReinforcedConcreteSection.GetPhiC(pcr, mcr, py, my, mu0, 0.80);
                double phiEnd = pcr + (phiC - pcr) * (mEnd - mcr) / (0.80 * mu0 - mcr);
                Assert.AreEqual(phiEnd * 1e3, phis[2], 1e-9 * phis[2], "終点がひび割れ点と C を結ぶ線上にない");
                Assert.AreEqual(mEnd / 1e6, ms[2], 1e-9 * ms[2]);
                StringAssert.Contains(table, $"ひび割れ ({P(pcr)}, {Kp(mcr)})・終点 ({P(phiEnd)}, {Kp(mEnd)}) ($= 0.52 \\times {K(mu0)}$)");
            }
            // Mcr ≥ β1β2·Mu0 なら弾性で終点まで (N = 9,000 kN)
            {
                var (mcr, _) = c.GetCrackMoment(9000e3, false);
                var (mu0, _) = c.GetUltimateMomentForSpecificN(9000e3);
                Assert.IsTrue(mcr >= 0.52 * mu0, "(前提) N = 9,000 kN は Mcr ≥ β1β2·Mu0");
                Assert.AreEqual(2, s.GetMPhiRelationship(9000).Phis.Count, "ひび割れ前に終点へ達するときは O と終点の 2 点");
            }
        }

        /// <summary>
        /// 場所打ち RC 杭の M-θ の表: 計算例の 4 点 (θcr・θy・θu と Mcr・My・0.95Mu0) が実装と一致し、
        /// 軸応力度の範囲 (−ft〜ξFc/4、σ0 = N/Ae) の外では剛にすること。
        /// </summary>
        [TestMethod]
        public void TheInsituRcMThetaExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-insitu-rc-mtheta"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.RcSection]();
            if (s.CreateSectionCalculator() is not InsituReinforcedConcreteSection c) { Assert.Fail("(前提) RC の断面計算オブジェクトを作れない"); return; }
            Assert.AreEqual("D25", s.MainBarSize, "(前提) 代表断面が変わった");
            string F3(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
            string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            string K(double knm) => knm.ToString("#,0.0", CultureInfo.InvariantCulture).Replace(",", "{,}");

            var (thetas, ms) = s.GetMThetaRelationship(3000)!.Value; // rad・kN·m
            Assert.AreEqual(4, thetas.Count);
            var (mcr, _) = c.GetCrackMoment(3000e3, false);
            var (my, py) = c.GetSteelYieldMoment(3000e3);
            var (mu0, _) = c.GetUltimateMomentForSpecificN(3000e3);
            double thetaY = 0.5 * 32 * 25 * py;
            Assert.AreEqual(thetaY, thetas[2], 1e-12, "θy = α·db·φy/2 (α = 32)");
            Assert.AreEqual(mcr / (InsituReinforcedConcreteSection.PostCrackInitialStiffnessFactor * my / thetaY), thetas[1], 1e-15, "θcr = Mcr/K0");
            Assert.AreEqual(10.0, InsituReinforcedConcreteSection.PostCrackInitialStiffnessFactor);
            Assert.AreEqual(0.01, thetas[3], 1e-15, "θu = 1/100");
            Assert.AreEqual(0.95 * mu0 / 1e6, ms[3], 1e-9 * ms[3]);
            StringAssert.Contains(table, $"= {F3(thetaY * 1e3)} \\times 10^{{-3}}$ rad、$M_y = {K(my / 1e6)}$ kN·m");
            StringAssert.Contains(table, $"$\\theta_{{cr}} = {F2(thetas[1] * 1e5)} \\times 10^{{-5}}$ rad ($M_{{cr}} = {(mcr / 1e6):0.0}$)");
            StringAssert.Contains(table, $"$0.95 M_{{u0}} = {K(ms[3])}$ kN·m");

            // 軸応力度の範囲: σ0 = N/Ae が −ft〜ξFc/4 の外は剛
            double upper = 0.25 * 0.75 * 27 * c.Ae / 1000, lower = -c.Ft * c.Ae / 1000;
            Assert.IsTrue(s.IsWithinMThetaValidAxialRange(3000));
            Assert.IsTrue(s.IsWithinMThetaValidAxialRange(upper - 1) && !s.IsWithinMThetaValidAxialRange(upper + 1), "上限は ξFc/4");
            Assert.IsTrue(s.IsWithinMThetaValidAxialRange(lower + 1) && !s.IsWithinMThetaValidAxialRange(lower - 1), "下限は −ft");
            Assert.IsFalse(s.IsWithinMThetaValidAxialRange(5000));
            StringAssert.Contains(table, $"$N = 5{{,}}000$ kN は $\\sigma_0 = {F2(5000e3 / c.Ae)} &gt; {F2(0.25 * 0.75 * 27)}$ で剛");
        }

        /// <summary>
        /// 場所打ち鋼管コンクリート杭の曲げの表: 計算例が検定と同じ N-M 曲線の値と一致し、β = 1.0 (低減前 = 低減後)、
        /// 安全限界の軸力の範囲 (−0.2(σy·Ag + 1.1F·As)〜0.4ξFc·Ae) と腐食代の差し引きが表のとおりであること。
        /// </summary>
        [TestMethod]
        public void TheInsituSteelPipeBendingExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-insitu-steel-pipe-bending"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.SteelPipeConcreteSection]();
            if (s.CreateSectionCalculator() is not InsituSteelPipeReinforcedConcreteSection c) { Assert.Fail("(前提) 断面計算オブジェクトを作れない"); return; }
            var p = c.InsituSteelPipe;
            Assert.AreEqual((998.0, 11.0, 235.0, 1.0, "SD390", 20, "D25"), (p.OutDia, p.T, p.F, s.ConcreteGsi, c.MainBars.Grade, s.MainBarNum, s.MainBarSize), "(前提) 代表断面が変わった (腐食代 1 mm を引いた φ998 × 11)");
            Assert.AreEqual(0.003, c.UltimateCompressiveStrain);
            string K(double v) => v.ToString("#,0.0", CultureInfo.InvariantCulture).Replace(",", "{,}");

            double M(bool f, SectionLimitState l, int lv) => CurveMomentAt(s, f, l, lv, 3000);
            double ms = M(true, SectionLimitState.Service, 1), md = M(true, SectionLimitState.Damage, 1), mu = M(true, SectionLimitState.Ultimate, 2);
            Assert.AreEqual(M(false, SectionLimitState.Service, 1), ms, 1e-9 * ms, "β1 = 1.0");
            Assert.AreEqual(md, M(true, SectionLimitState.Damage, 2), 1e-9 * md, "損傷限界はレベル1・2 同値");
            Assert.AreEqual(M(false, SectionLimitState.Ultimate, 2), mu, 1e-9 * mu, "β1β2 = 1.0");
            StringAssert.Contains(table, $"$M_s = {K(ms)}$ kN·m、$M_d = {K(md)}$ kN·m、$M_u = M_{{u0}} = {K(mu)}$ kN·m");

            double lo = -0.2 * (c.MainBars.RSigmaY * c.MainBars.Ag + 1.1 * p.F * p.AMinus);
            double hi = 0.4 * 1.0 * 27 * c.Ae;
            Assert.AreEqual(lo, c.UltimateLimitAxialForceThresholds[0], 1e-6);
            Assert.AreEqual(hi, c.UltimateLimitAxialForceThresholds[^1], 1e-6);
            StringAssert.Contains(table, $"$-{K(-lo / 1000)}$〜${K(hi / 1000)}$ kN");
            Assert.IsFalse(CurveMomentAt(s, true, SectionLimitState.Ultimate, 2, hi / 1000 + 100) > 0, "範囲の外で曲げ耐力が残っている");
        }

        /// <summary>
        /// 場所打ち鋼管コンクリート杭の M-φ の表: 杭頭部は (φu, Mu0)、杭中間部はひび割れ点と降伏点の線の延長 φC で終わること、
        /// 計算例の値が実装 (InsituSteelPipeReinforcedConcreteSection・MphiCurveResolver) と一致すること。
        /// </summary>
        [TestMethod]
        public void TheInsituSteelPipeMPhiExampleMatchesTheImplementation()
        {
            string table = Tables()["cond-insitu-steel-pipe-mphi"];
            var s = SectionInvariantTests.Recipes[PileDesign.Constants.PileTypeNames.SteelPipeConcreteSection]();
            if (s.CreateSectionCalculator() is not InsituSteelPipeReinforcedConcreteSection c) { Assert.Fail("(前提) 断面計算オブジェクトを作れない"); return; }
            string P(double perMm) => (perMm * 1e6).ToString("0.000", CultureInfo.InvariantCulture);
            string Kp(double nmm) => (nmm / 1e6).ToString("#,0.0", CultureInfo.InvariantCulture);
            string K(double nmm) => Kp(nmm).Replace(",", "{,}");

            var (mcr, pcr) = c.GetCrackMoment(3000e3);
            Assert.AreEqual(c.Ze * (c.Ft + 3000e3 / c.Ae), mcr, 1e-6 * mcr, "Mcr = Ze(ft + σ0e)");
            var (my, py) = c.GetYieldMoment(3000e3);
            var (mu0, pu) = c.GetUltimateMomentForSpecificN(3000e3);

            var (top, topM) = s.GetMPhiRelationship(3000); // 杭頭部 (1/m・kN·m)
            Assert.AreEqual(4, top.Count);
            Assert.AreEqual(pu * 1e3, top[3], 1e-9 * top[3], "杭頭部の終点は φu");
            Assert.AreEqual(mu0 / 1e6, topM[3], 1e-9 * topM[3], "β1 = 1.0");
            var mid = PileDesign.Services.MphiCurveResolver.Resolve(s, 3000, isPileTop: false)!.Value;
            double phiC = pcr + (py - pcr) * (mu0 - mcr) / (my - mcr);
            Assert.AreEqual(phiC * 1e3, mid.Phis[3], 1e-9 * mid.Phis[3], "杭中間部の終点はひび割れ点と降伏点の線の延長");

            StringAssert.Contains(table, $"ひび割れ ({P(pcr)}, {Kp(mcr)})・降伏 ({P(py)}, {Kp(my)})・$M_{{u0}} = {K(mu0)}$");
            StringAssert.Contains(table, $"杭頭部の終局点は ({P(pu)}, {Kp(mu0)})、杭中間部は ({P(phiC)}, {Kp(mu0)})");

            // 杭頭部の範囲は 0.5D
            string modelling = TestSource.Read("Graphics_r1", "FEM", "AnalysisModelling.cs");
            StringAssert.Contains(modelling, "pileTopZoneBottom = z0 - 0.5 * pileDia_m;");
        }

        private static string N1(double v) => v.ToString("#,0.0", CultureInfo.InvariantCulture).Replace(",", "{,}");

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
