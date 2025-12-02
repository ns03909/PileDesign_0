using PileDesign.FEM;

namespace PileDesign.Models.Results
{
    public sealed class NodeForceRow
    {
        [ResultColumn("NodeIdx", 0)] public int NodeIndex { get; init; }
        [ResultColumn("NodeName", 1)] public string NodeName { get; init; } = "";

        // 座標
        [ResultColumn("X(m)", 2, "N3")] public double X { get; init; }
        [ResultColumn("Y(m)", 3, "N3")] public double Y { get; init; }
        [ResultColumn("Z(m)", 4, "N3")] public double Z { get; init; }

        // 反力 (kN / kNm)
        [ResultColumn("Rx(kN)", 10, "N2")] public double Fx { get; init; }
        [ResultColumn("Ry(kN)", 11, "N2")] public double Fy { get; init; }
        [ResultColumn("Rz(kN)", 12, "N2")] public double Fz { get; init; }
        [ResultColumn("Mx(kNm)", 13, "N2")] public double Mx { get; init; }
        [ResultColumn("My(kNm)", 14, "N2")] public double My { get; init; }
        [ResultColumn("Mz(kNm)", 15, "N2")] public double Mz { get; init; }

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