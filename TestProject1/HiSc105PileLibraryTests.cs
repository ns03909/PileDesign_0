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
    /// 三谷セキサン Hi-SC105 パイル (Fc=105 の外殻鋼管付コンクリート杭) の検証。
    ///
    /// ストレートな SC 杭なので断面タイプは増やさず <b>SC杭 の製品一覧に連結</b>している。
    ///
    /// このカタログは<b>腐食代 0mm と 1mm の両方</b>の表を持つ。製品としては 0mm 側を取り込み、
    /// 1mm 側は CSV の <c>Corr1*</c> 列に併記して<b>本プログラムの腐食モデルの外部検証データ</b>
    /// として使う (これまでのカタログには無かった検証手段)。
    /// </summary>
    [TestClass]
    public class HiSc105PileLibraryTests
    {
        private const string Prefix = "Hi-SC105-";

        private static List<PrecastPile> _all = [];
        private static List<PrecastPile> _hiSc = [];
        private static List<Dictionary<string, string>> _raw = [];

        [ClassInitialize]
        public static void Init(TestContext _)
        {
            _all = PileSection.SCs;
            _hiSc = [.. _all.Where(p => p.Name.StartsWith(Prefix, StringComparison.Ordinal))];
            _raw = ReadRawCsv();
        }

        private static List<Dictionary<string, string>> ReadRawCsv()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                       "Models", "PileLibrary", "pile_library_SC_HISC105.csv");
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

        private static PileSection MakeSection(string productName, double corrosion = 0.0)
        {
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = PileTypeNames.Sc,
                PipeGrade = "SKK490",
            };
            s.SelectedPrecastPile.Name = productName;
            s.RecalculateSelectedPrecastPile();
            s.CorrosionDepth = corrosion;
            return s;
        }

        // ── 既存ライブラリへの合流 ──────────────────────────────────

        [TestMethod]
        public void Library_IsAppendedToTheScProductList()
        {
            // 標準性能表 (腐食代0mm) の全行。鋼管厚が 1mm 刻みなので行数が多い
            Assert.AreEqual(579, _hiSc.Count);
            Assert.AreEqual(579, _raw.Count);

            var options = PileSection.SCOption.ToList();
            Assert.AreEqual(_all.Count, options.Count);
            foreach (var p in _hiSc.Take(20))
                CollectionAssert.Contains(options, p.Name, $"{p.Name} が製品一覧に出ていない");
        }

        [TestMethod]
        public void ProductNames_DoNotCollideWithTheJisLibrary()
        {
            // 追加分どうし、および追加分と既存分の間で名前が衝突しないこと
            var hiScNames = _hiSc.Select(p => p.Name).ToList();
            Assert.AreEqual(hiScNames.Count, hiScNames.Distinct().Count(), "Hi-SC105 内で製品名が重複");

            var jisNames = _all.Where(p => !p.Name.StartsWith(Prefix, StringComparison.Ordinal))
                               .Select(p => p.Name).ToHashSet();
            var clash = hiScNames.Where(jisNames.Contains).ToList();
            Assert.AreEqual(0, clash.Count, $"既存 SC 製品と名前が衝突: {string.Join(", ", clash.Take(3))}");
        }

        [TestMethod]
        public void PreExistingDuplicatesInTheJisLibrary_AreUnchanged()
        {
            // 既存の pile_library_SC.csv には元から完全重複行が 5 組ある
            // (No. だけ違い、D/t/Fc/ts も名前も同一)。今回の追加とは無関係だが、
            // 増えていないことをここで見張っておく。整理する場合は本テストも更新すること。
            var dup = _all.Where(p => !p.Name.StartsWith(Prefix, StringComparison.Ordinal))
                          .GroupBy(p => p.Name).Where(g => g.Count() > 1)
                          .Select(g => g.Key).OrderBy(n => n).ToList();
            CollectionAssert.AreEqual(
                new[] { "SC-500-標準-105-14", "SC-500-標準-105-16",
                        "SC-600-標準-105-14", "SC-600-標準-105-16", "SC-600-標準-105-19" },
                dup);
        }

        [TestMethod]
        public void SteelThicknessIsTabulatedEveryMillimetre()
        {
            // 既存の JIS 汎用ライブラリは代表値のみ (4.5/6/9/12/...)。ここが追加分の価値。
            var ts = _hiSc.Select(p => p.Ts).Distinct().OrderBy(v => v).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(6, 20).Select(v => (double)v).ToList(), ts);

            var jisTs = _all.Where(p => !p.Name.StartsWith(Prefix, StringComparison.Ordinal))
                            .Select(p => p.Ts).Distinct().ToList();
            CollectionAssert.DoesNotContain(jisTs, 7.0);
        }

        [TestMethod]
        public void DiametersCoverTheLargeSizesTheJisLibraryLacks()
        {
            var dia = _hiSc.Select(p => p.PileDiameter).Distinct().OrderBy(v => v).ToList();
            foreach (double d in new[] { 1300.0, 1400.0, 1500.0 })
                CollectionAssert.Contains(dia, d, $"φ{d} が無い");
        }

        // ── 断面諸元の検算 ──────────────────────────────────────────

        [TestMethod]
        public void SectionAreas_MatchHollowCircleFormulae()
        {
            // 肉厚 t は鋼管 + コンクリートの全肉厚、ts が鋼管厚。鋼管は外殻。
            // カタログは cm² 単位で丸めているので許容は印字単位の半分とする。
            foreach (var r in _raw)
            {
                double d = Num(r, "D"), t = Num(r, "t"), ts = Num(r, "ts");
                double ao = HollowArea(d, d - 2 * t);
                double as_ = HollowArea(d, d - 2 * ts);
                Assert.AreEqual(ao, Num(r, "CatalogAo"), 50.0 + ao * 1e-4, $"{r["typ"]}: Ao");
                Assert.AreEqual(as_, Num(r, "CatalogAs"), 50.0 + as_ * 1e-4, $"{r["typ"]}: As");
                Assert.AreEqual(ao - as_, Num(r, "CatalogAc"), 50.0 + ao * 1e-4, $"{r["typ"]}: Ac");
            }
        }

        [TestMethod]
        public void TransformedArea_UsesTheSteelToConcreteRatio()
        {
            // Ae = Ac + (Es/Ec)·As。これが合うことが Es=205,000 の裏付けになる。
            // 印字の Ac/As は 1cm² 単位に丸められているので、断面は厳密式から求める。
            foreach (var r in _raw)
            {
                double d = Num(r, "D"), t = Num(r, "t"), ts = Num(r, "ts");
                double n = Num(r, "Es") / Num(r, "Ec");
                double as_ = HollowArea(d, d - 2 * ts);
                double ac = HollowArea(d, d - 2 * t) - as_;
                double calc = ac + n * as_;
                Assert.AreEqual(calc, Num(r, "CatalogAe"), 50.0 + calc * 1e-4, $"{r["typ"]}: Ae");
            }
        }

        [TestMethod]
        public void GrossAreaEqualsTheSumOfItsParts()
        {
            foreach (var r in _raw)
                Assert.AreEqual(Num(r, "CatalogAc") + Num(r, "CatalogAs"), Num(r, "CatalogAo"), 100.0,
                    $"{r["typ"]}: Ao ≠ Ac + As");
        }

        [TestMethod]
        public void CatalogInertia_IsSlightlyBelowTheExactValue()
        {
            // カタログの Ie は ts が厚いほど厳密値より小さい (最大 0.22%)。
            // 鋼管の断面二次モーメントを薄肉近似で出しているためと見られる。
            // 本プログラムは EI を自前で積算するので取り込みには影響しないが、
            // 「厳密式と少しずれる」という事実をここで固定しておく。
            double worst = 0.0;
            foreach (var r in _raw)
            {
                double d = Num(r, "D"), t = Num(r, "t"), ts = Num(r, "ts");
                double n = Num(r, "Es") / Num(r, "Ec");
                double calc = HollowInertia(d - 2 * ts, d - 2 * t) + n * HollowInertia(d, d - 2 * ts);
                double ratio = calc / Num(r, "CatalogIe");
                worst = Math.Max(worst, ratio);
                Assert.IsTrue(ratio is > 0.999 and < 1.005, $"{r["typ"]}: Ie 比 {ratio:F4}");
            }
            Assert.IsTrue(worst > 1.001, "系統的なずれが消えている (前提が変わったら本テストを見直すこと)");
        }

        // ── 腐食モデルの外部検証 (このカタログ固有の収穫) ───────────

        [TestMethod]
        public void CorrodedSteelArea_MatchesTheCatalogOneMillimetreTable()
        {
            // 本プログラムは腐食を CorrosionDepth パラメータで扱う。
            // カタログの腐食代1mm 表と突き合わせて、腐食モデルが外部データと一致することを確かめる。
            int checkedRows = 0;
            foreach (var r in _raw.Where(r => r["Corr1As"].Length > 0))
            {
                var s = MakeSection(r["typ"], corrosion: 1.0);
                Assert.AreEqual(Num(r, "Corr1As"), s.PipeAsCorroded, 50.0 + Num(r, "Corr1As") * 1e-4,
                    $"{r["typ"]}: 腐食後の鋼管断面積");
                checkedRows++;
            }
            Assert.AreEqual(579, checkedRows, "腐食代1mm の対応が取れていない行がある");
        }

        [TestMethod]
        public void CorrodedFlexuralStiffness_TracksTheCatalogOneMillimetreTable()
        {
            // EI は Ec·Ic + Es·Is なので、換算断面二次モーメント Ie とは EI = Ec·Ie の関係にある。
            // 腐食後についてこれが成り立つかをカタログ 1mm 表で確認する。
            // カタログ Ie 自体が厳密式より最大 0.22% 小さいので、その分の幅を見込む。
            foreach (var r in _raw.Where(r => r["Corr1Ie"].Length > 0).Take(120))
            {
                var s = MakeSection(r["typ"], corrosion: 1.0);
                double ieFromEi = s.EICorroded * 1e9 / Num(r, "Ec");   // kN·m² -> N·mm² -> mm⁴
                double ratio = ieFromEi / Num(r, "Corr1Ie");
                Assert.IsTrue(ratio is > 0.995 and < 1.01,
                    $"{r["typ"]}: EI から戻した Ie / カタログ = {ratio:F4}");
            }
        }

        [TestMethod]
        public void CatalogTypo_IsLimitedToTheCorrodedGrossAreaOfPhi1200()
        {
            // 腐食代1mm 表の φ1200 特厚型 は Ao の印字が Ac+As と 80cm² 食い違う (縦結合のため 17 行)。
            // 取り込み時に Ac+As へ置き換え、Corr1Note に理由を残している。
            var noted = _raw.Where(r => r["Corr1Note"].Length > 0).ToList();
            Assert.AreEqual(17, noted.Count);
            foreach (var r in noted)
            {
                Assert.AreEqual(1200.0, Num(r, "D"), 1e-9);
                Assert.AreEqual("特厚型", r["標準特厚"]);
                StringAssert.Contains(r["Corr1Note"], "誤植");
                Assert.AreEqual(Num(r, "Corr1Ac") + Num(r, "Corr1As"), Num(r, "Corr1Ao"), 100.0);
            }
        }

        // ── 断面への転記と諸元表 ────────────────────────────────────

        [TestMethod]
        public void SelectedProduct_IsResolvedByTheSection()
        {
            foreach (var r in _raw.Where((_, i) => i % 37 == 0))
            {
                var s = MakeSection(r["typ"]);
                Assert.IsTrue(s.IsSelectedPrecastPileInLibrary(), $"{r["typ"]}: ライブラリ未登録扱い");
                Assert.AreEqual(Num(r, "D"), s.PileDiameter, 1e-9, $"{r["typ"]}: 杭径");
                Assert.AreEqual(Num(r, "ts"), s.PipeTs, 1e-9, $"{r["typ"]}: 鋼管厚");
                // SC杭 の t は全肉厚。コンクリート厚は t − ts。
                Assert.AreEqual(Num(r, "t") - Num(r, "ts"), s.ConcreteThickness, 1e-9,
                    $"{r["typ"]}: コンクリート肉厚");
            }
        }

        [TestMethod]
        public void SpecTable_WarnsWhenTheSelectedSteelGradeDoesNotMatchTheProduct()
        {
            // 製品選択では鋼管規格は切り替わらない。SKK400 のままだと降伏点が食い違うので
            // 諸元表で知らせる (Hi-SC105 は SKK490 / STK490 = 325 N/mm²)。
            var ok = MakeSection(_raw[0]["typ"]);
            ok.SetSpecs();
            var okRow = ok.SelectedPileSectionSpecification.First(x => x.Item == "鋼管規格");
            Assert.AreEqual("", okRow.Note ?? "", "規格が一致しているのに警告が出ている");

            var ng = MakeSection(_raw[0]["typ"]);
            ng.PipeGrade = "SKK400";
            ng.SetSpecs();
            var ngRow = ng.SelectedPileSectionSpecification.First(x => x.Item == "鋼管規格");
            StringAssert.Contains(ngRow.Note ?? "", "規格の選択をご確認ください",
                "規格が食い違っているのに警告が出ていない");
        }

        [TestMethod]
        public void DesignConstants_MatchCatalogTable()
        {
            // p2「■設計に用いる数値／Hi-SC105」
            foreach (var p in _hiSc)
            {
                Assert.AreEqual(105.0, p.Fc, 1e-9, $"{p.Name}: Fc");
                Assert.AreEqual(60.0, p.SFc, 1e-9, $"{p.Name}: 短期許容圧縮");
                Assert.AreEqual(40000.0, p.Ec, 1e-9, $"{p.Name}: Ec");
                Assert.AreEqual(325.0, p.Fts, 1e-9, $"{p.Name}: 鋼管 降伏点");
                Assert.AreEqual(205000.0, p.Es, 1e-9, $"{p.Name}: 鋼管 ヤング係数");
                // SC杭 なので PC 鋼材・主筋は持たない
                Assert.AreEqual(0.0, p.Ap, 1e-9, $"{p.Name}: PC鋼材");
                Assert.IsFalse(p.HasReinf, $"{p.Name}: 主筋");
            }
        }
    }
}
