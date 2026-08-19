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
    /// PHC節杭 を杭断面として選んだときの挙動を固定する。
    ///
    /// 節杭の断面性能はカタログ上すべて軸部の中空円形断面基準なので、
    /// <b>断面耐力はストレート PHC 杭と完全に一致しなければならない</b>。
    /// 一方で自重はカタログ標準質量によるため PHC 杭とは異なる。
    /// 「静かに PHC 杭として扱われる／静かに RcSection に化ける」類の事故を検出する。
    /// </summary>
    [TestClass]
    public class NodularPileSectionTests
    {
        /// <summary>φ440-300 Fc85 A種 標準 (軸部 D=300, t=60, PC鋼棒 6-φ7.1, PCD=240)</summary>
        private const string SampleName = "NPH-440-300-標準-85-A";

        private static NodularPile SampleProduct() =>
            PileSection.NodularPiles.First(p => p.DisplayName == SampleName);

        /// <summary>製品を選択済みの節杭断面を作る。</summary>
        private static PileSection MakeNodularSection()
        {
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = PileTypeNames.PhcNodular,
            };
            s.SelectedPrecastPile.Name = SampleName;
            s.RecalculateSelectedPrecastPile();
            return s;
        }

        /// <summary>
        /// 節杭と<b>断面諸元が完全に同一</b>のストレート PHC 断面を作る（比較用）。
        ///
        /// 諸元を手で組み直すと既定値（主筋など）が残って比較にならないため、
        /// 節杭として製品を転記したあと断面タイプだけを PHC杭 に切り替える。
        /// これにより「断面タイプの違いだけ」を分離して比較できる。
        /// （既製杭では RecalculatePileDia() に分岐が無いので寸法は保持される）
        /// </summary>
        private static PileSection MakeEquivalentPhcSection()
        {
            var s = MakeNodularSection();
            s.PileSectionType = PileTypeNames.Phc;
            return s;
        }

        // ── 分類が生きていること ────────────────────────────────

        [TestMethod]
        public void PhcNodular_IsAcceptedAsSectionType()
        {
            var s = new PileSection { PileBodyType = PileTypeNames.PrecastConcrete };
            s.PileSectionType = PileTypeNames.PhcNodular;

            // validTypes への登録漏れがあると黙って 鉄筋コンクリート部 に差し替えられる
            Assert.AreEqual(PileTypeNames.PhcNodular, s.PileSectionType);
            Assert.IsTrue(s.IsNodularPile);
        }

        [TestMethod]
        public void PhcNodular_IsOfferedInPrecastSectionTypeOptions()
        {
            var s = new PileSection();
            CollectionAssert.Contains(s.PreCastConcretePileSectionTypeOption, PileTypeNames.PhcNodular);
        }

        [TestMethod]
        public void PileSectionType_SurvivesJsonRoundTrip()
        {
            var s = MakeNodularSection();
            var restored = JsonSerializer.Deserialize<PileSection>(JsonSerializer.Serialize(s));

            Assert.IsNotNull(restored);
            Assert.AreEqual(PileTypeNames.PhcNodular, restored!.PileSectionType);
            Assert.AreEqual(s.NodeDiameter, restored.NodeDiameter, 1e-9);
            Assert.AreEqual(s.CatalogMassPerM, restored.CatalogMassPerM, 1e-9);
        }

        [TestMethod]
        public void UnknownSectionType_StillFallsBackButIsRecorded()
        {
            // 互換のためフォールバック自体は残す（例外にしない）
            var s = new PileSection { PileSectionType = "存在しない断面タイプ" };
            Assert.AreEqual(PileTypeNames.RcSection, s.PileSectionType);
        }

        // ── 製品ライブラリの接続 ────────────────────────────────

        [TestMethod]
        public void NodularPileOption_ExposesEveryCatalogProduct()
        {
            Assert.AreEqual(292, PileSection.NodularPiles.Count);
            Assert.AreEqual(292, PileSection.NodularPileOption.Count);
            CollectionAssert.Contains(PileSection.NodularPileOption.ToList(), SampleName);
        }

        [TestMethod]
        public void SelectingProduct_TransfersShaftDimensions()
        {
            var s = MakeNodularSection();
            var n = SampleProduct();

            Assert.AreEqual(n.D, s.PileDiameter, 1e-9, "杭径は軸部径");
            Assert.AreEqual(n.D, s.ConcreteOutDia, 1e-9, "鋼管が無いのでコンクリート外径 = 軸部径");
            Assert.AreEqual(n.T, s.ConcreteThickness, 1e-9);
            Assert.AreEqual(n.Fc, s.ConcreteFc, 1e-9);
            Assert.AreEqual(n.Ap, s.TendonAp, 1e-9);
            Assert.AreEqual(n.Pcd, s.TendonDp, 1e-9);
            Assert.AreEqual(n.SigmaCeCalc, s.Prestress, 1e-9, "プレストレスは σce の計算値");
        }

        [TestMethod]
        public void SelectingProduct_TransfersNodularSpecificValues()
        {
            var s = MakeNodularSection();
            var n = SampleProduct();

            Assert.AreEqual(n.Do, s.NodeDiameter, 1e-9);
            Assert.AreEqual(n.MassPerM, s.CatalogMassPerM, 1e-9);
            Assert.IsTrue(s.NodeDiameter > s.PileDiameter, "節部径は軸部径より大きい");
        }

        [TestMethod]
        public void SwitchingAwayFromNodular_ClearsNodularValues()
        {
            var s = MakeNodularSection();
            Assert.IsTrue(s.NodeDiameter > 0);

            s.PileSectionType = PileTypeNames.Phc;
            s.SelectedPrecastPile.Name = PileSection.PHCOption.First();
            s.RecalculateSelectedPrecastPile();

            Assert.AreEqual(0.0, s.NodeDiameter, 1e-9, "節杭以外では節部径を残さない");
            Assert.AreEqual(0.0, s.CatalogMassPerM, 1e-9);
        }

        [TestMethod]
        public void SelectedProduct_IsRecognisedAsInLibrary()
        {
            var s = MakeNodularSection();
            Assert.IsTrue(s.IsSelectedPrecastPileInLibrary(),
                "ファイル読込時のライブラリ存在検証で節杭が未知扱いされてはいけない");
        }

        // ── 断面耐力は PHC杭 と同一 ────────────────────────────

        [TestMethod]
        public void SectionCalculator_IsPhcSection()
        {
            var s = MakeNodularSection();
            var calc = s.GetType()
                .GetMethod("CreateSectionCalculator", System.Reflection.BindingFlags.NonPublic
                                                    | System.Reflection.BindingFlags.Instance)!
                .Invoke(s, null);

            Assert.IsNotNull(calc, "null だと線形弾性フォールバックへ静かに落ちる");
            Assert.AreEqual("PHCSection", calc!.GetType().Name);
        }

        [TestMethod]
        public void MPhiCacheKey_IsDistinctFromPhcAndNotOther()
        {
            var nodular = MakeNodularSection();
            var phc = MakeEquivalentPhcSection();

            string keyN = nodular.GetMPhiCacheKey(1000.0);
            string keyP = phc.GetMPhiCacheKey(1000.0);

            Assert.IsFalse(keyN.StartsWith("OTHER|"),
                "キー未登録だと OTHER| に落ちて別断面とキャッシュが衝突する");
            Assert.IsTrue(keyN.StartsWith("NPH|"));
            Assert.AreNotEqual(keyP, keyN);
        }

        [TestMethod]
        public void NMCurve_MatchesEquivalentStraightPhc()
        {
            // 「断面性能は軸部基準」の実証。節部径は断面耐力に一切効かない。
            var nodular = MakeNodularSection();
            var phc = MakeEquivalentPhcSection();

            var (nN, mN) = nodular.UnfactoredUltimateNM;
            var (nP, mP) = phc.UnfactoredUltimateNM;

            Assert.AreEqual(nP.Count, nN.Count, "N-M 曲線の点数が一致しない");
            for (int i = 0; i < nP.Count; i++)
            {
                Assert.AreEqual(nP[i], nN[i], Math.Max(Math.Abs(nP[i]), 1.0) * 1e-9, $"N[{i}]");
                Assert.AreEqual(mP[i], mN[i], Math.Max(Math.Abs(mP[i]), 1.0) * 1e-9, $"M[{i}]");
            }
        }

        [TestMethod]
        public void NQCurve_MatchesEquivalentStraightPhc()
        {
            var nodular = MakeNodularSection();
            var phc = MakeEquivalentPhcSection();

            var (nN, qN) = nodular.UnfactoredUltimateNQ;
            var (nP, qP) = phc.UnfactoredUltimateNQ;

            Assert.AreEqual(nP.Count, nN.Count);
            for (int i = 0; i < nP.Count; i++)
                Assert.AreEqual(qP[i], qN[i], Math.Max(Math.Abs(qP[i]), 1.0) * 1e-9, $"Q[{i}]");
        }

        [TestMethod]
        public void ElasticStiffness_MatchesEquivalentStraightPhc()
        {
            var nodular = MakeNodularSection();
            var phc = MakeEquivalentPhcSection();

            Assert.AreEqual(phc.EA, nodular.EA, Math.Abs(phc.EA) * 1e-9, "EA は軸部断面で決まる");
            Assert.AreEqual(phc.EI, nodular.EI, Math.Abs(phc.EI) * 1e-9, "EI は軸部断面で決まる");
        }

        // ── 自重はカタログ標準質量による ───────────────────────

        [TestMethod]
        public void Weight_ComesFromCatalogMass()
        {
            var s = MakeNodularSection();
            var n = SampleProduct();

            Assert.AreEqual(n.MassPerM * UnitConversion.TON_TO_KN, s.W, 1e-9);
        }

        [TestMethod]
        public void Weight_IsHeavierThanEquivalentStraightPhc()
        {
            // 節の分だけ重い
            var nodular = MakeNodularSection();
            var phc = MakeEquivalentPhcSection();

            Assert.IsTrue(nodular.W > phc.W,
                $"節杭 {nodular.W:F4} kN/m はストレート PHC {phc.W:F4} kN/m より重いはず");
        }

        [TestMethod]
        public void Weight_IsNotAffectedByConcreteUnitWeight()
        {
            // カタログ質量を使う以上、基本設定の γc は節杭には効かない（意図した挙動）
            var s = MakeNodularSection();
            double before = s.W;

            s.ConcreteGamma = s.ConcreteGamma * 1.5;

            Assert.AreEqual(before, s.W, 1e-12,
                "節杭の自重はカタログ標準質量固定。γc 連動にしたなら本テストごと見直すこと");
        }

        [TestMethod]
        public void Weight_OfEveryProduct_IsPositiveAndPlausible()
        {
            foreach (var n in PileSection.NodularPiles)
            {
                double w = n.MassPerM * UnitConversion.TON_TO_KN;
                Assert.IsTrue(w > 0, $"{n.DisplayName}: 自重が 0 以下");
                Assert.IsTrue(w < 20.0, $"{n.DisplayName}: 自重 {w:F2} kN/m が過大");
            }
        }

        // ── 諸元表 ─────────────────────────────────────────────

        [TestMethod]
        public void Specification_ShowsNodeDiameterAndMassAndCaveat()
        {
            var s = MakeNodularSection();
            s.SetSpecs();

            var items = s.SelectedPileSectionSpecification.ToList();
            Assert.IsTrue(items.Any(x => x.Item == "節部径"), "諸元表に節部径が出ていない");
            Assert.IsTrue(items.Any(x => x.Item == "カタログ標準質量"), "諸元表に標準質量が出ていない");

            var nodeSpec = items.First(x => x.Item == "節部径");
            StringAssert.Contains(nodeSpec.Note ?? "", "周面抵抗",
                "周面抵抗が軸部径である旨の注記が無いと利用者が誤解する");
        }

        // ── 節位置（形状ではなく位置のみを描画するための算定）─────

        [TestMethod]
        public void NodeCenterDepths_FollowCatalogLayout()
        {
            var s = MakeNodularSection();
            Assert.AreEqual(1000.0, s.NodePitch, 1e-9);
            Assert.AreEqual(600.0, s.NodeHeadOffset, 1e-9);
            Assert.AreEqual(400.0, s.NodeToeOffset, 1e-9);

            // 杭長 10m: 上端 0.6m から 1m ピッチ、最終節は下端 0.4m 上
            var zs = s.NodeCenterDepthsFromSegmentTop(10.0).ToList();
            Assert.AreEqual(10, zs.Count, "杭長 L[m] の節数は L 個になる（600 + (L−1)×1000 + 400 = 1000L）");
            Assert.AreEqual(0.6, zs.First(), 1e-9);
            Assert.AreEqual(9.6, zs.Last(), 1e-9);
            for (int i = 1; i < zs.Count; i++)
                Assert.AreEqual(1.0, zs[i] - zs[i - 1], 1e-9);
        }

        [TestMethod]
        public void NodeCenterDepths_CountEqualsPileLengthInMetres()
        {
            // カタログの杭長 4〜15m (1m ピッチ) すべてで整合すること
            var s = MakeNodularSection();
            for (int lengthM = 4; lengthM <= 15; lengthM++)
            {
                var zs = s.NodeCenterDepthsFromSegmentTop(lengthM).ToList();
                Assert.AreEqual(lengthM, zs.Count, $"杭長 {lengthM}m の節数");
                Assert.AreEqual(lengthM - 0.4, zs.Last(), 1e-9, $"杭長 {lengthM}m の最終節位置");
                Assert.IsTrue(zs.Last() <= lengthM, "節が杭先端を越えている");
            }
        }

        [TestMethod]
        public void NodeCenterDepths_AreEmptyForStraightPhc()
        {
            var s = MakeEquivalentPhcSection();
            Assert.AreEqual(0, s.NodeCenterDepthsFromSegmentTop(10.0).Count(),
                "ストレート PHC 杭に節位置を描いてはいけない");
        }

        [TestMethod]
        public void NodeCenterDepths_HandleDegenerateLengths()
        {
            var s = MakeNodularSection();
            Assert.AreEqual(0, s.NodeCenterDepthsFromSegmentTop(0.0).Count());
            Assert.AreEqual(0, s.NodeCenterDepthsFromSegmentTop(-1.0).Count());
            // 節が 1 つも入らない短い区間
            Assert.AreEqual(0, s.NodeCenterDepthsFromSegmentTop(0.5).Count());
        }

        // ── 最上段区間では使えない ─────────────────────────────

        [TestMethod]
        public void TopSegment_DoesNotOfferNodularSectionType()
        {
            var s = new PileSection { PileBodyType = PileTypeNames.PrecastConcrete, IsTopSegment = true };
            CollectionAssert.DoesNotContain(s.PreCastConcretePileSectionTypeOption, PileTypeNames.PhcNodular,
                "PHC節杭 は上杭に継ぐ下杭なので最上段では選べない");
            CollectionAssert.Contains(s.PreCastConcretePileSectionTypeOption, PileTypeNames.Phc);
        }

        [TestMethod]
        public void NonTopSegment_OffersNodularSectionType()
        {
            var s = new PileSection { PileBodyType = PileTypeNames.PrecastConcrete, IsTopSegment = false };
            CollectionAssert.Contains(s.PreCastConcretePileSectionTypeOption, PileTypeNames.PhcNodular);
        }

        [TestMethod]
        public void PileBody_MarksOnlyTheFirstSegmentAsTop()
        {
            var body = MakeTwoSegmentBody(upperDiameter: 1100.0);
            body.PileBodySegmentsUpdate();

            Assert.IsTrue(body.PileBodySegments[0].PileSection.IsTopSegment);
            Assert.IsFalse(body.PileBodySegments[1].PileSection.IsTopSegment);
        }

        // ── 拡頭タイプの自動判定 ───────────────────────────────

        /// <summary>
        /// 上段 = ストレート PHC (径を指定), 下段 = 節杭 の杭体を作る。
        ///
        /// 注: <c>PileBodyInput.PileBodySegments</c> の setter は各区間に杭種を伝播して
        /// <c>ResetSectionProperties()</c> を呼ぶため、断面の設定は<b>区間を代入したあと</b>に行う。
        /// </summary>
        private static PileBodyInput MakeTwoSegmentBody(
            double upperDiameter, string nodularProduct = "NPH-1200-1100-標準-105-A")
        {
            var body = new PileBodyInput
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileBodySegments =
                [
                    new PileBodySegment { No = 1, SegmentLength = 10.0, PileSection = new PileSection() },
                    new PileBodySegment { No = 2, SegmentLength = 10.0, PileSection = new PileSection() },
                ],
            };

            var upper = body.PileBodySegments[0].PileSection;
            upper.PileBodyType = PileTypeNames.PrecastConcrete;
            upper.PileSectionType = PileTypeNames.Phc;
            upper.PileDiameter = upperDiameter;

            var lower = body.PileBodySegments[1].PileSection;
            lower.PileBodyType = PileTypeNames.PrecastConcrete;
            lower.PileSectionType = PileTypeNames.PhcNodular;
            lower.SelectedPrecastPile.Name = nodularProduct;
            lower.RecalculateSelectedPrecastPile();

            return body;
        }

        [TestMethod]
        public void HeadType_IsStandard_WhenUpperSegmentMatchesShaftDiameter()
        {
            // 上杭 φ1100 = 軸部径 → 拡頭不要
            var body = MakeTwoSegmentBody(upperDiameter: 1100.0);
            body.PileBodySegmentsUpdate();
            var lower = body.PileBodySegments[1].PileSection;

            Assert.AreEqual(PileSection.NodularHeadTypes.Standard, lower.NodularHeadType);
            Assert.AreEqual(0.0, lower.NodeHeadDiameter, 1e-9);
        }

        [TestMethod]
        public void HeadType_IsEnlarged_WhenUpperSegmentMatchesNodeDiameter()
        {
            // 上杭 φ1200 = 節部径 → 拡頭タイプ (Dt = Do)
            var body = MakeTwoSegmentBody(upperDiameter: 1200.0);
            body.PileBodySegmentsUpdate();
            var lower = body.PileBodySegments[1].PileSection;

            Assert.AreEqual(PileSection.NodularHeadTypes.EnlargedHead, lower.NodularHeadType);
            Assert.AreEqual(1200.0, lower.NodeHeadDiameter, 1e-9);
            Assert.AreEqual(600.0, lower.NodeHeadLength, 1e-9);
        }

        [TestMethod]
        public void HeadType_FallsBackToStandard_WhenNoMatchingHeadDiameter()
        {
            // φ1200-1100 に Dt=1150 の設定は無い → 標準に落とし、理由を残す
            var body = MakeTwoSegmentBody(upperDiameter: 1150.0);
            body.PileBodySegmentsUpdate();
            var lower = body.PileBodySegments[1].PileSection;

            Assert.AreEqual(PileSection.NodularHeadTypes.Standard, lower.NodularHeadType);
            StringAssert.Contains(lower.NodularHeadNote, "一致する拡頭径がありません");
        }

        [TestMethod]
        public void HeadType_IsIntermediate_ForAnIntermediateCatalogOption()
        {
            // φ800-600 は Dt=700 (中間径) と Dt=800 (拡頭) の 2 択
            var body = MakeTwoSegmentBody(upperDiameter: 700.0, nodularProduct: "NPH-800-600-標準-105-A");
            body.PileBodySegmentsUpdate();
            var lower = body.PileBodySegments[1].PileSection;

            Assert.AreEqual(PileSection.NodularHeadTypes.IntermediateHead, lower.NodularHeadType);
            Assert.AreEqual(700.0, lower.NodeHeadDiameter, 1e-9);
        }

        // ── 外形（姿図・3D 共通の定義）─────────────────────────

        [TestMethod]
        public void NodularOutline_StartsAndEndsAtShaftRadius()
        {
            var s = MakeNodularSection();
            var outline = s.NodularOutline(10.0);

            Assert.IsTrue(outline.Count > 0);
            Assert.AreEqual(0.0, outline[0].Depth, 1e-9);
            Assert.AreEqual(s.PileDiameter * 0.5, outline[0].Radius, 1e-9, "上端は軸部径");
            Assert.AreEqual(10.0, outline[^1].Depth, 1e-9);
            Assert.AreEqual(s.PileDiameter * 0.5, outline[^1].Radius, 1e-9, "下端は軸部径");
        }

        [TestMethod]
        public void NodularOutline_ReachesNodeRadiusAndNeverExceedsIt()
        {
            var s = MakeNodularSection();
            var outline = s.NodularOutline(10.0);

            double rNode = s.NodeDiameter * 0.5;
            Assert.IsTrue(outline.Any(p => System.Math.Abs(p.Radius - rNode) < 1e-9), "節部径に到達していない");
            foreach (var p in outline)
            {
                Assert.IsTrue(p.Radius <= rNode + 1e-9, $"節部径を超えている ({p.Radius})");
                Assert.IsTrue(p.Depth >= -1e-9 && p.Depth <= 10.0 + 1e-9, "区間外の点がある");
            }
        }

        [TestMethod]
        public void NodularOutline_IsMonotonicInDepth()
        {
            // 姿図はこの列を折れ線として辿るので、深さが逆行すると外形が破綻する
            var s = MakeNodularSection();
            var outline = s.NodularOutline(12.0);
            for (int i = 1; i < outline.Count; i++)
                Assert.IsTrue(outline[i].Depth >= outline[i - 1].Depth - 1e-9,
                    $"深さが逆行している ({outline[i - 1].Depth} → {outline[i].Depth})");
        }

        [TestMethod]
        public void NodularOutline_UsesHeadDiameterOverTheHeadLength()
        {
            var body = MakeTwoSegmentBody(upperDiameter: 1200.0);
            body.PileBodySegmentsUpdate();
            var lower = body.PileBodySegments[1].PileSection;

            var outline = lower.NodularOutline(10.0);
            Assert.AreEqual(lower.NodeHeadDiameter * 0.5, outline[0].Radius, 1e-9,
                "拡頭タイプの上端は拡頭部径");

            // 拡頭部の範囲 (0 〜 Lt) では拡頭部径を下回らない
            double headBottom = lower.NodeHeadLength / 1000.0;
            foreach (var p in outline.Where(p => p.Depth <= headBottom + 1e-9))
                Assert.IsTrue(p.Radius >= lower.NodeHeadDiameter * 0.5 - 1e-9,
                    $"拡頭部の範囲で径が細くなっている (深さ {p.Depth})");
        }

        [TestMethod]
        public void Specification_OfStraightPhc_HasNoNodularRows()
        {
            var s = MakeEquivalentPhcSection();
            s.SetSpecs();

            var items = s.SelectedPileSectionSpecification.ToList();
            Assert.IsFalse(items.Any(x => x.Item == "節部径"),
                "ストレート PHC 杭の諸元表に節杭固有の行が出てはいけない");
        }
    }
}
