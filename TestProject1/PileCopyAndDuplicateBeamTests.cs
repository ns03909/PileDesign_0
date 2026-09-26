using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 杭のコピーと基礎梁の重複削除 (2026-09-27 のレビュー)。
    /// - 杭のコピーが座標と群杭係数・杭間隔比しか渡さず、杭体番号・地盤番号・ΔZc・軸力などが既定値に戻っていた
    /// - 重複梁の削除が両端の節点だけで判断し、断面・材料の違う梁まで消していた
    /// </summary>
    [TestClass]
    public class PileCopyAndDuplicateBeamTests
    {
        private static PileLayoutDataItem Source()
        {
            var pile = new PileLayoutDataItem
            {
                No = 3, PileNo = 3, X = 1, Y = 2, Z = -1.5,
                PileBodyNo = 2, GroundNo = 3, FoundationBeamDeltaZc = 0.4,
                GroupPileFactor = 0.8, PileSpacingFactor = 3.5,
                AxialForceVL0 = 1200, AxialForceVLAdditional = 50,
                SoilPileAltNo = 5, SinglePileSettlementVL = 7.5, Rf = 100, Rp = 200, Ru = 300,
                AxialForce = 999, AxialForceIncrement = 9, LinkedPileNo = 3, IsSelected = true,
            };
            pile.AxialForceLevel1s[1] = 1500;
            pile.AxialForceLevel2s[2] = 1800;
            pile.IsFrontPiles[0] = false;
            pile.SinglePileSettlementLevel1s[0] = 4.0;
            return pile;
        }

        [TestMethod]
        public void ACopiedPileKeepsItsInputConditions()
        {
            var source = Source();
            var copy = PileLayoutService.CopyForNewPile(source, 5, 0, 0);

            Assert.AreEqual((2, 3, 0.4, 0.8, 3.5), (copy.PileBodyNo, copy.GroundNo, copy.FoundationBeamDeltaZc, copy.GroupPileFactor, copy.PileSpacingFactor),
                "杭体番号・地盤番号・ΔZc・群杭係数・杭間隔比が引き継がれていません");
            Assert.AreEqual((1200.0, 50.0), (copy.AxialForceVL0, copy.AxialForceVLAdditional), "長期軸力が引き継がれていません");
            Assert.AreEqual(1500, copy.AxialForceLevel1s[1], "地震時軸力 (レベル1) が引き継がれていません");
            Assert.AreEqual(1800, copy.AxialForceLevel2s[2], "地震時軸力 (レベル2) が引き継がれていません");
            Assert.IsFalse(copy.IsFrontPiles[0], "前方杭の判定が引き継がれていません");
            Assert.AreNotSame(source.AxialForceLevel1s, copy.AxialForceLevel1s, "軸力の一覧を元の杭と共有しています");
        }

        [TestMethod]
        public void ACopiedPileGetsItsOwnIdentityPositionAndNoResults()
        {
            var source = Source();
            var copy = PileLayoutService.CopyForNewPile(source, 5, -1, 0.5);

            Assert.AreNotEqual(source.UniqueId, copy.UniqueId, "固有 ID を元の杭と共有しています");
            Assert.AreEqual((0, 0), (copy.No, copy.PileNo), "番号は呼び出し側が振り直すので 0 にしておきます");
            Assert.IsNull(copy.LinkedPileNo);
            Assert.IsFalse(copy.IsSelected);
            Assert.AreEqual((6.0, 1.0, -1.0), (copy.X, copy.Y, copy.Z));

            Assert.AreEqual(0, copy.SoilPileAltNo, "土層-杭セットとの対応は作り直しで付けます (元の杭の対応を写さない)");
            Assert.AreEqual((0.0, 0.0, 0.0, 0.0), (copy.SinglePileSettlementVL, copy.Rf, copy.Rp, copy.Ru),
                "元の杭の位置で求めた沈下・支持力の結果を写しています");
            Assert.IsTrue(copy.SinglePileSettlementLevel1s.All(v => v == 0));
            Assert.AreEqual((0.0, 0.0), (copy.AxialForce, copy.AxialForceIncrement), "解析中の軸力を写しています");
        }

        [TestMethod]
        public void CopySelectedPilesUsesTheFullCopy()
        {
            var source = Source();
            var list = new ObservableCollection<PileLayoutDataItem> { source };
            var result = new PileLayoutService().CopySelectedPiles(list, 2, 0, 0, repetitionNumber: 2, _ => { });

            Assert.AreEqual(3, result.Count);
            var copies = result.Skip(1).ToList();
            CollectionAssert.AreEqual(new[] { 3.0, 5.0 }, copies.Select(c => c.X).ToList());
            Assert.IsTrue(copies.All(c => c.PileBodyNo == 2 && c.GroundNo == 3 && c.AxialForceVL0 == 1200),
                "コピーした杭の杭体番号・地盤番号・軸力が元の杭と違います");
            Assert.AreEqual(2, copies.Select(c => c.UniqueId).Distinct().Count());
        }

        // ── 基礎梁の重複 ───────────────────────────────

        private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

        private static FoundationBeam Beam(Guid i, Guid j, int section = 1, int material = 1) => new()
        {
            NodeI_Type = NodeReferenceType.FoundationNode, NodeI_Id = i,
            NodeJ_Type = NodeReferenceType.FoundationNode, NodeJ_Id = j,
            SectionNo = section, MaterialNo = material,
        };

        [TestMethod]
        public void IdenticalBeamsAreRemovedEvenWhenReversed()
        {
            var first = Beam(A, B);
            var reversed = Beam(B, A);
            var other = Beam(B, C);
            var (remove, differing) = MainWindowViewModel.FindDuplicateBeams([first, reversed, other]);

            CollectionAssert.AreEqual(new[] { reversed }, remove, "向きの逆な同じ梁を重複として消していません");
            Assert.AreEqual(0, differing.Count);
        }

        [TestMethod]
        public void BeamsWithDifferentSectionOrMaterialAreKeptAndReported()
        {
            var first = Beam(A, B, section: 1, material: 1);
            var otherSection = Beam(A, B, section: 2, material: 1);
            var otherMaterial = Beam(B, A, section: 1, material: 3);
            var sameAsSecond = Beam(A, B, section: 2, material: 1);
            var (remove, differing) = MainWindowViewModel.FindDuplicateBeams([first, otherSection, otherMaterial, sameAsSecond]);

            CollectionAssert.AreEqual(new[] { sameAsSecond }, remove, "断面・材料の違う梁まで消しています");
            Assert.AreEqual(2, differing.Count, "断面・材料の違う梁を確認の対象として返していません");
            Assert.IsTrue(differing.All(d => d.Kept == first));
            CollectionAssert.AreEquivalent(new[] { otherSection, otherMaterial }, differing.Select(d => d.Other).ToList());
        }
    }
}
