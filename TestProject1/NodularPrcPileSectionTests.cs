using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Models.PileLibrary;
using System;
using System.Linq;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// PRC節杭 を杭断面として選んだときの挙動を固定する。
    ///
    /// 1 本の PRC節杭 は杭頭側の <b>PRC部</b>（PC鋼棒 + 異形棒鋼）と、それより下の
    /// <b>PHC部</b>（PC鋼棒のみ）という 2 断面から成る。この 2 つを別々の断面タイプとして
    /// 杭区間に割り当てられることと、断面耐力がそれぞれストレート PRC / PHC 杭と
    /// 完全に一致することを検証する。
    ///
    /// 節杭固有の形状・自重は 2 断面で共通（同じ製品の別部位なので）。
    /// 「PHC部 に異形棒鋼が残る／PRC部 の主筋が落ちる」類の事故を検出する。
    /// </summary>
    [TestClass]
    public class NodularPrcPileSectionTests
    {
        /// <summary>φ440-300 Fc105 Ⅰ種 標準 (軸部 D=300, t=60, PC鋼棒 6-φ10.0, 異形棒鋼 6-D13)</summary>
        private const string SamplePrcName = "NPRC-440-300-標準-105-Ⅰ";
        private const string SamplePhcPartName = SamplePrcName + "-PHC部";

        private static NodularPrcPile SampleProduct() =>
            PileSection.NodularPrcPiles.First(p => p.DisplayName == SamplePrcName);

        private static PileSection MakeSection(string sectionType, string productName)
        {
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = sectionType,
            };
            s.SelectedPrecastPile.Name = productName;
            s.RecalculateSelectedPrecastPile();
            return s;
        }

        private static PileSection MakePrcPart() =>
            MakeSection(PileTypeNames.PrcNodular, SamplePrcName);

        private static PileSection MakePhcPart() =>
            MakeSection(PileTypeNames.PrcNodularPhcPart, SamplePhcPartName);

        /// <summary>
        /// 節杭と<b>断面諸元が完全に同一</b>のストレート断面を作る（比較用）。
        /// 諸元を転記したあと断面タイプだけを切り替えることで、
        /// 「断面タイプの違いだけ」を分離して比較できる。
        /// </summary>
        private static PileSection AsStraight(PileSection nodular, string straightType)
        {
            nodular.PileSectionType = straightType;
            return nodular;
        }

        private static object? Calculator(PileSection s) =>
            s.GetType()
             .GetMethod("CreateSectionCalculator", System.Reflection.BindingFlags.NonPublic
                                                 | System.Reflection.BindingFlags.Instance)!
             .Invoke(s, null);

        // ── 分類が生きていること ────────────────────────────────

        [TestMethod]
        public void BothSectionTypes_AreAcceptedAsSectionType()
        {
            foreach (var type in new[] { PileTypeNames.PrcNodular, PileTypeNames.PrcNodularPhcPart })
            {
                var s = new PileSection { PileBodyType = PileTypeNames.PrecastConcrete };
                s.PileSectionType = type;

                // validTypes への登録漏れがあると黙って 鉄筋コンクリート部 に差し替えられる
                Assert.AreEqual(type, s.PileSectionType);
                Assert.IsTrue(s.IsNodularPile, $"{type} が節杭として認識されない");
                Assert.IsTrue(s.IsHollowPrecastSection, $"{type} が中空既製断面として認識されない");
            }
        }

        [TestMethod]
        public void BothSectionTypes_AreOfferedExceptOnTopSegment()
        {
            var s = new PileSection { IsTopSegment = false };
            CollectionAssert.Contains(s.PreCastConcretePileSectionTypeOption, PileTypeNames.PrcNodular);
            CollectionAssert.Contains(s.PreCastConcretePileSectionTypeOption, PileTypeNames.PrcNodularPhcPart);

            // 節杭は上杭に継ぐ下杭として使うので最上段では選べない
            s.IsTopSegment = true;
            CollectionAssert.DoesNotContain(s.PreCastConcretePileSectionTypeOption, PileTypeNames.PrcNodular);
            CollectionAssert.DoesNotContain(s.PreCastConcretePileSectionTypeOption, PileTypeNames.PrcNodularPhcPart);
        }

        [TestMethod]
        public void PileSectionType_SurvivesJsonRoundTrip()
        {
            foreach (var s in new[] { MakePrcPart(), MakePhcPart() })
            {
                var restored = JsonSerializer.Deserialize<PileSection>(JsonSerializer.Serialize(s));

                Assert.IsNotNull(restored);
                Assert.AreEqual(s.PileSectionType, restored!.PileSectionType);
                Assert.AreEqual(s.SelectedPrecastPile.Name, restored.SelectedPrecastPile.Name);
                Assert.AreEqual(s.NodeDiameter, restored.NodeDiameter, 1e-9);
                Assert.AreEqual(s.CatalogMassPerM, restored.CatalogMassPerM, 1e-9);
            }
        }

        // ── 製品ライブラリの接続 ────────────────────────────────

        [TestMethod]
        public void ProductOptions_ExposeEveryCatalogProductOnce()
        {
            Assert.AreEqual(232, PileSection.NodularPrcPiles.Count);
            Assert.AreEqual(232, PileSection.NodularPrcOption.Count);
            Assert.AreEqual(232, PileSection.NodularPrcPhcPartOption.Count);

            CollectionAssert.Contains(PileSection.NodularPrcOption.ToList(), SamplePrcName);
            CollectionAssert.Contains(PileSection.NodularPrcPhcPartOption.ToList(), SamplePhcPartName);

            // PRC部 と PHC部 は同じ呼び名の別断面なので、表示名が衝突してはいけない
            var overlap = PileSection.NodularPrcOption
                .Intersect(PileSection.NodularPrcPhcPartOption).ToList();
            Assert.AreEqual(0, overlap.Count, $"表示名が重複: {string.Join(", ", overlap.Take(3))}");
        }

        [TestMethod]
        public void SelectedProduct_IsRecognisedAsInLibrary()
        {
            foreach (var s in new[] { MakePrcPart(), MakePhcPart() })
                Assert.IsTrue(s.IsSelectedPrecastPileInLibrary(),
                    $"{s.PileSectionType}: ファイル読込時のライブラリ存在検証で未知扱いされてはいけない");
        }

        [TestMethod]
        public void SelectingProduct_TransfersShaftDimensions()
        {
            var n = SampleProduct();
            foreach (var s in new[] { MakePrcPart(), MakePhcPart() })
            {
                Assert.AreEqual(n.D, s.PileDiameter, 1e-9, "杭径は軸部径");
                Assert.AreEqual(n.D, s.ConcreteOutDia, 1e-9, "鋼管が無いのでコンクリート外径 = 軸部径");
                Assert.AreEqual(n.T, s.ConcreteThickness, 1e-9);
                Assert.AreEqual(n.Fc, s.ConcreteFc, 1e-9);
                Assert.AreEqual(n.Ap, s.TendonAp, 1e-9);
                Assert.AreEqual(n.Pcd, s.TendonDp, 1e-9);
            }
        }

        [TestMethod]
        public void PrcPart_CarriesRebar_PhcPartDoesNot()
        {
            var n = SampleProduct();

            var prc = MakePrcPart();
            Assert.AreEqual(n.BarCount, prc.MainBarNum, "PRC部の主筋本数");
            Assert.AreEqual(n.BarDesignation, prc.MainBarSize);
            Assert.AreEqual(n.BarPcd, prc.MainBarDr, 1e-9);
            Assert.AreEqual(n.Ag, prc.MainBarAg, 1e-9);
            Assert.AreEqual(n.Er, prc.MainBarEr, 1e-9, "異形棒鋼のヤング係数 205000");
            Assert.AreEqual(n.SigmaCe, prc.Prestress, 1e-9, "PRC部の σce");

            var phc = MakePhcPart();
            Assert.AreEqual(0, phc.MainBarNum, "PHC部に主筋は無い");
            Assert.AreEqual(0.0, phc.MainBarAg, 1e-9);
            Assert.AreEqual(n.PhcSigmaCe, phc.Prestress, 1e-9, "PHC部の σce は PRC部と別値");
            Assert.AreNotEqual(n.SigmaCe, n.PhcSigmaCe, "そもそも 2 断面で σce が違うことが前提");
        }

        [TestMethod]
        public void SelectingProduct_TransfersNodularSpecificValues()
        {
            var n = SampleProduct();
            foreach (var s in new[] { MakePrcPart(), MakePhcPart() })
            {
                // PRC部 / PHC部 は同じ製品の別部位なので形状・質量は共通
                Assert.AreEqual(n.Do, s.NodeDiameter, 1e-9);
                Assert.AreEqual(n.MassPerM, s.CatalogMassPerM, 1e-9);
                Assert.AreEqual(n.NodePitch, s.NodePitch, 1e-9);
                Assert.AreEqual(n.HeadOffset, s.NodeHeadOffset, 1e-9);
                Assert.AreEqual(n.ToeOffset, s.NodeToeOffset, 1e-9);
                Assert.IsTrue(s.NodeDiameter > s.PileDiameter, "節部径は軸部径より大きい");
            }
        }

        [TestMethod]
        public void SwitchingAwayFromNodular_ClearsNodularValues()
        {
            var s = MakePrcPart();
            Assert.IsTrue(s.NodeDiameter > 0);

            s.PileSectionType = PileTypeNames.Prc;
            s.SelectedPrecastPile.Name = PileSection.PRCOption.First();
            s.RecalculateSelectedPrecastPile();

            Assert.AreEqual(0.0, s.NodeDiameter, 1e-9, "節杭以外では節部径を残さない");
            Assert.AreEqual(0.0, s.CatalogMassPerM, 1e-9);
        }

        // ── 断面耐力はストレート杭と同一 ────────────────────────

        [TestMethod]
        public void SectionCalculator_MatchesTheCorrespondingStraightPile()
        {
            Assert.AreEqual("PRCSection", Calculator(MakePrcPart())?.GetType().Name,
                "PRC部が PRCSection にならないと主筋が耐力に入らない");
            Assert.AreEqual("PHCSection", Calculator(MakePhcPart())?.GetType().Name,
                "PHC部が PHCSection にならないと存在しない主筋が耐力に入る");
        }

        [TestMethod]
        public void MPhiCacheKeys_AreDistinctAndRegistered()
        {
            string keyPrc = MakePrcPart().GetMPhiCacheKey(1000.0);
            string keyPhc = MakePhcPart().GetMPhiCacheKey(1000.0);

            // キー未登録だと OTHER| に落ちて別断面とキャッシュが衝突する
            Assert.IsTrue(keyPrc.StartsWith("NPRC|"), keyPrc);
            Assert.IsTrue(keyPhc.StartsWith("NPRC-PHC|"), keyPhc);
            Assert.AreNotEqual(keyPrc, keyPhc);

            // ストレート杭・PHC節杭 とも衝突しない
            string keyStraightPrc = AsStraight(MakePrcPart(), PileTypeNames.Prc).GetMPhiCacheKey(1000.0);
            string keyStraightPhc = AsStraight(MakePhcPart(), PileTypeNames.Phc).GetMPhiCacheKey(1000.0);
            string keyNph = AsStraight(MakePhcPart(), PileTypeNames.PhcNodular).GetMPhiCacheKey(1000.0);
            foreach (var other in new[] { keyStraightPrc, keyStraightPhc, keyNph })
            {
                Assert.AreNotEqual(other, keyPrc);
                Assert.AreNotEqual(other, keyPhc);
            }
        }

        [TestMethod]
        public void NMCurve_MatchesEquivalentStraightPile()
        {
            // 「断面性能は軸部基準」の実証。節部径は断面耐力に一切効かない。
            AssertNMEqual(MakePrcPart(), PileTypeNames.Prc);
            AssertNMEqual(MakePhcPart(), PileTypeNames.Phc);
        }

        private static void AssertNMEqual(PileSection nodular, string straightType)
        {
            var (nN, mN) = nodular.UnfactoredUltimateNM;
            var straight = AsStraight(MakeSection(nodular.PileSectionType, nodular.SelectedPrecastPile.Name),
                                      straightType);
            var (nP, mP) = straight.UnfactoredUltimateNM;

            Assert.AreEqual(nP.Count, nN.Count, $"{straightType}: N-M 曲線の点数が一致しない");
            for (int i = 0; i < nP.Count; i++)
            {
                Assert.AreEqual(nP[i], nN[i], Math.Max(Math.Abs(nP[i]), 1.0) * 1e-9, $"{straightType} N[{i}]");
                Assert.AreEqual(mP[i], mN[i], Math.Max(Math.Abs(mP[i]), 1.0) * 1e-9, $"{straightType} M[{i}]");
            }
        }

        [TestMethod]
        public void PrcPart_IsStrongerThanPhcPartOfTheSameProduct()
        {
            // 異形棒鋼の分だけ PRC部 の曲げ耐力が上回る (カタログの Mu > PhcMu と整合)
            var n = SampleProduct();
            Assert.IsTrue(n.Mu > n.PhcMu, "カタログ値の前提");

            double mPrc = MakePrcPart().UnfactoredUltimateNM.M.Max();
            double mPhc = MakePhcPart().UnfactoredUltimateNM.M.Max();
            Assert.IsTrue(mPrc > mPhc, $"PRC部 {mPrc:F1} が PHC部 {mPhc:F1} 以下");
        }

        [TestMethod]
        public void ElasticStiffness_DiffersByTheRebarContribution()
        {
            // EA/EI は換算断面なので、2 部位の差は異形棒鋼の寄与そのものになる。
            // カタログの Ae − PhcAe = (nr−1)Ag と同じ量を、アプリ側が独立に出せているかの検算。
            var n = SampleProduct();
            var prc = MakePrcPart();
            var phc = MakePhcPart();

            double expectedEA = (n.Er - n.Ec) * n.Ag / 1000.0; // N -> kN
            Assert.AreEqual(expectedEA, prc.EA - phc.EA, Math.Abs(expectedEA) * 1e-6,
                "EA の差が異形棒鋼の換算分と一致しない");

            // EI の差も同様にカタログの Ie − PhcIe と一致する。
            // 注: 比ではなく差で比べる。PileSection.EI は PC 鋼材の換算項を持たないため、
            //     EI 自体はカタログの Ie 基準より小さい (節杭に限らず PHC/PRC 杭 共通の既存挙動)。
            double expectedEI = n.Ec * (n.Ie - n.PhcIe) * 1e-9; // N·mm² -> kN·m²
            Assert.AreEqual(expectedEI, prc.EI - phc.EI, Math.Abs(expectedEI) * 0.01,
                "EI の差が異形棒鋼の換算分と一致しない");
        }

        // ── 自重はカタログ標準質量による ───────────────────────

        [TestMethod]
        public void Weight_ComesFromCatalogMassAndIsSharedByBothParts()
        {
            var n = SampleProduct();
            double expected = n.MassPerM * UnitConversion.TON_TO_KN;

            Assert.AreEqual(expected, MakePrcPart().W, 1e-9);
            Assert.AreEqual(expected, MakePhcPart().W, 1e-9, "同じ製品なので PHC部 も同じ自重");
        }

        [TestMethod]
        public void Weight_OfEveryProduct_IsPositiveAndPlausible()
        {
            foreach (var n in PileSection.NodularPrcPiles)
            {
                double w = n.MassPerM * UnitConversion.TON_TO_KN;
                Assert.IsTrue(w > 0, $"{n.DisplayName}: 自重が 0 以下");
                Assert.IsTrue(w < 20.0, $"{n.DisplayName}: 自重 {w:F2} kN/m が過大");
            }
        }

        // ── 計算書 (docx) の軸力制限ラベル ───────────────────────

        [TestMethod]
        public void DocxAxialLimitLabels_FollowTheCorrespondingStraightPile()
        {
            // 断面タイプを分岐に追加し忘れると「Nut/Nuc」の既定ラベルに落ちる。
            // PRC部 は主筋・テンドン破断限界 (PRC杭 と同じ)、PHC部 はひび割れ限界 (PHC杭 と同じ)。
            Assert.AreEqual(Label(PileTypeNames.Prc, isUltimate: true, index: 0),
                            Label(PileTypeNames.PrcNodular, isUltimate: true, index: 0));
            Assert.AreEqual(Label(PileTypeNames.Phc, isUltimate: true, index: 0),
                            Label(PileTypeNames.PrcNodularPhcPart, isUltimate: true, index: 0));

            // 圧壊限界の係数も PRC=60 / PHC=65 で分かれる
            StringAssert.Contains(Label(PileTypeNames.PrcNodular, true, 2), "60");
            StringAssert.Contains(Label(PileTypeNames.PrcNodularPhcPart, true, 2), "65");

            foreach (var type in new[] { PileTypeNames.PrcNodular, PileTypeNames.PrcNodularPhcPart })
                foreach (int i in new[] { 0, 1, 2 })
                    StringAssert.DoesNotMatch(Label(type, false, i), new System.Text.RegularExpressions.Regex("^Nd"),
                        $"{type}[{i}]: 既定ラベルに落ちている");
        }

        private static string Label(string sectionType, bool isUltimate, int index) =>
            (string)typeof(PileDesign.Output.WordDocument)
                .Assembly.GetType("PileDesign.Output.WordDocument")!
                .GetMethod("GetAxialLimitMeaning", System.Reflection.BindingFlags.NonPublic
                                                 | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [PileTypeNames.PrecastConcrete, sectionType, isUltimate, index])!;

        // ── 杭頭タイプ (拡頭) ──────────────────────────────────

        [TestMethod]
        public void NodularHead_ResolvesFromDiameterAbove()
        {
            // φ440-300 には拡頭径 400 / 450 が用意されている
            var s = MakePrcPart();

            s.ResolveNodularHead(400.0);
            Assert.AreEqual(400.0, s.NodeHeadDiameter, 1e-9);
            Assert.AreEqual(600.0, s.NodeHeadLength, 1e-9);
            Assert.AreEqual(PileSection.NodularHeadTypes.IntermediateHead, s.NodularHeadType);

            // 直上が軸部径と同径なら拡頭不要 (PRC部の下に付く PHC部 がこの状態)
            s.ResolveNodularHead(300.0);
            Assert.AreEqual(0.0, s.NodeHeadDiameter, 1e-9);
            Assert.AreEqual(PileSection.NodularHeadTypes.Standard, s.NodularHeadType);

            // 一致する拡頭径が無ければ標準タイプに落として理由を残す
            s.ResolveNodularHead(500.0);
            Assert.AreEqual(PileSection.NodularHeadTypes.Standard, s.NodularHeadType);
            StringAssert.Contains(s.NodularHeadNote, "一致する拡頭径がありません");
        }

        [TestMethod]
        public void NodularHead_UsesPrcCatalogNotPhcCatalog()
        {
            // NPH と NPRC で拡頭形状一覧が別ファイルなので、取り違えると
            // 「呼び名は同じだが用意された拡頭径が違う」ケースで静かに誤判定する。
            // φ1200-1100 は NPRC では拡頭径 1200 のみ。
            var s = MakeSection(PileTypeNames.PrcNodular, "NPRC-1200-1100-標準-105-ⅠA");
            s.ResolveNodularHead(1200.0);

            Assert.AreEqual(1200.0, s.NodeHeadDiameter, 1e-9);
            Assert.AreEqual(PileSection.NodularHeadTypes.EnlargedHead, s.NodularHeadType);
        }
    }
}
