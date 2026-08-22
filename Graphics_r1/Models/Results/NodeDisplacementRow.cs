using PileDesign.FEM;

namespace PileDesign.Models.Results
{
    public sealed class NodeDisplacementRow
    {
        // 連番インデックス（1始まり）と名称
        [ResultColumn("NodeIdx", 0, tooltip: "節点の通し番号（1 始まり）")] public int NodeIndex { get; init; }
        [ResultColumn("NodeName", 1, tooltip: "節点の名称")] public string NodeName { get; init; } = "";

        // 座標
        [ResultColumn("X(m)", 2, "N3", "節点の全体座標系 X 座標（水平）")] public double X { get; init; }
        [ResultColumn("Y(m)", 3, "N3", "節点の全体座標系 Y 座標（水平）")] public double Y { get; init; }
        [ResultColumn("Z(m)", 4, "N3", "節点の全体座標系 Z 座標（鉛直、上向きが正）")] public double Z { get; init; }

        // 変位 (m → mm 換算)
        [ResultColumn("Ux(mm)", 10, "N3", "全体座標系 X 方向の変位。水平合成は √(Ux²+Uy²)")] public double UxMm { get; init; }
        [ResultColumn("Uy(mm)", 11, "N3", "全体座標系 Y 方向の変位。水平合成は √(Ux²+Uy²)")] public double UyMm { get; init; }
        [ResultColumn("Uz(mm)", 12, "N3", "全体座標系 Z 方向（鉛直）の変位。沈下は負")] public double UzMm { get; init; }

        // 回転 (そのまま rad)
        [ResultColumn("Rx(rad)", 13, "N5", "全体座標系 X 軸まわりの回転角")] public double Rx { get; init; }
        [ResultColumn("Ry(rad)", 14, "N5", "全体座標系 Y 軸まわりの回転角")] public double Ry { get; init; }
        [ResultColumn("Rz(rad)", 15, "N5", "全体座標系 Z 軸（鉛直軸）まわりの回転角")] public double Rz { get; init; }

        // NodeDisp を直接使用（BeamDisp ではない）
        public static NodeDisplacementRow From(int nodeIndex, Node node, NodeDisp disp)
        {
            return new NodeDisplacementRow
            {
                NodeIndex = nodeIndex,
                NodeName = node.Name,
                X = node.Coord.X,
                Y = node.Coord.Y,
                Z = node.Coord.Z,
                UxMm = disp.Ux * 1000.0,
                UyMm = disp.Uy * 1000.0,
                UzMm = disp.Uz * 1000.0,
                Rx = disp.Rx,
                Ry = disp.Ry,
                Rz = disp.Rz
            };
        }
    }
}