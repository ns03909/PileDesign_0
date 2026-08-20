using PileDesign.Constants;
using PileDesign.Models.InputData;

namespace PileDesign.Common
{
    /// <summary>
    /// 杭先端の拡大根固め部（ソイルセメント球根）の位置。
    ///
    /// 同じ形を 杭姿図 / 擬似 3D / Viewport3D / DXF / 3dm の 5 箇所で描いており、
    /// 工法ごとの上端・下端の決め方が散らばると表示が食い違う
    /// （実際、DXF と 3dm は球根を杭先端から<b>下向き</b>に描いており、
    ///  姿図・3D の上向きと逆になっていた）。ここに集約する。
    ///
    /// 球根は杭先端を下端として上方へ伸ばすのが基本で、杭体の最下部が球根に埋まる表現になる。
    /// Smart-MAGNUM だけは杭下拡大根固め部が杭先端より下に LL だけ出る。
    /// </summary>
    public static class PileToeShape
    {
        /// <summary>
        /// 拡大根固め部の上端・下端の高さ（杭先端からの相対値 m、上を正）。
        /// 呼び出し側は杭先端の Z にこれを足す。
        /// </summary>
        public static (double AboveToe, double BelowToe) BulbExtent(PileBodyInput body)
        {
            if (body == null) return (0, 0);

            if (PileConstructionTypeNames.IsSmartMagnum(body.PileConstructionType))
                return (SoilPile.SmartMagnumBulbTopAboveToe, body.SmartMagnumLL);

            if (PileConstructionTypeNames.IsHybridKneading(body.PileConstructionType))
            {
                // 根固め部上端 = 先端支持力算定位置（杭先端の Lu 上）のさらに 2m
                // （設計拡径比 e が 1.7 以上なら 3m）上。杭先端より下には出ない。
                double above = body.HybridPileBelowLength
                    + (body.HybridExpansionRatio >= SoilPile.HybridLargeExpansionThreshold ? 3.0 : 2.0);
                return (above, 0);
            }

            // 既存の埋込み杭: 根固め部径 × 高さ径比
            return (body.PileToeDia / 1000.0 * body.PrecastConcretePileToeHeightRatio, 0);
        }

        /// <summary>拡大根固め部の上端・下端の Z。</summary>
        public static (double TopZ, double BottomZ) BulbRange(PileBodyInput body, double pileToeZ)
        {
            var (above, below) = BulbExtent(body);
            return (pileToeZ + above, pileToeZ - below);
        }

        /// <summary>
        /// この杭体に拡大根固め部を描くか。
        /// 高支持力杭工法は根固め部が必ず存在するので、径の大小によらず描く。
        /// </summary>
        public static bool HasBulb(PileBodyInput body, double shaftDia)
        {
            if (body == null) return false;
            if (!PileConstructionTypeNames.HasEnlargedBulb(body.PileConstructionType)) return false;

            return PileConstructionTypeNames.IsHighCapacityMethod(body.PileConstructionType)
                || body.PileToeDia / 1000.0 > shaftDia;
        }
    }
}
