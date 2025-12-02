using PileDesign.FEM;

namespace PileDesign.Models.Results
{
    public sealed class NodeDisplacementRow
    {
        // 連番インデックス（1始まり）と名称
        [ResultColumn("NodeIdx", 0)] public int NodeIndex { get; init; }
        [ResultColumn("NodeName", 1)] public string NodeName { get; init; } = "";

        // 座標
        [ResultColumn("X(m)", 2, "N3")] public double X { get; init; }
        [ResultColumn("Y(m)", 3, "N3")] public double Y { get; init; }
        [ResultColumn("Z(m)", 4, "N3")] public double Z { get; init; }

        // 変位 (m → mm 換算)
        [ResultColumn("Ux(mm)", 10, "N3")] public double UxMm { get; init; }
        [ResultColumn("Uy(mm)", 11, "N3")] public double UyMm { get; init; }
        [ResultColumn("Uz(mm)", 12, "N3")] public double UzMm { get; init; }

        // 回転 (そのまま rad)
        [ResultColumn("Rx(rad)", 13, "N5")] public double Rx { get; init; }
        [ResultColumn("Ry(rad)", 14, "N5")] public double Ry { get; init; }
        [ResultColumn("Rz(rad)", 15, "N5")] public double Rz { get; init; }

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