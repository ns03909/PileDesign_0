using PileDesign.FEM;

namespace PileDesign.Models.Results
{
    public sealed class ElementSectionForceRow
    {
        [ResultColumn("ElemIdx", 0)] public int ElementIndex { get; init; }
        [ResultColumn("ElemName", 1)] public string ElementName { get; init; } = "";
        [ResultColumn("Node1Idx", 2)] public int Node1Index { get; init; }
        [ResultColumn("Node2Idx", 3)] public int Node2Index { get; init; }

        // i端
        [ResultColumn("Fxi(kN)", 10, "N1")] public double Fxi { get; init; }
        [ResultColumn("Fyi(kN)", 11, "N1")] public double Fyi { get; init; }
        [ResultColumn("Fzi(kN)", 12, "N1")] public double Fzi { get; init; }
        [ResultColumn("Mxi(kNm)", 13, "N1")] public double Mxi { get; init; }
        [ResultColumn("Myi(kNm)", 14, "N1")] public double Myi { get; init; }
        [ResultColumn("Mzi(kNm)", 15, "N1")] public double Mzi { get; init; }

        // j端
        [ResultColumn("Fxj(kN)", 20, "N1")] public double Fxj { get; init; }
        [ResultColumn("Fyj(kN)", 21, "N1")] public double Fyj { get; init; }
        [ResultColumn("Fzj(kN)", 22, "N1")] public double Fzj { get; init; }
        [ResultColumn("Mxj(kNm)", 23, "N1")] public double Mxj { get; init; }
        [ResultColumn("Myj(kNm)", 24, "N1")] public double Myj { get; init; }
        [ResultColumn("Mzj(kNm)", 25, "N1")] public double Mzj { get; init; }

        // 包絡（絶対最大）
        [ResultColumn("|M|max(kNm)", 30, "N1")] public double MabsMax { get; init; }
        [ResultColumn("|V|max(kN)", 31, "N1")] public double FabsMax { get; init; }

        public static ElementSectionForceRow From(
            int elementIndex,
            int node1Index,
            int node2Index,
            Beam beam,
            BeamForce bf)
        {
            return new ElementSectionForceRow
            {
                ElementIndex = elementIndex,
                ElementName = beam.Name,
                Node1Index = node1Index,
                Node2Index = node2Index,
                Fxi = bf.Fxi,
                Fyi = bf.Fyi,
                Fzi = bf.Fzi,
                Mxi = bf.Mxi,
                Myi = bf.Myi,
                Mzi = bf.Mzi,
                Fxj = bf.Fxj,
                Fyj = bf.Fyj,
                Fzj = bf.Fzj,
                Mxj = bf.Mxj,
                Myj = bf.Myj,
                Mzj = bf.Mzj,
                MabsMax = bf.MabsMax,
                FabsMax = bf.FabsMax
            };
        }
    }
}