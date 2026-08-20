using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Constants;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// 杭先端の拡大根固め部の位置を固定する。
    ///
    /// 同じ形を 杭姿図 / 擬似 3D / Viewport3D / DXF / 3dm の 5 箇所で描いており、
    /// 工法ごとの上端・下端の決め方が散らばると表示が食い違う。実際に次の食い違いがあった。
    ///   - Viewport3D が拡大根固め杭を「拡底コーン」として描いていた（姿図は円柱）
    ///   - DXF / 3dm が球根を杭先端から下向きに描いていた（姿図・3D は上向き）
    ///   - Viewport3D / DXF / 3dm が Hybrid ニーディングを知らなかった
    ///   - 拡底部の立上り・側面角度が 0.3m / 12° のハードコードで入力が効かなかった
    /// </summary>
    [TestClass]
    public class PileToeShapeTests
    {
        private static PileBodyInput Body(string constructionType) => new()
        {
            PileBodyType = PileTypeNames.PrecastConcrete,
            PileConstructionType = constructionType,
            PileToeDia = 1500,
            PrecastConcretePileToeHeightRatio = 2.0,
        };

        [TestMethod]
        public void Preboring_BulbGrowsUpwardFromTheToe()
        {
            var body = Body(PileConstructionTypeNames.Preboring);

            var (above, below) = PileToeShape.BulbExtent(body);
            Assert.AreEqual(1.5 * 2.0, above, 1e-9, "高さは 根固め部径 × 高さ径比");
            Assert.AreEqual(0.0, below, 1e-9, "杭先端より下には出ない");

            var (topZ, bottomZ) = PileToeShape.BulbRange(body, pileToeZ: -20.0);
            Assert.AreEqual(-17.0, topZ, 1e-9);
            Assert.AreEqual(-20.0, bottomZ, 1e-9, "球根の下端が杭先端と一致していない");
        }

        [TestMethod]
        public void SmartMagnum_ExtendsBelowTheToeByLL()
        {
            var body = Body(PileConstructionTypeNames.SmartMagnum);
            body.SmartMagnumLL = 1.0;

            var (above, below) = PileToeShape.BulbExtent(body);
            Assert.AreEqual(SoilPile.SmartMagnumBulbTopAboveToe, above, 1e-9, "上端は杭先端の 2m 上");
            Assert.AreEqual(1.0, below, 1e-9, "杭下拡大根固め部 LL が下に出ていない");
        }

        [DataTestMethod]
        [DataRow(1.5, 2.0)]   // e ≦ 1.6 → 2m
        [DataRow(1.8, 3.0)]   // e ≧ 1.7 → 3m
        public void HybridKneading_BulbTopFollowsTheExpansionRatio(double e, double expectedAboveEvaluation)
        {
            var body = Body(PileConstructionTypeNames.HybridKneading);
            body.HybridExpansionRatio = e;
            body.HybridPileBelowLength = 0.5;

            var (above, below) = PileToeShape.BulbExtent(body);
            Assert.AreEqual(0.5 + expectedAboveEvaluation, above, 1e-9,
                "上端が「先端支持力算定位置 (杭先端の Lu 上) のさらに 2m または 3m 上」になっていない");
            Assert.AreEqual(0.0, below, 1e-9, "Hybrid は杭先端より下には出ない");
        }

        [TestMethod]
        public void HighCapacityMethods_AlwaysHaveABulbEvenIfTheDiameterIsNotLarger()
        {
            // 高支持力杭工法は根固め部が必ず存在する。径の大小で描画を止めない
            foreach (string t in new[]
            {
                PileConstructionTypeNames.SmartMagnum,
                PileConstructionTypeNames.HybridKneading,
            })
            {
                var body = Body(t);
                Assert.IsTrue(PileToeShape.HasBulb(body, shaftDia: 2.0), t);
            }
        }

        [TestMethod]
        public void ConventionalMethods_NeedALargerToeDiameter()
        {
            var body = Body(PileConstructionTypeNames.Preboring);

            Assert.IsTrue(PileToeShape.HasBulb(body, shaftDia: 1.0), "根固め部径 > 軸部径 なら描く");
            Assert.IsFalse(PileToeShape.HasBulb(body, shaftDia: 2.0), "拡大が無ければ描かない");
        }

        [TestMethod]
        public void MethodsWithoutABulb_AreExcluded()
        {
            foreach (string t in new[]
            {
                PileConstructionTypeNames.Insitu,
                PileConstructionTypeNames.Driven,
                PileConstructionTypeNames.Rotary,
            })
            {
                Assert.IsFalse(PileToeShape.HasBulb(Body(t), shaftDia: 0.5), t);
            }
        }
    }
}
