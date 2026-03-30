using PileDesign.FEM;

namespace PileDesign.Models.Results
{
    public sealed class SoilSpringForceRow
    {
        [ResultColumn("SpringIdx", 0)] public int SpringIndex { get; init; }
        [ResultColumn("SpringName", 1)] public string SpringName { get; init; } = "";
        [ResultColumn("Node1Idx", 2)] public int Node1Index { get; init; }
        [ResultColumn("Node2Idx", 3)] public int Node2Index { get; init; }

        // 相対変位 (mm)
        [ResultColumn("RelUx(mm)", 10, "N3")] public double RelUx { get; init; }
        [ResultColumn("RelUy(mm)", 11, "N3")] public double RelUy { get; init; }
        [ResultColumn("RelUz(mm)", 12, "N3")] public double RelUz { get; init; }

        // 地盤反力 (NodeI端)
        [ResultColumn("Fx(kN)", 20, "N2")] public double Fx { get; init; }
        [ResultColumn("Fy(kN)", 21, "N2")] public double Fy { get; init; }
        [ResultColumn("Fz(kN)", 22, "N2")] public double Fz { get; init; }

        // 水平合力
        [ResultColumn("|Fh|(kN)", 30, "N2")] public double FhAbs { get; init; }
        [ResultColumn("|RelUh|(mm)", 31, "N3")] public double RelUhAbs { get; init; }

        public static SoilSpringForceRow From(
            int springIndex,
            int node1Index,
            int node2Index,
            HorizontalSoilSpring spring,
            BeamForce bf,
            BeamDisp bd)
        {
            double relUx = (bd.Dxi - bd.Dxj) * 1000.0; // m → mm
            double relUy = (bd.Dyi - bd.Dyj) * 1000.0;
            double relUz = (bd.Dzi - bd.Dzj) * 1000.0;
            return new SoilSpringForceRow
            {
                SpringIndex = springIndex,
                SpringName = spring.Name,
                Node1Index = node1Index,
                Node2Index = node2Index,
                RelUx = relUx,
                RelUy = relUy,
                RelUz = relUz,
                Fx = bf.Fxi,
                Fy = bf.Fyi,
                Fz = bf.Fzi,
                FhAbs = System.Math.Sqrt(bf.Fxi * bf.Fxi + bf.Fyi * bf.Fyi),
                RelUhAbs = System.Math.Sqrt(relUx * relUx + relUy * relUy),
            };
        }
    }
}
