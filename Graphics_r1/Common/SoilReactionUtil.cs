using PileDesign.FEM;

namespace PileDesign.Common
{
    /// <summary>
    /// 水平地盤ばね (HorizontalSoilSpring) の単位長さ当たり反力 (kN/m) 計算用ユーティリティ。
    /// 分担長は FEM 上の実梁長 (Beam.NodeI - Beam.NodeJ 距離) を集計して求める。
    /// 旧実装の HorizontalSoilReactionItem.ZTop/ZBtm ベースだと、要素分割時に
    /// 元の反力区間長を共有して二重計上する恐れがあるため、本実装に統一する。
    /// </summary>
    public static class SoilReactionUtil
    {
        /// <summary>
        /// 節点 <paramref name="node"/> の分担長 (m) を返す。
        ///   接続する FEM 梁要素のうち HorizontalSoilReactionItem が設定されているもののみを対象に、
        ///   各梁の物理長 L = |Beam.NodeI.Coord - Beam.NodeJ.Coord| の半分 (L/2) を合計する。
        ///   - 内部節点 (上下に梁があるケース): (L_上 + L_下) / 2
        ///   - 端部節点 (片側のみ):              L_片側 / 2
        ///   - 反力アイテムが付かない梁は除外 (杭頭リジッドリンク等)。
        /// </summary>
        public static double GetNodeTributaryLength(Node node, AnaModel anaModel)
        {
            if (node == null || anaModel?.Beams == null) return 0;
            double tributary = 0;
            foreach (var beam in anaModel.Beams)
            {
                if (beam?.NodeI == null || beam.NodeJ == null) continue;
                if (beam.HorizontalSoilReactionItem == null) continue;
                if (beam.NodeI != node && beam.NodeJ != node) continue;

                double dx = beam.NodeI.Coord.X - beam.NodeJ.Coord.X;
                double dy = beam.NodeI.Coord.Y - beam.NodeJ.Coord.Y;
                double dz = beam.NodeI.Coord.Z - beam.NodeJ.Coord.Z;
                double L = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (L > 0) tributary += L * 0.5;
            }
            return tributary;
        }
    }
}
