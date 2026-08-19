using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// PRC 節杭製品ライブラリ (ジャパンパイル JP-NPRC105) の検証。
    ///
    /// カタログ (nprc.pdf) の断面諸数値は軸部の中空円形断面から一意に計算できるため、
    /// 転記した全行を計算値と突合できる。NPH と違い異形棒鋼 (SD345, Er=205000) を持つので
    /// 換算比は np = Ep/Ec = 5.0 と nr = Er/Ec = 5.125 の 2 つを使う。
    ///
    /// カタログは有効数字 3〜4 桁に丸められているので相対 0.15% を許容する
    /// (桁の転記ミスは 1% 以上ずれるため確実に検出される)。
    /// </summary>
    [TestClass]
    public class NodularPrcPileLibraryTests
    {
        private const double RelTol = 0.0015; // カタログの丸め (有効数字 3〜4 桁) を吸収

        private static List<NodularPrcPile> _piles = [];
        private static List<NodularPrcPileHead> _heads = [];

        [ClassInitialize]
        public static void Init(TestContext _)
        {
            _piles = NodularPrcPileLoader.LoadDefault();
            _heads = NodularPrcPileLoader.LoadDefaultHeads();
        }

        // ── 読み込み ────────────────────────────────────────────────

        [TestMethod]
        public void Library_LoadsExpectedRowCount()
        {
            // 断面諸数値一覧表 (p2/p4/p6) の全データ行を呼び名ごとに展開した結果
            Assert.AreEqual(232, _piles.Count);
            Assert.AreEqual(25, _heads.Count, "拡頭中間径/拡頭タイプの形状一覧 (p8) は 25 行");
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
        public void Library_IsSingleConcreteStrengthSeries()
        {
            // JP-NPRC は Fc105 のみ (NPH は 85/105/123 の 3 種)
            var fcs = _piles.Select(p => p.Fc).Distinct().ToList();
            CollectionAssert.AreEqual(new[] { 105.0 }, fcs);
            Assert.IsTrue(_piles.All(p => p.Series == "JP-NPRC105"));
        }

        [TestMethod]
        public void Library_UsesRomanNumeralPrestressTypes()
        {
            // 種類はローマ数字 Ⅰ〜Ⅷ。径によっては ⅠA / ⅠB のように枝番が付く。
            var kinds = _piles.Select(p => p.PrestressType).Distinct().ToList();
            CollectionAssert.AreEquivalent(
                new[] { "Ⅰ", "ⅠA", "ⅠB", "Ⅱ", "ⅡA", "ⅡB", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ", "Ⅶ", "Ⅷ" }, kinds);
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

        [TestMethod]
        public void Library_AlwaysHasBothPcBarsAndDeformedBars()
        {
            // PRC = PC 鋼棒と異形棒鋼の併用。片方が欠けていたら列ズレを疑う。
            foreach (var p in _piles)
            {
                Assert.IsTrue(p.PcCount > 0 && p.Ap > 0, $"{Id(p)}: PC 鋼棒");
                Assert.IsTrue(p.BarCount > 0 && p.Ag > 0, $"{Id(p)}: 異形棒鋼");
                // 異形棒鋼は PC 鋼棒と同本数 (交互配置) か、その半数 (1 本おき) のいずれか
                Assert.IsTrue(p.BarCount == p.PcCount || p.BarCount * 2 == p.PcCount,
                    $"{Id(p)}: PC 鋼棒 {p.PcCount} 本に対し異形棒鋼 {p.BarCount} 本");
                Assert.IsTrue(p.BarPcd < p.Pcd, $"{Id(p)}: 異形棒鋼は PC 鋼棒より内側");
                StringAssert.StartsWith(p.BarDesignation, "D", $"{Id(p)}: 異形棒鋼の呼び名");
            }
        }

        [TestMethod]
        public void RebarArea_MatchesNominalAreaTimesCount()
        {
            // Ag = 本数 × JIS G 3112 の公称断面積。呼び名・本数・面積が 3 者で整合していることを
            // 確認することで、列ズレによる誤抽出を検出する。
            var nominal = new Dictionary<string, double>
            {
                ["D13"] = 126.7, ["D16"] = 198.6, ["D19"] = 286.5, ["D22"] = 387.1,
                ["D25"] = 506.7, ["D29"] = 642.4, ["D32"] = 794.2, ["D35"] = 956.6,
            };
            foreach (var p in _piles)
            {
                Assert.IsTrue(nominal.ContainsKey(p.BarDesignation),
                    $"{Id(p)}: 未知の異形棒鋼 {p.BarDesignation}");
                double expected = p.BarCount * nominal[p.BarDesignation];
                // カタログは Ag を 3〜4 桁に丸めている
                Assert.AreEqual(expected, p.Ag, expected * 0.01, $"{Id(p)}: Ag");
            }
        }

        // ── 断面諸数値の検算 (カタログ vs 計算) ─────────────────────

        [TestMethod]
        public void SectionArea_MatchesHollowCircleFormula()
        {
            // Ao = π/4 (D² − di²)、di = D − 2t （断面性能は軸部基準）
            AssertAllClose(AoCalc, p => p.Ao, "Ao");
        }

        [TestMethod]
        public void SecondMomentOfArea_MatchesHollowCircleFormula()
        {
            // Io = π/64 (D⁴ − di⁴)
            AssertAllClose(IoCalc, p => p.Io, "Io");
        }

        [TestMethod]
        public void TransformedArea_MatchesPcBarAndRebarContribution()
        {
            // Ae = Ao + (np − 1) Ap + (nr − 1) Ag
            // nr = Er/Ec = 205000/40000 = 5.125 (異形棒鋼のヤング係数は PC 鋼棒と違う)
            AssertAllClose(p => AoCalc(p) + (Np(p) - 1.0) * p.Ap + (Nr(p) - 1.0) * p.Ag,
                           p => p.Ae, "Ae");
        }

        [TestMethod]
        public void TransformedSecondMoment_MatchesRingContributions()
        {
            // Ie = Io + (np − 1) Ap (PCD/2)² / 2 + (nr − 1) Ag (BarPCD/2)² / 2
            // 円周上に均等配置された鋼材群の直径軸まわり 2 次モーメントは Σ A r² / 2
            AssertAllClose(p => IoCalc(p)
                                + (Np(p) - 1.0) * p.Ap * Math.Pow(p.Pcd / 2.0, 2) / 2.0
                                + (Nr(p) - 1.0) * p.Ag * Math.Pow(p.BarPcd / 2.0, 2) / 2.0,
                           p => p.Ie, "Ie");
        }

        [TestMethod]
        public void SectionModulus_IsConsistentWithTransformedSecondMoment()
        {
            // Ze = Ie / (D/2)
            AssertAllClose(p => p.Ie / (p.D / 2.0), p => p.Ze, "Ze");
        }

        [TestMethod]
        public void PhcPartSection_ExcludesRebarContribution()
        {
            // PHC部は異形棒鋼を持たないので PC 鋼棒の項だけになる
            AssertAllClose(p => AoCalc(p) + (Np(p) - 1.0) * p.Ap, p => p.PhcAe, "PhcAe");
            AssertAllClose(p => IoCalc(p) + (Np(p) - 1.0) * p.Ap * Math.Pow(p.Pcd / 2.0, 2) / 2.0,
                           p => p.PhcIe, "PhcIe");
        }

        [TestMethod]
        public void FirstMomentOfArea_MatchesHollowCircleFormula()
        {
            // So = (D³ − di³)/12 (中実円 D³/12 から中空部を引いたもの)。
            // カタログ印字に誤植のある行 (Note 付き) は除外し、
            // 代わりに SoFromSection が正しく算出されていることを確認する。
            int noted = 0;
            foreach (var p in _piles)
            {
                double expected = SoCalc(p);
                Assert.AreEqual(expected, p.SoFromSection, expected * RelTol, $"{Id(p)}: SoFromSection");

                if (!string.IsNullOrEmpty(p.Note)) { noted++; continue; }
                Assert.AreEqual(expected, p.So, expected * RelTol, $"{Id(p)}: カタログ So");
            }
            Assert.AreEqual(18, noted, "カタログ So の誤植は φ700 (t=100) の 18 行のみ");
        }

        [TestMethod]
        public void CatalogTypo_IsLimitedToFirstMomentOfPhi700()
        {
            // 既知のカタログ誤植を明示的に固定しておく (将来カタログ改訂時に気付けるように)。
            // 印字 18 617×10³ は 18 167×10³ の桁の入れ替わり。
            var noted = _piles.Where(p => !string.IsNullOrEmpty(p.Note)).ToList();
            Assert.AreEqual(18, noted.Count);
            foreach (var p in noted)
            {
                Assert.AreEqual(700.0, p.D, 1e-9);
                Assert.AreEqual(100.0, p.T, 1e-9);
                Assert.AreEqual(18_617_000.0, p.So, 1.0);
                Assert.AreEqual(18_166_667.0, p.SoFromSection, 1.0);
            }
            // 同じ φ700 でも t=120 の行は正しく印字されている
            foreach (var p in _piles.Where(p => p.D == 700.0 && p.T == 120.0))
                Assert.AreEqual("", p.Note, $"{Id(p)}: t=120 の行は誤植ではない");
        }

        // ── 断面性能の整合 ──────────────────────────────────────────

        [TestMethod]
        public void MomentCapacities_AreOrdered()
        {
            // ひび割れ < 長期許容 < 短期許容 < 終局
            // (NPH と順序が違う。PRC は異形棒鋼があるためひび割れ後も許容応力度が伸びる)
            foreach (var p in _piles)
            {
                Assert.IsTrue(p.Msc < p.Mal, $"{Id(p)}: Msc < Mal");
                Assert.IsTrue(p.Mal < p.Mas, $"{Id(p)}: Mal < Mas");
                Assert.IsTrue(p.Mas < p.Mu, $"{Id(p)}: Mas < Mu");
                Assert.IsTrue(p.PhcMc < p.PhcMu, $"{Id(p)}: PHC部 Mc < Mu");
            }
        }

        [TestMethod]
        public void ShearCapacities_DecreaseWithShearSpanRatio()
        {
            // せん断スパン比が大きいほどせん断耐力は小さい
            foreach (var p in _piles)
            {
                Assert.IsTrue(p.QasStd10 > p.QasStd15 && p.QasStd15 > p.QasStd20, $"{Id(p)}: 標準型 Qas");
                Assert.IsTrue(p.QuStd10 > p.QuStd15 && p.QuStd15 > p.QuStd20, $"{Id(p)}: 標準型 Qu");
                Assert.IsTrue(p.QasHigh10 > p.QasHigh15 && p.QasHigh15 > p.QasHigh20, $"{Id(p)}: 高せん断型 Qas");
                Assert.IsTrue(p.QuHigh10 > p.QuHigh15 && p.QuHigh15 > p.QuHigh20, $"{Id(p)}: 高せん断型 Qu");
            }
        }

        [TestMethod]
        public void ShearCapacities_AreOrderedAcrossTypes()
        {
            foreach (var p in _piles)
            {
                Assert.IsTrue(p.Qal < p.QasStd20, $"{Id(p)}: 長期許容 < 短期許容");
                Assert.IsTrue(p.QasStd10 < p.QuStd10, $"{Id(p)}: Qas < Qu");
                // 高せん断型は標準型を下回らない
                Assert.IsTrue(p.QasHigh10 >= p.QasStd10, $"{Id(p)}: 高せん断型 Qas ≥ 標準型");
                Assert.IsTrue(p.QuHigh10 >= p.QuStd10, $"{Id(p)}: 高せん断型 Qu ≥ 標準型");
                Assert.IsTrue(p.PhcQas < p.PhcQu, $"{Id(p)}: PHC部 Qas < Qu");
            }
        }

        [TestMethod]
        public void PrcPart_IsStifferAndStrongerThanPhcPart()
        {
            // 異形棒鋼の分だけ PRC部 が上回る。ただし σce は換算断面積が大きい PRC部 の方が小さい。
            foreach (var p in _piles)
            {
                Assert.IsTrue(p.Ae > p.PhcAe, $"{Id(p)}: Ae > PhcAe");
                Assert.IsTrue(p.Ie > p.PhcIe, $"{Id(p)}: Ie > PhcIe");
                Assert.IsTrue(p.Mu > p.PhcMu, $"{Id(p)}: Mu > PhcMu");
                Assert.IsTrue(p.Nal > p.PhcNal, $"{Id(p)}: Nal > PhcNal");
                Assert.IsTrue(p.SigmaCe < p.PhcSigmaCe, $"{Id(p)}: σce は PRC部 の方が小さい");
            }
        }

        [TestMethod]
        public void AllowableAxialForce_IsBelowGrossConcreteCapacity()
        {
            // 長期許容軸力は「長期許容圧縮応力度 × 換算断面積」を超えない
            foreach (var p in _piles)
            {
                Assert.IsTrue(p.Nal <= p.FcAllowCompLong * p.Ae / 1000.0 * 1.02 + 1e-6,
                    $"{Id(p)}: PRC部 N={p.Nal}kN");
                Assert.IsTrue(p.PhcNal <= p.FcAllowCompLong * p.PhcAe / 1000.0 * 1.02 + 1e-6,
                    $"{Id(p)}: PHC部 N={p.PhcNal}kN");
            }
        }

        [TestMethod]
        public void ShearReinforcement_IsPresentForAllThreeSpecifications()
        {
            // 標準型 (490 / 785 N/mm²) と 高せん断型 (785 N/mm²) の 3 仕様
            foreach (var p in _piles)
            {
                Assert.IsTrue(p.ShearBarStdDia490 > 0 && p.ShearBarStdPitch490 > 0, $"{Id(p)}: 標準型 490");
                Assert.IsTrue(p.ShearBarStdDia785 > 0 && p.ShearBarStdPitch785 > 0, $"{Id(p)}: 標準型 785");
                Assert.IsTrue(p.ShearBarHighDia785 > 0 && p.ShearBarHighPitch785 > 0, $"{Id(p)}: 高せん断型");
                // 高せん断型は同じ線径をより密に配する
                Assert.IsTrue(p.ShearBarHighPitch785 <= p.ShearBarStdPitch785,
                    $"{Id(p)}: 高せん断型のピッチが標準型より粗い");
            }
        }

        // ── 設計に用いる諸定数 ──────────────────────────────────────

        [TestMethod]
        public void DesignConstants_MatchCatalogTable()
        {
            // p1「■設計に用いる諸定数」
            foreach (var p in _piles)
            {
                Assert.AreEqual(40000.0, p.Ec, 1e-9, "コンクリート ヤング係数");
                Assert.AreEqual(30.0, p.FcAllowCompLong, 1e-9, "長期許容圧縮");
                Assert.AreEqual(60.0, p.FcAllowCompShort, 1e-9, "短期許容圧縮");

                Assert.AreEqual(1275.0, p.Ftp, 1e-9, "PC 鋼棒 耐力");
                Assert.AreEqual(1420.0, p.SigmaPu, 1e-9, "PC 鋼棒 引張強さ");
                Assert.AreEqual(200000.0, p.Ep, 1e-9, "PC 鋼棒 ヤング係数");

                Assert.AreEqual(490.0, p.BarFtu, 1e-9, "異形棒鋼 引張強さ");
                Assert.AreEqual(345.0, p.BarFy, 1e-9, "異形棒鋼 降伏点応力度");
                Assert.AreEqual(215.0, p.BarAllowLong, 1e-9, "異形棒鋼 長期許容 (D25 以下)");
                Assert.AreEqual(195.0, p.BarAllowLongD29Up, 1e-9, "異形棒鋼 長期許容 (D29 以上)");
                Assert.AreEqual(345.0, p.BarAllowShort, 1e-9, "異形棒鋼 短期許容");
                Assert.AreEqual(205000.0, p.Er, 1e-9, "異形棒鋼 ヤング係数");
            }
        }

        [TestMethod]
        public void AllowableStresses_DifferBetweenPrcAndPhcParts()
        {
            // 斜引張の短期と曲げ引張は PHC部 にしか規定が無い (PRC部 の短期は Qas 表による)
            foreach (var p in _piles)
            {
                Assert.AreEqual(1.2, p.PrcAllowDiagLong, 1e-9, "PRC部 長期許容斜引張");
                Assert.AreEqual(1.2, p.PhcAllowDiagLong, 1e-9, "PHC部 長期許容斜引張");
                Assert.AreEqual(1.8, p.PhcAllowDiagShort, 1e-9, "PHC部 短期許容斜引張");
                Assert.AreEqual(0.25, p.PhcAllowBendTensLongFactor, 1e-9, "PHC部 長期許容曲げ引張 = σce/4");
                Assert.AreEqual(0.5, p.PhcAllowBendTensShortFactor, 1e-9, "PHC部 短期許容曲げ引張 = σce/2");
            }
        }

        [TestMethod]
        public void RebarSizes_SpanBothAllowableStressCategories()
        {
            // 長期許容応力度が D25 以下 / D29 以上 で分かれるため、両方の製品が存在することを確認する
            var sizes = _piles.Select(p => p.BarDesignation).Distinct().ToList();
            Assert.IsTrue(sizes.Any(s => BarSize(s) <= 25), $"D25 以下: {string.Join(",", sizes)}");
            Assert.IsTrue(sizes.Any(s => BarSize(s) >= 29), $"D29 以上: {string.Join(",", sizes)}");
        }

        // ── 形状 ───────────────────────────────────────────────────

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
        }

        [TestMethod]
        public void EstimatedNodeShape_FitsWithinNodePitch()
        {
            foreach (var p in _piles)
                Assert.IsTrue(p.EstimatedNodeTotalLength < p.NodePitch,
                    $"{Id(p)}: 節全長 {p.EstimatedNodeTotalLength} が節ピッチを超える");
        }

        [TestMethod]
        public void MassPerMetre_IsPresentAndPlausible()
        {
            // 標準質量表 (p8) は「0.154×L」形式。L = 杭長 [m] なので係数がそのまま t/m。
            foreach (var p in _piles)
            {
                Assert.IsTrue(p.MassPerM > 0, $"{Id(p)}: 質量が未設定");
                // 軸部コンクリート体積 × 2.5 t/m³ を下限、節の分を見込んで 1.6 倍を上限とする
                double shaft = AoCalc(p) * 1e-6 * 2.5; // t/m
                Assert.IsTrue(p.MassPerM > shaft * 0.95 && p.MassPerM < shaft * 1.6,
                    $"{Id(p)}: 質量 {p.MassPerM} t/m が軸部体積 {shaft:F3} t/m と不整合");
            }
        }

        [TestMethod]
        public void MassPerMetre_IncreasesWithThickness()
        {
            // 同じ呼び名なら肉厚が厚いほど重い (質量表と断面表の行対応がずれていないことの確認)
            foreach (var g in _piles.GroupBy(p => p.Name))
            {
                var byT = g.GroupBy(p => p.T)
                           .Select(x => (T: x.Key, Mass: x.First().MassPerM))
                           .OrderBy(x => x.T).ToList();
                for (int i = 1; i < byT.Count; i++)
                    Assert.IsTrue(byT[i].Mass > byT[i - 1].Mass,
                        $"{g.Key}: t={byT[i].T} の質量 {byT[i].Mass} が t={byT[i - 1].T} 以下");
            }
        }

        // ── 拡頭タイプ ──────────────────────────────────────────────

        [TestMethod]
        public void HeadTypes_ReferenceAnExistingDesignation()
        {
            var names = _piles.Select(p => p.Name).Distinct().ToList();
            foreach (var h in _heads)
            {
                CollectionAssert.Contains(names, h.Name, $"拡頭 {h.Name} に対応する呼び名が無い");
                Assert.AreEqual(600.0, h.Lt, 1e-9, "拡頭部長さはカタログ全行で 600mm");
                Assert.IsTrue(h.Dt > h.D, $"{h.Name}: 拡頭径 {h.Dt} が軸部径 {h.D} 以下");
                // 拡頭径は原則 節部径以下 (中間径) または節部径と同値だが、
                // φ440-300(450) / φ450-300(450) だけは Dt=450 > Do=440 となる。
                Assert.IsTrue(h.Dt <= h.Do || (h.D == 300.0 && h.Dt == 450.0),
                    $"{h.Name}: 拡頭径 {h.Dt} が節部径 {h.Do} を超えている");
            }
        }

        [TestMethod]
        public void HeadTypes_MatchDesignationDiameters()
        {
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

        // ── DTO 変換 ───────────────────────────────────────────────

        [TestMethod]
        public void ToPrecastPile_CarriesRebarOnlyForPrcPart()
        {
            var p = _piles.First(x => x.Name == "440-300");

            var prc = p.ToPrecastPile();
            Assert.AreEqual("NPRC", prc.PileType);
            Assert.IsTrue(prc.HasReinf, "PRC部は異形棒鋼を持つ");
            Assert.AreEqual(p.BarCount, prc.Nr);
            Assert.AreEqual(p.Ag, prc.Ag, 1e-9);
            Assert.AreEqual(p.BarPcd, prc.Dr, 1e-9);
            Assert.AreEqual(p.Er, prc.Er, 1e-9);
            Assert.AreEqual(p.SigmaCe, prc.SigmaE, 1e-9);

            var phc = p.ToPrecastPile(phcPart: true);
            Assert.AreEqual("NPRC_PHC", phc.PileType);
            Assert.IsFalse(phc.HasReinf, "PHC部は異形棒鋼を持たない");
            Assert.AreEqual(0, phc.Nr);
            Assert.AreEqual(0.0, phc.Ag, 1e-9);
            Assert.AreEqual(p.PhcSigmaCe, phc.SigmaE, 1e-9);
            // 短期許容曲げ引張 = σce/2
            Assert.AreEqual(p.PhcSigmaCe / 2.0, phc.Fbc, 1e-9);

            // 断面性能は軸部基準なので杭径・肉厚は両者共通、鋼管は無い
            foreach (var dto in new[] { prc, phc })
            {
                Assert.AreEqual(p.D, dto.PileDiameter, 1e-9);
                Assert.AreEqual(p.T, dto.PileThickness, 1e-9);
                Assert.AreEqual(0.0, dto.Ts, 1e-9);
            }
        }

        [TestMethod]
        public void PhcSectionModulus_IsDerivedFromPhcSecondMoment()
        {
            foreach (var p in _piles)
                Assert.AreEqual(p.PhcIe / (p.D / 2.0), p.PhcZe, 1e-6, $"{Id(p)}: PhcZe");
        }

        // ── ヘルパ ─────────────────────────────────────────────────

        private static string Id(NodularPrcPile p) => $"{p.Name} {p.PrestressType} {p.ThicknessType}";

        private static double Np(NodularPrcPile p) => p.Ep / p.Ec;
        private static double Nr(NodularPrcPile p) => p.Er / p.Ec;

        private static double AoCalc(NodularPrcPile p) =>
            Math.PI / 4.0 * (p.D * p.D - p.InnerDiameter * p.InnerDiameter);

        private static double IoCalc(NodularPrcPile p) =>
            Math.PI / 64.0 * (Math.Pow(p.D, 4) - Math.Pow(p.InnerDiameter, 4));

        private static double SoCalc(NodularPrcPile p) =>
            (Math.Pow(p.D, 3) - Math.Pow(p.InnerDiameter, 3)) / 12.0;

        private static int BarSize(string designation) =>
            int.Parse(designation.TrimStart('D'));

        private static void AssertAllClose(Func<NodularPrcPile, double> calc,
                                           Func<NodularPrcPile, double> catalog, string label)
        {
            foreach (var p in _piles)
            {
                double c = calc(p);
                Assert.AreEqual(c, catalog(p), Math.Abs(c) * RelTol, $"{Id(p)}: {label}");
            }
        }
    }
}
