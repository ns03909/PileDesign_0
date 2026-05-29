using PileDesign.Common;
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

        // 単位長さ当たり地盤反力 (kN/m) — 分担長は SoilReactionUtil で算出 (FEM 実梁長ベース)
        [ResultColumn("Lt(m)",       40, "N3")] public double TributaryLength { get; init; }
        [ResultColumn("Fx/Lt(kN/m)", 41, "N2")] public double FxPerLt { get; init; }
        [ResultColumn("Fy/Lt(kN/m)", 42, "N2")] public double FyPerLt { get; init; }
        [ResultColumn("Fz/Lt(kN/m)", 43, "N2")] public double FzPerLt { get; init; }
        [ResultColumn("|Fh|/Lt(kN/m)", 44, "N2")] public double FhAbsPerLt { get; init; }

        public static SoilSpringForceRow From(
            int springIndex,
            int node1Index,
            int node2Index,
            HorizontalSoilSpring spring,
            BeamForce bf,
            BeamDisp bd,
            AnaModel anaModel = null)
        {
            double relUx = (bd.Dxi - bd.Dxj) * 1000.0; // m → mm
            double relUy = (bd.Dyi - bd.Dyj) * 1000.0;
            double relUz = (bd.Dzi - bd.Dzj) * 1000.0;

            double tributary = SoilReactionUtil.GetNodeTributaryLength(spring?.NodeI, anaModel);
            double inv = tributary > 1e-9 ? 1.0 / tributary : 0;
            double fhAbs = System.Math.Sqrt(bf.Fxi * bf.Fxi + bf.Fyi * bf.Fyi);

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
                FhAbs = fhAbs,
                RelUhAbs = System.Math.Sqrt(relUx * relUx + relUy * relUy),
                TributaryLength = tributary,
                FxPerLt = bf.Fxi * inv,
                FyPerLt = bf.Fyi * inv,
                FzPerLt = bf.Fzi * inv,
                FhAbsPerLt = fhAbs * inv,
            };
        }
    }
}
