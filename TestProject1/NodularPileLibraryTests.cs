using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 節杭製品ライブラリ (ジャパンパイル JP-NPH85/105/123) の検証。
    ///
    /// カタログ (nph.pdf) の断面諸数値は軸部の中空円形断面から一意に計算できるため、
    /// 転記した全行を計算値と突合できる。カタログは有効数字 3〜4 桁に丸められているので
    /// 相対 0.15% を許容する (桁の転記ミスは 1% 以上ずれるため確実に検出される)。
    /// </summary>
    [TestClass]
    public class NodularPileLibraryTests
    {
        private const double RelTol = 0.0015; // カタログの丸め (有効数字 3〜4 桁) を吸収
        private const double Y0 = 0.0;        // 未使用 (可読性のためのプレースホルダではない)

        private static List<NodularPile> _piles = [];
        private static List<NodularPileHead> _heads = [];

        [ClassInitialize]
        public static void Init(TestContext _)
        {
            _piles = NodularPileLoader.LoadDefault();
            _heads = NodularPileLoader.LoadDefaultHeads();
        }

        // ── 読み込み ────────────────────────────────────────────────

        [TestMethod]
        public void Library_LoadsExpectedRowCount()
        {
            Assert.AreEqual(292, _piles.Count, "JP-NPH カタログの断面諸数値一覧表は 292 行");
            Assert.AreEqual(25, _heads.Count, "拡頭中間径/拡頭タイプの形状一覧は 25 行");
        }

        [TestMethod]
        public void Library_CoversAllSeventeenDesignations()
        {
            var names = _piles.Select(p => p.Name).Distinct().ToList();
            Assert.AreEqual(17, names.Count, $"呼び名は 17 種: {string.Join(", ", names)}");
            foreach (var expected in new[] { "440-300", "450-300", "1200-1100" })
                CollectionAssert.Contains(names, expected);
        }

        [TestMethod]
        public void Library_HasThreeConcreteStrengths()
        {
            var fcs = _piles.Select(p => p.Fc).Distinct().OrderBy(x => x).ToList();
            CollectionAssert.AreEqual(new[] { 85.0, 105.0, 123.0 }, fcs);
        }

        [TestMethod]
        public void Library_NameEncodesNodeAndShaftDiameter()
        {
            // 呼び名 'Do-D' は節部径と軸部径そのもの
            foreach (var p in _piles)
            {
                var parts = p.Name.Split('-');
                Assert.AreEqual(2, parts.Length, $"呼び名の形式: {p.Name}");
                Assert.AreEqual(double.Parse(parts[0]), p.Do, 1e-9, $"{p.Name} の節部径");
                Assert.AreEqual(double.Parse(parts[1]), p.D, 1e-9, $"{p.Name} の軸部径");
                Assert.IsTrue(p.Do > p.D, $"{p.Name}: 節部径は軸部径より大きい");
            }
        }

        // ── 断面諸数値の検算 (カタログ vs 計算) ─────────────────────

        [TestMethod]
        public void SectionArea_MatchesHollowCircleFormula()
        {
            // Ao = π/4 (D² − di²)、di = D − 2t （断面性能は軸部基準）
            AssertAllClose(p => Math.PI / 4.0 * (p.D * p.D - p.InnerDiameter * p.InnerDiameter),
                           p => p.Ao, "Ao");
        }

        [TestMethod]
        public void SecondMomentOfArea_MatchesHollowCircleFormula()
        {
            // Io = π/64 (D⁴ − di⁴)
            AssertAllClose(p => Math.PI / 64.0 * (Math.Pow(p.D, 4) - Math.Pow(p.InnerDiameter, 4)),
                           p => p.Io, "Io");
        }

        [TestMethod]
        public void TransformedArea_MatchesPcBarContribution()
        {
            // Ae = Ao + (n − 1) Ap、n = Ep/Ec
            AssertAllClose(p => p.Ao_Calc() + (p.Ep / p.Ec - 1.0) * p.Ap, p => p.Ae, "Ae");
        }

        [TestMethod]
        public void TransformedSecondMoment_MatchesPcBarRingContribution()
        {
            // Ie = Io + (n − 1) Ap (PCD/2)² / 2
            // 円周上に均等配置された鋼材群の直径軸まわり 2 次モーメントは Σ A r² / 2
            AssertAllClose(p => p.Io_Calc() + (p.Ep / p.Ec - 1.0) * p.Ap * Math.Pow(p.Pcd / 2.0, 2) / 2.0,
                           p => p.Ie, "Ie");
        }

        [TestMethod]
        public void SectionModulus_IsConsistentWithTransformedSecondMoment()
        {
            // Ze = Ie / (D/2)。カタログ印字に誤植のある行 (Note 付き) は除外し、
            // 代わりに ZeFromIe が正しく算出されていることを確認する。
            int noted = 0;
            foreach (var p in _piles)
            {
                double expected = p.Ie / (p.D / 2.0);
                Assert.AreEqual(expected, p.ZeFromIe, expected * RelTol,
                    $"{p.Name} Fc{p.Fc} {p.PrestressType}: ZeFromIe");

                if (!string.IsNullOrEmpty(p.Note)) { noted++; continue; }
                Assert.AreEqual(expected, p.Ze, expected * RelTol,
                    $"{p.Name} Fc{p.Fc} {p.PrestressType}: カタログ Ze");
            }
            Assert.AreEqual(2, noted,
                "カタログ Ze の誤植は φ700-600 / φ800-600 の Fc123 AH 種の 2 行のみ");
        }

        [TestMethod]
        public void CatalogTypo_IsLimitedToZeOfFc123AhType()
        {
            // 既知のカタログ誤植を明示的に固定しておく (将来カタログ改訂時に気付けるように)
            var noted = _piles.Where(p => !string.IsNullOrEmpty(p.Note)).ToList();
            Assert.AreEqual(2, noted.Count);
            foreach (var p in noted)
            {
                Assert.AreEqual(123.0, p.Fc);
                Assert.AreEqual("AH", p.PrestressType);
                CollectionAssert.Contains(new[] { "700-600", "800-600" }, p.Name);
                // 印字値 16 430×10³ に対し正しくは Ie/(D/2) = 18 113×10³
                Assert.AreEqual(16_430_000.0, p.Ze, 1.0);
                Assert.IsTrue(p.ZeFromIe > 18_000_000.0);
            }
        }

        // ── 断面性能の整合 ──────────────────────────────────────────

        [TestMethod]
        public void EffectivePrestress_SpecAndCalcAgreeWithinFivePercent()
        {
            // カタログ注記: 計算値は「定めた値 (規定値)」の ±5% の範囲とする
            foreach (var p in _piles.Where(p => p.SigmaCeSpec > 0))
            {
                double rel = Math.Abs(p.SigmaCeCalc - p.SigmaCeSpec) / p.SigmaCeSpec;
                Assert.IsTrue(rel <= 0.05 + 1e-9,
                    $"{p.Name} Fc{p.Fc} {p.PrestressType}: σce 規定 {p.SigmaCeSpec} vs 計算 {p.SigmaCeCalc}");
            }
        }

        [TestMethod]
        public void MomentCapacities_AreOrdered()
        {
            // 長期許容 < 短期許容 ≤ ひび割れ < 終局
            foreach (var p in _piles)
            {
                string id = $"{p.Name} Fc{p.Fc} {p.PrestressType}";
                Assert.IsTrue(p.Mal < p.Mas, $"{id}: Mal < Mas");
                Assert.IsTrue(p.Mas <= p.Mc, $"{id}: Mas ≤ Mc");
                Assert.IsTrue(p.Mc < p.Mu, $"{id}: Mc < Mu");
            }
        }

        [TestMethod]
        public void ShearCapacities_AreOrdered()
        {
            foreach (var p in _piles)
            {
                string id = $"{p.Name} Fc{p.Fc} {p.PrestressType}";
                Assert.IsTrue(p.Qal < p.Qas, $"{id}: Qal < Qas");
                Assert.IsTrue(p.Qas < p.Qu, $"{id}: Qas < Qu");
            }
        }

        [TestMethod]
        public void AllowableAxialForce_IsBelowGrossConcreteCapacity()
        {
            // 長期許容軸力は「長期許容圧縮応力度 × 換算断面積」を超えない
            foreach (var p in _piles)
            {
                double cap = p.FcAllowCompLong * p.Ae / 1000.0; // kN
                Assert.IsTrue(p.Nal <= cap * 1.02 + 1e-6,
                    $"{p.Name} Fc{p.Fc} {p.PrestressType}: N={p.Nal}kN > fc·Ae={cap:F0}kN");
            }
        }

        [TestMethod]
        public void DesignConstants_MatchCatalogTable()
        {
            // p1「設計に用いる諸定数」
            var expected = new Dictionary<double, (double ec, double cl, double cs)>
            {
                [85] = (40000, 24, 48),
                [105] = (40000, 30, 60),
                [123] = (42000, 35, 70),
            };
            foreach (var p in _piles)
            {
                var (ec, cl, cs) = expected[p.Fc];
                Assert.AreEqual(ec, p.Ec, 1e-9, $"Fc{p.Fc} の Ec");
                Assert.AreEqual(cl, p.FcAllowCompLong, 1e-9, $"Fc{p.Fc} の長期許容圧縮");
                Assert.AreEqual(cs, p.FcAllowCompShort, 1e-9, $"Fc{p.Fc} の短期許容圧縮");
                Assert.AreEqual(1.2, p.FcAllowDiagLong, 1e-9);
                Assert.AreEqual(1.8, p.FcAllowDiagShort, 1e-9);
                Assert.AreEqual(1275.0, p.Ftp, 1e-9, "PC 鋼棒 耐力");
                Assert.AreEqual(1420.0, p.SigmaPu, 1e-9, "PC 鋼棒 引張強さ");
                Assert.AreEqual(200000.0, p.Ep, 1e-9, "PC 鋼棒 ヤング係数");
            }
        }

        // ── 質量・形状 ──────────────────────────────────────────────

        [TestMethod]
        public void MassPerMetre_IsPresentAndPlausible()
        {
            foreach (var p in _piles)
            {
                Assert.IsTrue(p.MassPerM > 0, $"{p.Name} {p.ThicknessType}: 質量が未設定");
                // 軸部コンクリート体積 × 2.5 t/m³ を下限、節の分を見込んで 1.6 倍を上限とする
                double shaft = p.Ao * 1e-6 * 2.5; // t/m
                Assert.IsTrue(p.MassPerM > shaft * 0.95 && p.MassPerM < shaft * 1.6,
                    $"{p.Name} {p.ThicknessType}: 質量 {p.MassPerM} t/m が軸部体積 {shaft:F3} t/m と不整合");
            }
        }

        [TestMethod]
        public void ElevationDimensions_AreCatalogValues()
        {
            foreach (var p in _piles)
            {
                Assert.AreEqual(1000.0, p.NodePitch, 1e-9, "節ピッチ");
                Assert.AreEqual(600.0, p.HeadOffset, 1e-9, "杭頭から第 1 節中心");
                Assert.AreEqual(400.0, p.ToeOffset, 1e-9, "杭先端から最終節中心");
            }
        }

        [TestMethod]
        public void NodeCenterPositions_FollowCatalogLayout()
        {
            var p = _piles.First(x => x.Name == "440-300");
            // 杭長 10m: 杭頭 600mm から 1000mm ピッチ、杭先端 400mm 手前まで
            var zs = p.NodeCenterPositions(10.0).ToList();
            Assert.AreEqual(600.0, zs.First(), 1e-9);
            Assert.AreEqual(9600.0, zs.Last(), 1e-9);
            Assert.AreEqual(10, zs.Count);
            for (int i = 1; i < zs.Count; i++)
                Assert.AreEqual(1000.0, zs[i] - zs[i - 1], 1e-9);
        }

        [TestMethod]
        public void EstimatedNodeShape_IsFortyFiveDegreeTaper()
        {
            // 姿図の実測: テーパーは 45° (軸方向長さ = 半径差)、節部長さも同値
            var p = _piles.First(x => x.Name == "440-300");
            Assert.AreEqual(70.0, p.EstimatedTaperLength, 1e-9, "(440−300)/2");
            Assert.AreEqual(70.0, p.EstimatedNodeFlatLength, 1e-9);
            Assert.AreEqual(210.0, p.EstimatedNodeTotalLength, 1e-9);
            // 節全長は必ず節ピッチ内に収まる
            foreach (var q in _piles)
                Assert.IsTrue(q.EstimatedNodeTotalLength < q.NodePitch,
                    $"{q.Name}: 節全長 {q.EstimatedNodeTotalLength} が節ピッチを超える");
        }

        // ── 拡頭タイプ ──────────────────────────────────────────────

        [TestMethod]
        public void HeadTypes_ReferenceAnExistingDesignation()
        {
            var names = _piles.Select(p => p.Name).ToHashSet();
            foreach (var h in _heads)
            {
                CollectionAssert.Contains(names.ToList(), h.Name, $"拡頭 {h.Name} に対応する呼び名が無い");
                Assert.AreEqual(600.0, h.Lt, 1e-9, "拡頭部長さはカタログ全行で 600mm");
                Assert.IsTrue(h.Dt > h.D, $"{h.Name}: 拡頭径 {h.Dt} が軸部径 {h.D} 以下");
                // 拡頭径は原則 節部径以下 (中間径) または節部径と同値だが、
                // φ440-300(450) / φ450-300(450) だけは Dt=450 > Do=440 となる
                // (450mm が規格の拡頭径として用意されているため)。
                Assert.IsTrue(h.Dt <= h.Do || (h.D == 300.0 && h.Dt == 450.0),
                    $"{h.Name}: 拡頭径 {h.Dt} が節部径 {h.Do} を超えている");
                Assert.IsTrue(h.MassAdd > 0, $"{h.Name}: 質量増分が未設定");
            }
        }

        [TestMethod]
        public void HeadTypes_MatchDesignationDiameters()
        {
            // 拡頭表の Do/D は呼び名と一致する
            foreach (var h in _heads)
            {
                var p = _piles.First(x => x.Name == h.Name);
                Assert.AreEqual(p.Do, h.Do, 1e-9, $"{h.Name} の節部径");
                Assert.AreEqual(p.D, h.D, 1e-9, $"{h.Name} の軸部径");
            }
        }

        [TestMethod]
        public void HeadTypes_IncludeBothIntermediateAndFullHead()
        {
            Assert.IsTrue(_heads.Any(h => h.IsIntermediateHead), "拡頭中間径タイプが存在する");
            Assert.IsTrue(_heads.Any(h => !h.IsIntermediateHead), "拡頭タイプ (Dt = Do) が存在する");
        }

        // ── ヘルパ ─────────────────────────────────────────────────

        private static void AssertAllClose(Func<NodularPile, double> calc,
                                           Func<NodularPile, double> catalog, string label)
        {
            foreach (var p in _piles)
            {
                double c = calc(p);
                Assert.AreEqual(c, catalog(p), Math.Abs(c) * RelTol,
                    $"{p.Name} Fc{p.Fc} {p.PrestressType} {p.ThicknessType}: {label}");
            }
        }
    }

    internal static class NodularPileTestExtensions
    {
        public static double Ao_Calc(this NodularPile p) =>
            Math.PI / 4.0 * (p.D * p.D - p.InnerDiameter * p.InnerDiameter);

        public static double Io_Calc(this NodularPile p) =>
            Math.PI / 64.0 * (Math.Pow(p.D, 4) - Math.Pow(p.InnerDiameter, 4));
    }
}
