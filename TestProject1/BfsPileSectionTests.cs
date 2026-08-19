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
    /// BF.S (頭部厚型節付き杭) を杭断面として選んだときの挙動を固定する。
    ///
    /// 1 本の杭が<b>外径の違う 2 つの軸部</b>から成るので、杭区間ごとに
    /// 頭部軸部 / 先端軸部 を選び分ける。断面耐力はどちらも PHC杭 と同じ計算で、
    /// <b>先端軸部はカタログに耐力の記載が無いため、ここで計算した値がそのまま設計値になる</b>。
    /// </summary>
    [TestClass]
    public class BfsPileSectionTests
    {
        /// <summary>φ400-3045 Fc105 A2種 (頭部 D=400/t=105、先端 D=300/t=55、節部 450)</summary>
        private const string SampleHeadName = "BF.S-400-3045-105-A2";
        private const string SampleTipName = SampleHeadName + "-先端軸部";

        private static BfsPile SampleProduct() =>
            PileSection.BfsPiles.First(p => p.DisplayName == SampleHeadName);

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

        private static PileSection MakeHead() => MakeSection(PileTypeNames.BfsHead, SampleHeadName);
        private static PileSection MakeTip() => MakeSection(PileTypeNames.BfsTip, SampleTipName);

        private static object? Calculator(PileSection s) =>
            s.GetType()
             .GetMethod("CreateSectionCalculator", System.Reflection.BindingFlags.NonPublic
                                                 | System.Reflection.BindingFlags.Instance)!
             .Invoke(s, null);

        // ── 分類 ───────────────────────────────────────────────────

        [TestMethod]
        public void BothSectionTypes_AreAcceptedAndClassified()
        {
            foreach (var type in new[] { PileTypeNames.BfsHead, PileTypeNames.BfsTip })
            {
                var s = new PileSection { PileBodyType = PileTypeNames.PrecastConcrete };
                s.PileSectionType = type;

                Assert.AreEqual(type, s.PileSectionType, "validTypes への登録漏れ");
                Assert.IsTrue(s.IsNodularPile, $"{type}: 節を持つ断面として扱われない");
                Assert.IsTrue(s.IsHollowPrecastSection, $"{type}: 中空既製断面として扱われない");
                Assert.IsTrue(PileTypeNames.IsPhcLikeSection(type), $"{type}: PHC杭 系として扱われない");
            }
        }

        [TestMethod]
        public void BothSectionTypes_AreOfferedRegardlessOfSegmentPosition()
        {
            // 区間の位置で選択肢を変えると、同じ状態でも「初回は選べるが開き直すと消える」
            // という不安定な挙動になっていたため、位置による除外はしない
            foreach (bool isTop in new[] { false, true })
            {
                var s = new PileSection { IsTopSegment = isTop };
                CollectionAssert.Contains(s.PreCastConcretePileSectionTypeOption, PileTypeNames.BfsHead, $"IsTopSegment={isTop}");
                CollectionAssert.Contains(s.PreCastConcretePileSectionTypeOption, PileTypeNames.BfsTip, $"IsTopSegment={isTop}");
            }
        }

        [TestMethod]
        public void ProductOptions_ExposeEveryCatalogProductOnce()
        {
            Assert.AreEqual(54, PileSection.BfsPiles.Count);
            Assert.AreEqual(54, PileSection.BfsHeadOption.Count);
            Assert.AreEqual(54, PileSection.BfsTipOption.Count);
            CollectionAssert.Contains(PileSection.BfsHeadOption.ToList(), SampleHeadName);
            CollectionAssert.Contains(PileSection.BfsTipOption.ToList(), SampleTipName);

            var overlap = PileSection.BfsHeadOption.Intersect(PileSection.BfsTipOption).ToList();
            Assert.AreEqual(0, overlap.Count, $"表示名が重複: {string.Join(", ", overlap.Take(3))}");
        }

        [TestMethod]
        public void PileSectionType_SurvivesJsonRoundTrip()
        {
            foreach (var s in new[] { MakeHead(), MakeTip() })
            {
                var restored = JsonSerializer.Deserialize<PileSection>(JsonSerializer.Serialize(s));

                Assert.IsNotNull(restored);
                Assert.AreEqual(s.PileSectionType, restored!.PileSectionType);
                Assert.AreEqual(s.SelectedPrecastPile.Name, restored.SelectedPrecastPile.Name);
                Assert.AreEqual(s.PileDiameter, restored.PileDiameter, 1e-9);
                Assert.AreEqual(s.NodeDiameter, restored.NodeDiameter, 1e-9);
                Assert.AreEqual(s.CatalogNodeFlatLength, restored.CatalogNodeFlatLength, 1e-9);
            }
        }

        [TestMethod]
        public void SelectedProduct_IsRecognisedAsInLibrary()
        {
            foreach (var s in new[] { MakeHead(), MakeTip() })
                Assert.IsTrue(s.IsSelectedPrecastPileInLibrary(), $"{s.PileSectionType}");
        }

        // ── 製品の転記 ─────────────────────────────────────────────

        [TestMethod]
        public void SelectingProduct_TransfersTheShaftOfThatPart()
        {
            var n = SampleProduct();

            var head = MakeHead();
            Assert.AreEqual(n.HeadDia, head.PileDiameter, 1e-9, "頭部軸部径");
            Assert.AreEqual(n.HeadThickness, head.ConcreteThickness, 1e-9, "頭部肉厚");
            Assert.AreEqual(n.HeadSigmaCe, head.Prestress, 1e-9, "頭部 σce");

            var tip = MakeTip();
            Assert.AreEqual(n.TipDia, tip.PileDiameter, 1e-9, "先端軸部径");
            Assert.AreEqual(n.TipThickness, tip.ConcreteThickness, 1e-9, "先端肉厚");
            Assert.AreEqual(n.TipSigmaCe, tip.Prestress, 1e-9, "先端 σce (算定値)");

            // PC 鋼棒は両軸部共通
            foreach (var s in new[] { head, tip })
            {
                Assert.AreEqual(n.Ap, s.TendonAp, 1e-9);
                Assert.AreEqual(n.Pcd, s.TendonDp, 1e-9);
                Assert.AreEqual(n.Fc, s.ConcreteFc, 1e-9);
            }
            // 内径は両軸部で同じ
            Assert.AreEqual(head.PileDiameter - 2 * head.ConcreteThickness,
                            tip.PileDiameter - 2 * tip.ConcreteThickness, 1e-9);
        }

        [TestMethod]
        public void SelectingProduct_TransfersNodeGeometryOfThatPart()
        {
            var n = SampleProduct();

            foreach (var s in new[] { MakeHead(), MakeTip() })
            {
                Assert.AreEqual(n.NodeDia, s.NodeDiameter, 1e-9, "節部径は両部位共通");
                Assert.AreEqual(n.NodePitch, s.NodePitch, 1e-9);
                Assert.AreEqual(n.ToeOffset, s.NodeToeOffset, 1e-9);
                Assert.AreEqual(n.NodeFlatLength, s.NodeFlatLength, 1e-9, "節部長さはカタログ寸法");
            }

            // 節の出寸法は軸部径で決まるので部位ごとに違う。テーパーは 45°。
            var head = MakeHead();
            var tip = MakeTip();
            Assert.AreEqual((n.NodeDia - n.HeadDia) / 2, head.NodeRadialRise, 1e-9);
            Assert.AreEqual((n.NodeDia - n.TipDia) / 2, tip.NodeRadialRise, 1e-9);
            Assert.AreEqual(head.NodeRadialRise, head.NodeTaperLength, 1e-9, "テーパーは 45°");
            Assert.IsTrue(tip.NodeRadialRise > head.NodeRadialRise, "先端軸部の方が節が大きく出る");

            // カタログ寸法の 25(50)/75(100)/25(50) と一致すること
            Assert.AreEqual(25.0, head.NodeTaperLength, 1e-9);
            Assert.AreEqual(75.0, head.NodeFlatLength, 1e-9);
            Assert.AreEqual(75.0, tip.NodeTaperLength, 1e-9);
        }

        [TestMethod]
        public void NodeFlatLength_UsesCatalogValueNotTheEstimate()
        {
            // 節杭 (JP-NPH) は寸法記入が無いので推定値 (= テーパー長)、
            // BF.S は寸法記入があるのでカタログ値。頭部軸部では両者が食い違う。
            var head = MakeHead();
            Assert.AreEqual(75.0, head.CatalogNodeFlatLength, 1e-9);
            Assert.AreNotEqual(head.NodeRadialRise, head.NodeFlatLength,
                "カタログ寸法ではなく推定値 (テーパー長) が使われている");

            // 節杭側は従来どおり推定値のまま
            var nph = MakeSection(PileTypeNames.PhcNodular, "NPH-440-300-標準-85-A");
            Assert.AreEqual(0.0, nph.CatalogNodeFlatLength, 1e-9);
            Assert.AreEqual(nph.NodeRadialRise, nph.NodeFlatLength, 1e-9);
        }

        [TestMethod]
        public void SwitchingAwayFromBfs_ClearsNodeGeometry()
        {
            var s = MakeHead();
            Assert.IsTrue(s.NodeDiameter > 0 && s.CatalogNodeFlatLength > 0);

            s.PileSectionType = PileTypeNames.Phc;
            s.SelectedPrecastPile.Name = PileSection.PHCOption.First();
            s.RecalculateSelectedPrecastPile();

            Assert.AreEqual(0.0, s.NodeDiameter, 1e-9);
            Assert.AreEqual(0.0, s.CatalogNodeFlatLength, 1e-9);
        }

        [TestMethod]
        public void EnlargedHead_IsNotOfferedForBfs()
        {
            // 頭部軸部そのものが太いので「拡頭」の設定は無い。
            // 直上区間が太くても拡頭を探しに行かず、標準タイプのまま理由を残す。
            var s = MakeTip();
            s.ResolveNodularHead(400.0);

            Assert.AreEqual(0.0, s.NodeHeadDiameter, 1e-9);
            Assert.AreEqual(PileSection.NodularHeadTypes.Standard, s.NodularHeadType);
            StringAssert.Contains(s.NodularHeadNote, "拡頭の設定はありません");
            // 入力チェックの警告文言 (拡頭径の不一致) には該当させない
            Assert.IsFalse(s.NodularHeadNote.Contains("一致する拡頭径がありません"));
        }

        // ── 断面耐力 ───────────────────────────────────────────────

        [TestMethod]
        public void SectionCalculator_IsPhcSectionForBothParts()
        {
            Assert.AreEqual("PHCSection", Calculator(MakeHead())?.GetType().Name);
            Assert.AreEqual("PHCSection", Calculator(MakeTip())?.GetType().Name,
                "null だと線形弾性フォールバックへ静かに落ちる");
        }

        [TestMethod]
        public void MPhiCacheKeys_AreDistinctAndRegistered()
        {
            string keyHead = MakeHead().GetMPhiCacheKey(1000.0);
            string keyTip = MakeTip().GetMPhiCacheKey(1000.0);

            Assert.IsTrue(keyHead.StartsWith("BFS-HEAD|"), keyHead);
            Assert.IsTrue(keyTip.StartsWith("BFS-TIP|"), keyTip);
            Assert.AreNotEqual(keyHead, keyTip);
        }

        [TestMethod]
        public void HeadIsStifferAndStrongerThanTip()
        {
            var head = MakeHead();
            var tip = MakeTip();

            Assert.IsTrue(head.EA > tip.EA, "頭部軸部の方が軸剛性が大きい");
            Assert.IsTrue(head.EI > tip.EI, "頭部軸部の方が曲げ剛性が大きい");
            Assert.IsTrue(head.UnfactoredUltimateNM.M.Max() > tip.UnfactoredUltimateNM.M.Max(),
                "頭部軸部の方が曲げ耐力が大きい");
        }

        [TestMethod]
        public void HeadUltimateMoment_IsInTheRangeOfTheCatalogValue()
        {
            // カタログの破壊モーメント Mu (軸力 0 時) と、アプリの N-M 曲線の
            // 軸力 0 における安全限界モーメントを突き合わせる。
            // 断面計算の規準が違うので一致はしないが、実測では全製品で +2〜7% (計算が安全側でない側)
            // に収まる。ここが崩れたら断面諸元の転記か PCD 逆算を疑うこと。
            // 先端軸部はカタログに Mu が無いので、この突合が唯一の外部照合になる。
            foreach (var product in PileSection.BfsPiles.Where(p => p.Fc == 105))
            {
                var s = MakeSection(PileTypeNames.BfsHead, product.DisplayName);
                var (ns, ms) = s.UnfactoredUltimateNM;
                double mAtZero = InterpolateAtZeroAxial(ns, ms);

                double ratio = mAtZero / product.HeadMu;
                Assert.IsTrue(ratio > 0.98 && ratio < 1.12,
                    $"{product.DisplayName}: 計算 {mAtZero:F0} kN·m / カタログ Mu {product.HeadMu} kN·m = {ratio:F2}");
            }
        }

        private static double InterpolateAtZeroAxial(System.Collections.Generic.List<double> ns,
                                                     System.Collections.Generic.List<double> ms)
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

        // ── 自重 ───────────────────────────────────────────────────

        [TestMethod]
        public void Weight_FallsBackToVolumeBecauseCatalogHasNoMassTable()
        {
            // このカタログには標準質量表が無い。節杭 (JP-NPH) と違い自重は軸部体積による
            // (= 節の分は未計上)。ここが変わったら諸元表・ヘルプの注記も見直すこと。
            var head = MakeHead();
            Assert.AreEqual(0.0, head.CatalogMassPerM, 1e-9, "カタログ標準質量は存在しない");
            Assert.IsTrue(head.W > 0);

            double before = head.W;
            head.ConcreteGamma *= 1.5;
            Assert.IsTrue(head.W > before, "自重が単位体積重量に連動していない");
        }
    }
}
