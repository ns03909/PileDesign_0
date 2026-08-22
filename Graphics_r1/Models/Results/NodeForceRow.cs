using PileDesign.FEM;

namespace PileDesign.Models.Results
{
    public sealed class NodeForceRow
    {
        [ResultColumn("NodeIdx", 0, tooltip: "節点の通し番号（1 始まり）")] public int NodeIndex { get; init; }
        [ResultColumn("NodeName", 1, tooltip: "節点の名称")] public string NodeName { get; init; } = "";

        // 座標
        [ResultColumn("X(m)", 2, "N3", "節点の全体座標系 X 座標（水平）")] public double X { get; init; }
        [ResultColumn("Y(m)", 3, "N3", "節点の全体座標系 Y 座標（水平）")] public double Y { get; init; }
        [ResultColumn("Z(m)", 4, "N3", "節点の全体座標系 Z 座標（鉛直、上向きが正）")] public double Z { get; init; }

        // 反力 (kN / kNm)
        [ResultColumn("Rx(kN)", 10, "N2", "全体座標系 X 方向の節点反力。水平合成は √(Rx²+Ry²)")] public double Fx { get; init; }
        [ResultColumn("Ry(kN)", 11, "N2", "全体座標系 Y 方向の節点反力。水平合成は √(Rx²+Ry²)")] public double Fy { get; init; }
        [ResultColumn("Rz(kN)", 12, "N2", "全体座標系 Z 方向（鉛直）の節点反力")] public double Fz { get; init; }
        [ResultColumn("Mx(kNm)", 13, "N2", "全体座標系 X 軸まわりのモーメント反力。回転剛性を持つばねの節点以外はほぼ 0")] public double Mx { get; init; }
        [ResultColumn("My(kNm)", 14, "N2", "全体座標系 Y 軸まわりのモーメント反力。回転剛性を持つばねの節点以外はほぼ 0")] public double My { get; init; }
        [ResultColumn("Mz(kNm)", 15, "N2", "全体座標系 Z 軸（鉛直軸）まわりのモーメント反力")] public double Mz { get; init; }

        public static NodeForceRow From(int index, Node node, NodeReaction reaction)
        {
            return new NodeForceRow
            {
                NodeIndex = index,
                NodeName = node.Name,
                X = node.Coord.X,
                Y = node.Coord.Y,
                Z = node.Coord.Z,
                Fx = reaction.Fx,
                Fy = reaction.Fy,
                Fz = reaction.Fz,
                Mx = reaction.Mx,
                My = reaction.My,
                Mz = reaction.Mz
            };
        }
    }
}