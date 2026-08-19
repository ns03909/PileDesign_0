using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 頭部厚型節付き杭 製品ライブラリ (三谷セキサン BF.S105 / BF.S123) の検証。
    ///
    /// この製品はカタログが<b>頭部軸部の断面性能しか載せていない</b>ため、
    /// 取り込み時に 2 つの「カタログに書かれていない量」を補っている。
    /// <list type="number">
    ///   <item>配筋径 PCD — 頭部軸部の Ie から逆算</item>
    ///   <item>先端軸部の断面諸元と σce — 有効プレストレス力が両軸部で共通という前提で算定</item>
    /// </list>
    /// どちらも独立した裏付けがあり、それをここで固定する
    /// (PCD は 5mm 丸めかつ同一呼び名で一致、先端 σce は JIS の A/B/C 種規定値に一致)。
    /// </summary>
    [TestClass]
    public class BfsPileLibraryTests
    {
        private const double RelTol = 0.0015; // カタログの丸め (有効数字 3〜4 桁) を吸収

        private static List<BfsPile> _piles = [];

        [ClassInitialize]
        public static void Init(TestContext _) => _piles = BfsPileLoader.LoadDefault();

        private static string Id(BfsPile p) => $"{p.Name} Fc{p.Fc} {p.PrestressType}";
        private static double N(BfsPile p) => p.Ep / p.Ec;

        private static double HollowArea(double d, double t) =>
            Math.PI / 4.0 * (d * d - (d - 2 * t) * (d - 2 * t));

        private static double HollowInertia(double d, double t) =>
            Math.PI / 64.0 * (Math.Pow(d, 4) - Math.Pow(d - 2 * t, 4));

        // ── 読み込み ────────────────────────────────────────────────

        [TestMethod]
        public void Library_LoadsExpectedRowCount()
        {
            // 標準仕様／標準性能表 = Fc 2 種 × 呼び名 9 種 × 種類 3 種
            Assert.AreEqual(54, _piles.Count);
            CollectionAssert.AreEquivalent(new[] { 105.0, 123.0 },
                _piles.Select(p => p.Fc).Distinct().ToList());
            CollectionAssert.AreEquivalent(new[] { "A2", "B2", "C2" },
                _piles.Select(p => p.PrestressType).Distinct().ToList());
            Assert.AreEqual(9, _piles.Select(p => p.Name).Distinct().Count());
        }

        [TestMethod]
        public void Name_EncodesTheTwoShaftDiametersAndTipThickness()
        {
            // 呼び名 400-3045 = 頭部軸部径 400 / 先端軸部径 300 / 先端肉厚 45…ではなく、
            // 先端肉厚は表の値。呼び名の前半 2 つが径であることだけを固定する。
            foreach (var p in _piles)
            {
                var parts = p.Name.Split('-');
                Assert.AreEqual(2, parts.Length, $"呼び名の形式: {p.Name}");
                Assert.AreEqual(double.Parse(parts[0]), p.HeadDia, 1e-9, $"{p.Name} の頭部軸部径");
                StringAssert.StartsWith(parts[1], $"{p.TipDia / 10:0}", $"{p.Name} の先端軸部径");
                Assert.IsTrue(p.HeadDia > p.TipDia, $"{Id(p)}: 頭部軸部径 > 先端軸部径");
                Assert.IsTrue(p.NodeDia > p.HeadDia, $"{Id(p)}: 節部径 > 頭部軸部径");
            }
        }

        [TestMethod]
        public void BothShafts_ShareTheSameInnerBore()
        {
            // この製品の要。頭部は外側にだけ厚く、内径は先端軸部と完全に一致する。
            // ここが崩れると PC 鋼棒を両軸部で共通に扱っている前提そのものが成り立たない。
            foreach (var p in _piles)
                Assert.AreEqual(p.HeadDia - 2 * p.HeadThickness, p.TipDia - 2 * p.TipThickness, 1e-9,
                    $"{Id(p)}: 頭部軸部と先端軸部の内径が違う");
        }

        // ── 頭部軸部 = カタログ記載値の検算 ─────────────────────────

        [TestMethod]
        public void HeadSectionArea_MatchesHollowCircleFormula()
        {
            foreach (var p in _piles)
            {
                double calc = HollowArea(p.HeadDia, p.HeadThickness);
                Assert.AreEqual(calc, p.HeadAo, calc * RelTol, $"{Id(p)}: Ao");
            }
        }

        [TestMethod]
        public void HeadTransformedArea_MatchesTendonContribution()
        {
            foreach (var p in _piles)
            {
                double calc = HollowArea(p.HeadDia, p.HeadThickness) + (N(p) - 1) * p.Ap;
                Assert.AreEqual(calc, p.HeadAe, calc * RelTol, $"{Id(p)}: Ae");
            }
        }

        [TestMethod]
        public void HeadTransformedInertia_IsConsistentWithTheSolvedPcd()
        {
            // PCD は Ie から逆算しているので、この一致自体は恒等。
            // 意味があるのは「逆算値が製品ごとに 1 つに定まるか」で、それは下のテストで見る。
            foreach (var p in _piles)
            {
                double calc = HollowInertia(p.HeadDia, p.HeadThickness)
                            + (N(p) - 1) * p.Ap * Math.Pow(p.Pcd / 2, 2) / 2;
                Assert.AreEqual(calc, p.HeadIe, calc * RelTol, $"{Id(p)}: Ie");
            }
        }

        [TestMethod]
        public void SolvedPcd_IsUniquePerDesignationAndLandsOnAFiveMillimetreGrid()
        {
            // カタログは PCD を印字していない。逆算値が Fc・種類によらず同じで、
            // かつ 5mm 丸めに乗ることが「逆算が正しい」ことの裏付けになっている。
            foreach (var g in _piles.GroupBy(p => p.Name))
            {
                var values = g.Select(p => p.Pcd).Distinct().ToList();
                Assert.AreEqual(1, values.Count,
                    $"{g.Key}: PCD が 1 つに定まらない ({string.Join(", ", values)})");
                Assert.AreEqual(0.0, values[0] % 5.0, 1e-9, $"{g.Key}: PCD {values[0]} が 5mm 刻みでない");
            }
        }

        [TestMethod]
        public void SolvedPcd_FitsInsideBothShaftWalls()
        {
            // PC 鋼棒は両軸部を貫通するので、配筋円はどちらの肉厚の中にも収まらなければならない。
            foreach (var p in _piles)
            {
                Assert.IsTrue(p.InnerDiameter < p.Pcd && p.Pcd < p.TipDia,
                    $"{Id(p)}: PCD {p.Pcd} が先端軸部の肉厚 ({p.InnerDiameter}..{p.TipDia}) の外");
                Assert.IsTrue(p.InnerDiameter < p.Pcd && p.Pcd < p.HeadDia,
                    $"{Id(p)}: PCD {p.Pcd} が頭部軸部の肉厚 ({p.InnerDiameter}..{p.HeadDia}) の外");
            }
        }

        // ── 先端軸部 = 算定値の検証 ─────────────────────────────────

        [TestMethod]
        public void TipSection_IsComputedFromTheSameTendons()
        {
            foreach (var p in _piles)
            {
                double ao = HollowArea(p.TipDia, p.TipThickness);
                Assert.AreEqual(ao, p.TipAo, ao * RelTol, $"{Id(p)}: 先端 Ao");
                Assert.AreEqual(ao + (N(p) - 1) * p.Ap, p.TipAe, p.TipAe * RelTol, $"{Id(p)}: 先端 Ae");

                double ie = HollowInertia(p.TipDia, p.TipThickness)
                          + (N(p) - 1) * p.Ap * Math.Pow(p.Pcd / 2, 2) / 2;
                Assert.AreEqual(ie, p.TipIe, ie * RelTol, $"{Id(p)}: 先端 Ie");
            }
        }

        [TestMethod]
        public void TipPrestress_PreservesTheEffectivePrestressForce()
        {
            // σce·Ae = 有効プレストレス力。同じ杭・同じ PC 鋼棒なので両軸部で等しい。
            foreach (var p in _piles)
                Assert.AreEqual(p.HeadSigmaCe * p.HeadAe, p.TipSigmaCe * p.TipAe,
                    p.HeadSigmaCe * p.HeadAe * 1e-3, $"{Id(p)}: プレストレス力が両軸部で合わない");
        }

        [TestMethod]
        public void TipPrestress_MatchesTheJisGradeValues()
        {
            // ここが PCD 逆算とプレストレス力一定の仮定に対する<b>独立した裏付け</b>。
            // 2 つの仮定が正しいときだけ、先端軸部の σce が JIS A 5373 の
            // A/B/C 種の規定値 (4 / 8 / 10 N/mm²) になる。カタログ注記と同じ ±5% を許容する。
            var spec = new Dictionary<string, double> { ["A2"] = 4.0, ["B2"] = 8.0, ["C2"] = 10.0 };
            foreach (var p in _piles)
            {
                double s = spec[p.PrestressType];
                Assert.AreEqual(s, p.TipSigmaCe, s * 0.05,
                    $"{Id(p)}: 先端軸部 σce が JIS {p.PrestressType} 種の規定値から外れる");
            }
        }

        [TestMethod]
        public void HeadPrestress_IsDilutedByTheThickerWall()
        {
            // 同じプレストレス力を厚い頭部で受けるので σce は必ず小さくなる
            foreach (var p in _piles)
                Assert.IsTrue(p.HeadSigmaCe < p.TipSigmaCe,
                    $"{Id(p)}: 頭部 σce {p.HeadSigmaCe} ≥ 先端 σce {p.TipSigmaCe}");
        }

        // ── 断面性能の整合 ──────────────────────────────────────────

        [TestMethod]
        public void MomentCapacities_AreOrdered()
        {
            foreach (var p in _piles)
            {
                Assert.IsTrue(p.HeadMal < p.HeadMas, $"{Id(p)}: Mal < Mas");
                Assert.IsTrue(p.HeadMas <= p.HeadMcr, $"{Id(p)}: Mas ≤ Mcr");
                Assert.IsTrue(p.HeadMcr < p.HeadMu, $"{Id(p)}: Mcr < Mu");
                Assert.IsTrue(p.HeadQal < p.HeadQas, $"{Id(p)}: Qal < Qas");
                Assert.IsTrue(p.HeadQas < p.HeadQcr, $"{Id(p)}: Qas < Qcr");
            }
        }

        [TestMethod]
        public void AllowableAxialForce_IsBelowGrossConcreteCapacity()
        {
            foreach (var p in _piles)
            {
                double cap = p.FcAllowCompLong * p.HeadAe / 1000.0; // kN
                Assert.IsTrue(p.HeadNal <= cap * 1.02 + 1e-6,
                    $"{Id(p)}: N={p.HeadNal}kN > fc·Ae={cap:F0}kN");
            }
        }

        [TestMethod]
        public void DesignConstants_MatchCatalogTable()
        {
            // p3「■設計に用いる諸数値」
            var comp = new Dictionary<double, (double L, double S)>
            {
                [105] = (30, 60),
                [123] = (35, 70),
            };
            foreach (var p in _piles)
            {
                var (l, sh) = comp[p.Fc];
                Assert.AreEqual(40000.0, p.Ec, 1e-9, "コンクリート ヤング係数");
                Assert.AreEqual(l, p.FcAllowCompLong, 1e-9, $"Fc{p.Fc} 長期許容圧縮");
                Assert.AreEqual(sh, p.FcAllowCompShort, 1e-9, $"Fc{p.Fc} 短期許容圧縮");
                Assert.AreEqual(1.2, p.FcAllowDiagLong, 1e-9);
                Assert.AreEqual(1.8, p.FcAllowDiagShort, 1e-9);
                Assert.AreEqual(0.25, p.AllowBendTensLongFactor, 1e-9, "長期許容曲げ引張 = σce/4");
                Assert.AreEqual(0.5, p.AllowBendTensShortFactor, 1e-9, "短期許容曲げ引張 = σce/2");
                Assert.AreEqual(1275.0, p.Ftp, 1e-9, "PC 鋼棒 耐力");
                Assert.AreEqual(1420.0, p.SigmaPu, 1e-9, "PC 鋼棒 引張強さ");
                Assert.AreEqual(200000.0, p.Ep, 1e-9, "PC 鋼棒 ヤング係数");
            }
        }

        // ── 姿図 ───────────────────────────────────────────────────

        [TestMethod]
        public void NodeDimensions_AreCatalogValues()
        {
            // 標準構造図の寸法記入値: 節ピッチ 1000 / 先端 500 /
            // 節部 75 (φ800-7090 以上は 100) で、いずれも (D1 − D0)/2 に一致する。
            foreach (var p in _piles)
            {
                Assert.AreEqual(1000.0, p.NodePitch, 1e-9, "節ピッチ");
                Assert.AreEqual(500.0, p.ToeOffset, 1e-9, "最終節中心〜杭先端");
                Assert.AreEqual(500.0, p.HeadOffset, 1e-9, "杭頭〜第 1 節中心 (導出値)");
                Assert.AreEqual((p.NodeDia - p.TipDia) / 2.0, p.NodeFlatLength, 1e-9, "節部長さ");
                CollectionAssert.Contains(new[] { 75.0, 100.0 }, p.NodeFlatLength,
                    $"{Id(p)}: 節部長さ {p.NodeFlatLength} が寸法記入値 75/100 のどちらでもない");
            }
        }

        [TestMethod]
        public void NodeCenterPositions_FollowCatalogLayout()
        {
            var p = _piles.First();
            // 杭長 10m: 杭頭 500mm から 1000mm ピッチ、杭先端 500mm 手前まで
            var zs = p.NodeCenterPositions(10.0).ToList();
            Assert.AreEqual(500.0, zs.First(), 1e-9);
            Assert.AreEqual(9500.0, zs.Last(), 1e-9);
            Assert.AreEqual(10, zs.Count);
        }

        // ── DTO 変換 ───────────────────────────────────────────────

        [TestMethod]
        public void ToPrecastPile_SwitchesShaftDimensionsAndPrestress()
        {
            var p = _piles.First(x => x.Name == "400-3045" && x.Fc == 105 && x.PrestressType == "A2");

            var head = p.ToPrecastPile();
            Assert.AreEqual("BFS_HEAD", head.PileType);
            Assert.AreEqual(p.HeadDia, head.PileDiameter, 1e-9);
            Assert.AreEqual(p.HeadThickness, head.PileThickness, 1e-9);
            Assert.AreEqual(p.HeadSigmaCe, head.SigmaE, 1e-9);

            var tip = p.ToPrecastPile(tipPart: true);
            Assert.AreEqual("BFS_TIP", tip.PileType);
            Assert.AreEqual(p.TipDia, tip.PileDiameter, 1e-9);
            Assert.AreEqual(p.TipThickness, tip.PileThickness, 1e-9);
            Assert.AreEqual(p.TipSigmaCe, tip.SigmaE, 1e-9);
            Assert.AreEqual(p.TipSigmaCe / 2.0, tip.Fbc, 1e-9, "短期許容曲げ引張 = σce/2");

            // PC 鋼棒は両軸部共通、主筋・鋼管は無い
            foreach (var dto in new[] { head, tip })
            {
                Assert.AreEqual(p.Ap, dto.Ap, 1e-9);
                Assert.AreEqual(p.Pcd, dto.Dp, 1e-9);
                Assert.IsFalse(dto.HasReinf);
                Assert.AreEqual(0.0, dto.Ts, 1e-9);
            }
            Assert.AreNotEqual(head.Name, tip.Name, "表示名が衝突している");
        }
    }
}
