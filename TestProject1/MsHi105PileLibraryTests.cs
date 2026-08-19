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
    /// 三谷セキサン MS-hi105 パイル (Fc=105 のストレート PHC 杭) の検証。
    ///
    /// ストレート杭は断面の挙動が既存の PHC杭 と完全に同じなので、断面タイプは増やさず
    /// <b>PHC杭 の製品一覧に連結</b>している。したがってここで固定すべきは
    /// 「JIS 汎用ライブラリと衝突せずに並ぶこと」と「転記した断面諸元が理論式と合うこと」。
    ///
    /// このカタログも PCD を印字していないので Ie から逆算しており、
    /// 逆算値が径ごとに一意で 5mm 刻みに乗ることがその裏付けになっている。
    /// </summary>
    [TestClass]
    public class MsHi105PileLibraryTests
    {
        private const double RelTol = 0.0015; // カタログの丸め (有効数字 3〜4 桁) を吸収
        private const string Prefix = "MS-hi105-";

        private static List<PrecastPile> _all = [];
        private static List<PrecastPile> _msHi = [];
        private static List<Dictionary<string, string>> _raw = [];

        [ClassInitialize]
        public static void Init(TestContext _)
        {
            _all = PileSection.PHCs;
            _msHi = [.. _all.Where(p => p.Name.StartsWith(Prefix, StringComparison.Ordinal))];
            _raw = ReadRawCsv();
        }

        /// <summary>カタログ記載の耐力列 (既存ローダーは列番号で読むため触らない列) を直接読む。</summary>
        private static List<Dictionary<string, string>> ReadRawCsv()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                       "Models", "PileLibrary", "pile_library_PHC_MSHI105.csv");
            var lines = File.ReadAllLines(path);
            var head = lines[0].TrimStart('﻿').Split(',');
            return [.. lines.Skip(1).Where(l => l.Length > 0)
                            .Select(l => head.Zip(l.Split(','))
                                             .ToDictionary(x => x.First, x => x.Second))];
        }

        private static string Id(PrecastPile p) => p.Name;

        private static double HollowArea(double d, double t) =>
            Math.PI / 4.0 * (d * d - (d - 2 * t) * (d - 2 * t));

        private static double HollowInertia(double d, double t) =>
            Math.PI / 64.0 * (Math.Pow(d, 4) - Math.Pow(d - 2 * t, 4));

        // ── 既存ライブラリへの合流 ──────────────────────────────────

        [TestMethod]
        public void Library_IsAppendedToThePhcProductList()
        {
            // 本体部標準性能表 = 径 12 種 × (肉厚仕様 × 種類) の全 94 行
            Assert.AreEqual(94, _msHi.Count);
            Assert.AreEqual(94, _raw.Count);

            // 断面タイプは増やさない。PHC杭 の選択肢にそのまま出る
            var options = PileSection.PHCOption.ToList();
            Assert.AreEqual(_all.Count, options.Count);
            foreach (var p in _msHi)
                CollectionAssert.Contains(options, p.Name, $"{Id(p)} が製品一覧に出ていない");
        }

        [TestMethod]
        public void ProductNames_DoNotCollideWithTheJisLibrary()
        {
            var names = _all.Select(p => p.Name).ToList();
            var dup = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.AreEqual(0, dup.Count, $"製品名が重複: {string.Join(", ", dup.Take(3))}");
        }

        [TestMethod]
        public void EverySelectedProduct_IsResolvedByTheSection()
        {
            // ライブラリ存在検証とファイル読込の互換 (名前から諸元が引けること)
            foreach (var p in _msHi)
            {
                var s = new PileSection
                {
                    PileBodyType = PileTypeNames.PrecastConcrete,
                    PileSectionType = PileTypeNames.Phc,
                };
                s.SelectedPrecastPile.Name = p.Name;
                s.RecalculateSelectedPrecastPile();

                Assert.IsTrue(s.IsSelectedPrecastPileInLibrary(), $"{Id(p)}: ライブラリ未登録扱い");
                Assert.AreEqual(p.PileDiameter, s.PileDiameter, 1e-9, $"{Id(p)}: 杭径");
                Assert.AreEqual(p.PileThickness, s.ConcreteThickness, 1e-9, $"{Id(p)}: 肉厚");
                Assert.AreEqual(p.SigmaE, s.Prestress, 1e-9, $"{Id(p)}: σce");
                Assert.AreEqual(p.Dp, s.TendonDp, 1e-9, $"{Id(p)}: PCD");
            }
        }

        // ── 断面諸元の検算 ──────────────────────────────────────────

        [TestMethod]
        public void SectionArea_MatchesHollowCircleFormula()
        {
            foreach (var r in _raw)
            {
                double d = Num(r, "D"), t = Num(r, "t");
                double calc = HollowArea(d, t);
                Assert.AreEqual(calc, Num(r, "CatalogAo"), calc * RelTol, $"{r["typ"]}: Ao");
            }
        }

        [TestMethod]
        public void TransformedArea_MatchesTendonContribution()
        {
            foreach (var r in _raw)
            {
                double d = Num(r, "D"), t = Num(r, "t"), n = Num(r, "Ep") / Num(r, "Ec");
                double calc = HollowArea(d, t) + (n - 1) * Num(r, "ap");
                Assert.AreEqual(calc, Num(r, "CatalogAe"), calc * RelTol, $"{r["typ"]}: Ae");
            }
        }

        [TestMethod]
        public void TransformedInertia_IsConsistentWithTheSolvedPcd()
        {
            // PCD は Ie から逆算しているのでこの一致自体は恒等。
            // 逆算値が径ごとに 1 つに定まることが裏付け (下のテスト)。
            foreach (var r in _raw)
            {
                double d = Num(r, "D"), t = Num(r, "t"), n = Num(r, "Ep") / Num(r, "Ec");
                double calc = HollowInertia(d, t)
                            + (n - 1) * Num(r, "ap") * Math.Pow(Num(r, "dp") / 2, 2) / 2;
                Assert.AreEqual(calc, Num(r, "CatalogIe"), calc * RelTol, $"{r["typ"]}: Ie");
            }
        }

        [TestMethod]
        public void SolvedPcd_IsUniquePerDiameterAndLandsOnAFiveMillimetreGrid()
        {
            foreach (var g in _msHi.GroupBy(p => p.PileDiameter))
            {
                var values = g.Select(p => p.Dp).Distinct().ToList();
                Assert.AreEqual(1, values.Count,
                    $"φ{g.Key}: PCD が 1 つに定まらない ({string.Join(", ", values)})");
                Assert.AreEqual(0.0, values[0] % 5.0, 1e-9, $"φ{g.Key}: PCD {values[0]} が 5mm 刻みでない");
                Assert.IsTrue(values[0] < g.Key, $"φ{g.Key}: PCD {values[0]} が杭径以上");
            }
        }

        // ── 種類と肉厚仕様 ──────────────────────────────────────────

        [TestMethod]
        public void Prestress_NeverExceedsTheJisGradeValueAndDropsOnlyForTheThickSpec()
        {
            // 種類 A/B/C は JIS A 5373 の σce = 4 / 8 / 10 N/mm²。
            // 標準型・特厚型はちょうど規定値、厚型だけが下がる
            // (標準型と同じ PC 鋼棒のまま肉厚が増え、換算断面積が大きくなるため)。
            var spec = new Dictionary<string, double>
            {
                ["A"] = 4.0, ["B"] = 8.0, ["B2"] = 8.0, ["C"] = 10.0, ["C2"] = 10.0,
            };
            foreach (var r in _raw)
            {
                double v = Num(r, "sigma_e") / spec[r["種"]];
                Assert.IsTrue(v is >= 0.90 and <= 1.005,
                    $"{r["typ"]}: σce {r["sigma_e"]} が種別 {r["種"]} の規定値と釣り合わない (比 {v:F3})");
                if (r["標準特厚"] != "厚型")
                    Assert.IsTrue(v >= 0.995, $"{r["typ"]}: 厚型以外なのに σce が規定値を下回る");
            }
            Assert.IsTrue(_raw.Any(r => r["標準特厚"] == "厚型" && Num(r, "sigma_e") / spec[r["種"]] < 0.99),
                "厚型で σce が下がる行が 1 つも無い (前提が変わったらこのテストごと見直すこと)");
        }

        [TestMethod]
        public void ThicknessSpecs_AreOrderedWithinEachDiameterAndKind()
        {
            foreach (var g in _raw.GroupBy(r => (r["D"], r["種"])))
            {
                var order = new Dictionary<string, int> { ["標準型"] = 0, ["厚型"] = 1, ["特厚型"] = 2 };
                var sorted = g.OrderBy(r => order[r["標準特厚"]]).ToList();
                for (int i = 1; i < sorted.Count; i++)
                {
                    Assert.IsTrue(Num(sorted[i], "t") > Num(sorted[i - 1], "t"),
                        $"φ{g.Key.Item1} {g.Key.Item2}: 肉厚が厚さ仕様の順に増えていない");
                    Assert.IsTrue(Num(sorted[i], "CatalogAo") > Num(sorted[i - 1], "CatalogAo"),
                        $"φ{g.Key.Item1} {g.Key.Item2}: Ao が厚さ仕様の順に増えていない");
                }
            }
        }

        [TestMethod]
        public void ThickSpec_IsAdditionalComparedToTheJisLibrary()
        {
            // 既存の JIS 汎用ライブラリは 標準/特厚 の 2 段階。MS-hi105 は 厚型 を持つ。
            // 「重複を足しただけ」ではないことの確認。
            Assert.IsTrue(_raw.Any(r => r["標準特厚"] == "厚型"), "厚型が 1 つも無い");
            var jisThickness = _all.Where(p => !p.Name.StartsWith(Prefix, StringComparison.Ordinal))
                                   .Select(p => p.ThicknessType).Distinct().ToList();
            CollectionAssert.DoesNotContain(jisThickness, "厚型");
        }

        // ── 耐力 ───────────────────────────────────────────────────

        [TestMethod]
        public void CatalogCapacities_AreOrdered()
        {
            foreach (var r in _raw)
            {
                Assert.IsTrue(Num(r, "CatalogMal") < Num(r, "CatalogMas"), $"{r["typ"]}: Mal < Mas");
                Assert.IsTrue(Num(r, "CatalogMas") <= Num(r, "CatalogMcr"), $"{r["typ"]}: Mas ≤ Mcr");
                Assert.IsTrue(Num(r, "CatalogMcr") < Num(r, "CatalogMu"), $"{r["typ"]}: Mcr < Mu");
                Assert.IsTrue(Num(r, "CatalogQal") < Num(r, "CatalogQas"), $"{r["typ"]}: Qal < Qas");
                Assert.IsTrue(Num(r, "CatalogQas") < Num(r, "CatalogQcr"), $"{r["typ"]}: Qas < Qcr");
            }
        }

        [TestMethod]
        public void ComputedUltimateMoment_TracksTheCatalogValue()
        {
            // カタログの破壊モーメント Mu (軸力 0 時) と、アプリの N-M 曲線の
            // 軸力 0 における安全限界モーメントを突き合わせる。
            // 断面計算の規準が違うので一致はしないが、実測では全 94 製品が同じ幅に収まる。
            // ここが崩れたら断面諸元の転記か PCD 逆算を疑うこと。
            double lo = double.MaxValue, hi = 0.0;
            foreach (var r in _raw)
            {
                var s = new PileSection
                {
                    PileBodyType = PileTypeNames.PrecastConcrete,
                    PileSectionType = PileTypeNames.Phc,
                };
                s.SelectedPrecastPile.Name = r["typ"];
                s.RecalculateSelectedPrecastPile();

                var (ns, ms) = s.UnfactoredUltimateNM;
                double mAtZero = InterpolateAtZeroAxial(ns, ms);
                double ratio = mAtZero / Num(r, "CatalogMu");
                lo = Math.Min(lo, ratio);
                hi = Math.Max(hi, ratio);
                Assert.IsTrue(ratio is > 0.90 and < 1.20,
                    $"{r["typ"]}: 計算 {mAtZero:F0} / カタログ Mu {r["CatalogMu"]} = {ratio:F2}");
            }
            Assert.IsTrue(hi - lo < 0.25, $"比のばらつきが大きい ({lo:F2}〜{hi:F2})");
        }

        private static double InterpolateAtZeroAxial(List<double> ns, List<double> ms)
        {
            double best = 0.0;
            for (int i = 0; i + 1 < ns.Count; i++)
            {
                if ((ns[i] <= 0 && ns[i + 1] >= 0) || (ns[i] >= 0 && ns[i + 1] <= 0))
                {
                    double t = Math.Abs(ns[i + 1] - ns[i]) < 1e-12
                        ? 0.0
                        : (0.0 - ns[i]) / (ns[i + 1] - ns[i]);
                    best = Math.Max(best, ms[i] + t * (ms[i + 1] - ms[i]));
                }
            }
            return best;
        }

        // ── 諸定数 ─────────────────────────────────────────────────

        [TestMethod]
        public void DesignConstants_MatchCatalogTable()
        {
            // p2「1.材料強度」
            foreach (var p in _msHi)
            {
                Assert.AreEqual(105.0, p.Fc, 1e-9, $"{Id(p)}: Fc");
                Assert.AreEqual(60.0, p.SFc, 1e-9, $"{Id(p)}: 短期許容圧縮");
                Assert.AreEqual(p.SigmaE / 2.0, p.Fbc, 1e-6, $"{Id(p)}: 短期許容曲げ引張 = σce/2");
                Assert.AreEqual(40000.0, p.Ec, 1e-9, $"{Id(p)}: Ec");
                Assert.AreEqual(1275.0, p.Ftp, 1e-9, $"{Id(p)}: PC鋼棒 耐力");
                Assert.AreEqual(1420.0, p.SigmaPu, 1e-9, $"{Id(p)}: PC鋼棒 引張強さ");
                Assert.AreEqual(200000.0, p.Ep, 1e-9, $"{Id(p)}: PC鋼棒 ヤング係数");
                // ストレート PHC 杭なので主筋・鋼管は無い
                Assert.IsFalse(p.HasReinf, $"{Id(p)}: 主筋を持つ扱いになっている");
                Assert.AreEqual(0.0, p.Ts, 1e-9, $"{Id(p)}: 鋼管厚");
            }
        }

        private static double Num(Dictionary<string, string> r, string key) =>
            double.Parse(r[key], CultureInfo.InvariantCulture);
    }
}
