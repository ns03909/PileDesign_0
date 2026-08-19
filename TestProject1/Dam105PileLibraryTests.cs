using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 三谷セキサン DAM105 パイル (Fc=105 のストレート PRC 杭) の検証。
    ///
    /// 断面タイプは増やさず <b>PRC杭 の製品一覧に連結</b>している。
    ///
    /// このカタログは配筋径を印字していないので Ie から逆算しているが、
    /// PC鋼棒と異形鉄筋は別々の円周にあり、さらに主筋の円は鉄筋径が太いほど内側に寄る。
    /// 同じ断面に種類 (主筋径) 違いが複数あることを使って
    /// 「PC鋼棒の配筋径」と「主筋のかぶり」を最小二乗で解いており、
    /// 残差が小さく収まることがモデルの裏付けになっている。
    /// </summary>
    [TestClass]
    public class Dam105PileLibraryTests
    {
        private const string Prefix = "DAM105-";

        private static List<PrecastPile> _all = [];
        private static List<PrecastPile> _dam = [];
        private static List<Dictionary<string, string>> _raw = [];

        [ClassInitialize]
        public static void Init(TestContext _)
        {
            _all = PileSection.PRCs;
            _dam = [.. _all.Where(p => p.Name.StartsWith(Prefix, StringComparison.Ordinal))];
            _raw = ReadRawCsv();
        }

        private static List<Dictionary<string, string>> ReadRawCsv()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                       "Models", "PileLibrary", "pile_library_PRC_DAM105.csv");
            var lines = File.ReadAllLines(path);
            var head = lines[0].TrimStart('﻿').Split(',');
            return [.. lines.Skip(1).Where(l => l.Length > 0)
                            .Select(l => head.Zip(l.Split(','))
                                             .ToDictionary(x => x.First, x => x.Second))];
        }

        private static double Num(Dictionary<string, string> r, string key) =>
            double.Parse(r[key], CultureInfo.InvariantCulture);

        private static double HollowArea(double d, double di) =>
            Math.PI / 4.0 * (d * d - di * di);

        private static double HollowInertia(double d, double di) =>
            Math.PI / 64.0 * (Math.Pow(d, 4) - Math.Pow(di, 4));

        // ── 既存ライブラリへの合流 ──────────────────────────────────

        [TestMethod]
        public void Library_IsAppendedToThePrcProductList()
        {
            // 本体部標準性能表 (p5〜p7) の全データ行
            Assert.AreEqual(218, _dam.Count);
            Assert.AreEqual(218, _raw.Count);

            var options = PileSection.PRCOption.ToList();
            Assert.AreEqual(_all.Count, options.Count);
            foreach (var p in _dam.Take(20))
                CollectionAssert.Contains(options, p.Name, $"{p.Name} が製品一覧に出ていない");
        }

        [TestMethod]
        public void ProductNames_DoNotCollideWithTheJisLibrary()
        {
            var damNames = _dam.Select(p => p.Name).ToList();
            Assert.AreEqual(damNames.Count, damNames.Distinct().Count(), "DAM105 内で製品名が重複");

            var jisNames = _all.Where(p => !p.Name.StartsWith(Prefix, StringComparison.Ordinal))
                               .Select(p => p.Name).ToHashSet();
            var clash = damNames.Where(jisNames.Contains).ToList();
            Assert.AreEqual(0, clash.Count, $"既存 PRC 製品と名前が衝突: {string.Join(", ", clash.Take(3))}");
        }

        [TestMethod]
        public void CoverageExtendsBeyondTheJisLibrary()
        {
            // 既存は φ300〜φ1000 / 主筋 D13〜D29 / 肉厚仕様なし
            var dia = _dam.Select(p => p.PileDiameter).Distinct().ToList();
            foreach (double d in new[] { 1100.0, 1200.0, 1300.0, 1400.0, 1500.0 })
                CollectionAssert.Contains(dia, d, $"φ{d} が無い");

            var bars = _dam.Select(p => p.RDesignation).Distinct().ToList();
            CollectionAssert.Contains(bars, "D32");
            CollectionAssert.Contains(bars, "D35");

            CollectionAssert.AreEquivalent(new[] { "標準型", "厚型", "特厚型" },
                _dam.Select(p => p.ThicknessType).Distinct().ToList());
        }

        // ── 断面諸元の検算 ──────────────────────────────────────────

        [TestMethod]
        public void SectionArea_MatchesHollowCircleFormula()
        {
            foreach (var r in _raw)
            {
                double d = Num(r, "D"), t = Num(r, "t");
                double calc = HollowArea(d, d - 2 * t);
                Assert.AreEqual(calc, Num(r, "CatalogAo"), 50.0 + calc * 1e-4, $"{r["typ"]}: Ao");
            }
        }

        [TestMethod]
        public void TransformedArea_UsesASingleRatioForBothSteels()
        {
            // PC鋼棒も異形鉄筋も E = 200,000 なので換算比は n = 5.0 の 1 本で済む
            // (JP-NPRC は異形棒鋼が 205,000 で 2 本立てだった)。
            // Ae にはメーカー側の中間値の丸めが乗るので許容を少し広げる (最大 1.8cm²)。
            foreach (var r in _raw)
            {
                double d = Num(r, "D"), t = Num(r, "t");
                double n = Num(r, "Ep") / Num(r, "Ec");
                Assert.AreEqual(200000.0, Num(r, "Er"), 1e-9, $"{r["typ"]}: 異形鉄筋 ヤング係数");
                double calc = HollowArea(d, d - 2 * t) + (n - 1) * (Num(r, "ap") + Num(r, "ag"));
                Assert.AreEqual(calc, Num(r, "CatalogAe"), 200.0 + calc * 1e-4, $"{r["typ"]}: Ae");
            }
        }

        [TestMethod]
        public void RebarArea_MatchesCountTimesNominalArea()
        {
            var nominal = new Dictionary<string, double>
            {
                ["D13"] = 126.7, ["D16"] = 198.6, ["D19"] = 286.5, ["D22"] = 387.1,
                ["D25"] = 506.7, ["D29"] = 642.4, ["D32"] = 794.2, ["D35"] = 956.6,
            };
            foreach (var r in _raw)
            {
                Assert.IsTrue(nominal.ContainsKey(r["r_designation"]),
                    $"{r["typ"]}: 未知の主筋径 {r["r_designation"]}");
                double expected = Num(r, "nr") * nominal[r["r_designation"]];
                Assert.AreEqual(expected, Num(r, "ag"), expected * 0.01, $"{r["typ"]}: 主筋断面積");
                // 種類は主筋径そのもの
                Assert.AreEqual($"A-{r["r_designation"]}", r["種"], $"{r["typ"]}: 種類と主筋径");
            }
        }

        [TestMethod]
        public void SolvedBarCircles_ReproduceTheCatalogInertia()
        {
            // 逆算した配筋径で Ie を組み直し、カタログ値に戻ることを確かめる。
            // 断面ごとに 2 つの未知数を複数の種類で解いているので、これは恒等ではない。
            double worst = 0.0;
            foreach (var r in _raw)
            {
                double d = Num(r, "D"), t = Num(r, "t");
                double n = Num(r, "Ep") / Num(r, "Ec");
                double calc = HollowInertia(d, d - 2 * t)
                            + (n - 1) * (Num(r, "ap") * Math.Pow(Num(r, "dp") / 2, 2)
                                         + Num(r, "ag") * Math.Pow(Num(r, "dr") / 2, 2)) / 2;
                double ratio = calc / Num(r, "CatalogIe");
                worst = Math.Max(worst, Math.Abs(ratio - 1.0));
                Assert.IsTrue(Math.Abs(ratio - 1.0) < 0.006, $"{r["typ"]}: Ie 比 {ratio:F4}");
            }
            Assert.IsTrue(worst > 0.0, "残差がゼロ = 恒等になっている (モデルを見直すこと)");
        }

        [TestMethod]
        public void SolvedBarCircles_AreOrderedAndInsideTheWall()
        {
            foreach (var r in _raw)
            {
                double d = Num(r, "D"), t = Num(r, "t");
                double dp = Num(r, "dp"), dr = Num(r, "dr");
                Assert.IsTrue(d - 2 * t < dp && dp < d, $"{r["typ"]}: PC鋼棒の配筋径 {dp} が肉厚の外");
                Assert.IsTrue(d - 2 * t < dr && dr < d, $"{r["typ"]}: 主筋の配筋径 {dr} が肉厚の外");
            }

            // 同じ断面では主筋が太いほど配筋円は内側に寄る (かぶり一定のモデル)
            foreach (var g in _raw.GroupBy(r => (r["D"], r["t"])))
            {
                var seq = g.OrderBy(r => double.Parse(r["r_designation"][1..], CultureInfo.InvariantCulture))
                           .Select(r => Num(r, "dr")).ToList();
                for (int i = 1; i < seq.Count; i++)
                    Assert.IsTrue(seq[i] < seq[i - 1],
                        $"φ{g.Key.Item1} t{g.Key.Item2}: 主筋が太いのに配筋径が内側へ寄っていない");
            }
        }

        [TestMethod]
        public void CatalogTypo_IsLimitedToOneRebarArea()
        {
            // φ400 厚型 A-D16 の主筋断面積が 13.39cm² と印字されているが、
            // 7-D16 = 13.90cm² が正しい (同じ製品の他の肉厚仕様も 13.90、Ae も 13.90 で計算されている)。
            var noted = _raw.Where(r => r["AsNote"].Length > 0).ToList();
            Assert.AreEqual(1, noted.Count);
            Assert.AreEqual("DAM105-400-厚型-A-D16", noted[0]["typ"]);
            StringAssert.Contains(noted[0]["AsNote"], "誤植");
            Assert.AreEqual(7 * 198.6, Num(noted[0], "ag"), 1.0);
        }

        // ── 耐力・諸定数 ────────────────────────────────────────────

        [TestMethod]
        public void CatalogMomentCapacities_AreOrdered()
        {
            foreach (var r in _raw)
            {
                Assert.IsTrue(Num(r, "CatalogMal") < Num(r, "CatalogMcr"), $"{r["typ"]}: Mal < Mcr");
                Assert.IsTrue(Num(r, "CatalogMcr") < Num(r, "CatalogMas"), $"{r["typ"]}: Mcr < Mas");
                Assert.IsTrue(Num(r, "CatalogMas") < Num(r, "CatalogMu"), $"{r["typ"]}: Mas < Mu");
            }
        }

        [TestMethod]
        public void ShearValues_AreKeptAsReferenceOnly()
        {
            // カタログ自身が「シアスパン比 a=1.0 の参考値。実際の設計で使う値は別途計算式による」と
            // 注記しているので、設計値としては使わず参照列に置いている。
            foreach (var r in _raw.Take(5))
                StringAssert.Contains(r["RefShearNote"], "参考値");

            // 既存ローダーが読む 28 列には、せん断の参考値が紛れ込んでいないこと
            var head = File.ReadAllLines(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Models", "PileLibrary",
                "pile_library_PRC_DAM105.csv"))[0].TrimStart('﻿').Split(',');
            foreach (var name in head.Take(28))
                Assert.IsFalse(name.StartsWith("Ref", StringComparison.Ordinal),
                    $"参照列 {name} が既存ローダーの読み取り範囲に入っている");
        }

        [TestMethod]
        public void SelectedProduct_IsResolvedByTheSection()
        {
            foreach (var r in _raw.Where((_, i) => i % 29 == 0))
            {
                var s = new PileSection
                {
                    PileBodyType = PileTypeNames.PrecastConcrete,
                    PileSectionType = PileTypeNames.Prc,
                };
                s.SelectedPrecastPile.Name = r["typ"];
                s.RecalculateSelectedPrecastPile();

                Assert.IsTrue(s.IsSelectedPrecastPileInLibrary(), $"{r["typ"]}: ライブラリ未登録扱い");
                Assert.AreEqual(Num(r, "D"), s.PileDiameter, 1e-9, $"{r["typ"]}: 杭径");
                Assert.AreEqual(Num(r, "t"), s.ConcreteThickness, 1e-9, $"{r["typ"]}: 肉厚");
                Assert.AreEqual(Num(r, "nr"), s.MainBarNum, 1e-9, $"{r["typ"]}: 主筋本数");
                Assert.AreEqual(r["r_designation"], s.MainBarSize, $"{r["typ"]}: 主筋径");
                Assert.AreEqual(Num(r, "dr"), s.MainBarDr, 1e-9, $"{r["typ"]}: 主筋配筋径");
                Assert.AreEqual(Num(r, "dp"), s.TendonDp, 1e-9, $"{r["typ"]}: PC鋼棒配筋径");
            }
        }

        [TestMethod]
        public void SpecTable_WarnsWhenTheSelectedRebarGradeDoesNotMatchTheProduct()
        {
            // 鋼管規格と同じく、製品選択では鉄筋規格は切り替わらない。
            // DAM105 は SD345 なので、既定の SD390 のままだと降伏点が 13% 高く見積もられる。
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = PileTypeNames.Prc,
            };
            s.SelectedPrecastPile.Name = _raw[0]["typ"];
            s.RecalculateSelectedPrecastPile();

            s.MainBarSpec = "SD390";
            s.SetSpecs();
            var ng = s.SelectedPileSectionSpecification.First(x => x.Item == "鉄筋規格");
            StringAssert.Contains(ng.Note ?? "", "規格の選択をご確認ください",
                "規格が食い違っているのに警告が出ていない");

            s.MainBarSpec = "SD345";
            s.SetSpecs();
            var ok = s.SelectedPileSectionSpecification.First(x => x.Item == "鉄筋規格");
            Assert.AreEqual("", ok.Note ?? "", "規格が一致しているのに警告が出ている");
        }

        [TestMethod]
        public void DesignConstants_MatchCatalogTable()
        {
            // p2「1.材料強度」「2.許容応力度」
            foreach (var p in _dam)
            {
                Assert.AreEqual(105.0, p.Fc, 1e-9, $"{p.Name}: Fc");
                Assert.AreEqual(60.0, p.SFc, 1e-9, $"{p.Name}: 短期許容圧縮");
                Assert.AreEqual(40000.0, p.Ec, 1e-9, $"{p.Name}: Ec");
                Assert.AreEqual(1275.0, p.Ftp, 1e-9, $"{p.Name}: PC鋼棒 耐力");
                Assert.AreEqual(1420.0, p.SigmaPu, 1e-9, $"{p.Name}: PC鋼棒 引張強さ");
                Assert.AreEqual(200000.0, p.Ep, 1e-9, $"{p.Name}: PC鋼棒 ヤング係数");
                Assert.AreEqual(345.0, p.Ftr, 1e-9, $"{p.Name}: 異形鉄筋 SD345");
                Assert.AreEqual(200000.0, p.Er, 1e-9, $"{p.Name}: 異形鉄筋 ヤング係数");
                Assert.IsTrue(p.HasReinf, $"{p.Name}: 主筋を持つ扱いになっていない");
                // PRC杭 なので鋼管は無い
                Assert.AreEqual(0.0, p.Ts, 1e-9, $"{p.Name}: 鋼管厚");
            }
        }
    }
}
