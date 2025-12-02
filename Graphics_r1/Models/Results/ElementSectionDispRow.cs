using PileDesign.FEM;

namespace PileDesign.Models.Results
{
    public sealed class ElementSectionDispRow
    {
        [ResultColumn("ElemIdx", 0)] public int ElementIndex { get; init; }
        [ResultColumn("ElemName", 1)] public string ElementName { get; init; } = "";
        [ResultColumn("Node1Idx", 2)] public int Node1Index { get; init; }
        [ResultColumn("Node2Idx", 3)] public int Node2Index { get; init; }

        // i端
        [ResultColumn("Uxi(mm)", 10, "N3")] public double Uxi { get; init; }
        [ResultColumn("Uyi(mm)", 11, "N3")] public double Uyi { get; init; }
        [ResultColumn("Uzi(mm)", 12, "N3")] public double Uzi { get; init; }
        [ResultColumn("Txi(rad)", 13, "N5")] public double Txi { get; init; }
        [ResultColumn("Tyi(rad)", 14, "N5")] public double Tyi { get; init; }
        [ResultColumn("Tzi(rad)", 15, "N5")] public double Tzi { get; init; }

        // j端
        [ResultColumn("Uxj(mm)", 20, "N3")] public double Uxj { get; init; }
        [ResultColumn("Uyj(mm)", 21, "N3")] public double Uyj { get; init; }
        [ResultColumn("Uzj(mm)", 22, "N3")] public double Uzj { get; init; }
        [ResultColumn("Txj(rad)", 23, "N5")] public double Txj { get; init; }
        [ResultColumn("Tyj(rad)", 24, "N5")] public double Tyj { get; init; }
        [ResultColumn("Tzj(rad)", 25, "N5")] public double Tzj { get; init; }

        public static ElementSectionDispRow From(
            int elementIndex,
            int node1Index,
            int node2Index,
            Beam beam,
            BeamDisp disp)
        {
            return new ElementSectionDispRow
            {
                ElementIndex = elementIndex,
                ElementName = beam.Name,
                Node1Index = node1Index,
                Node2Index = node2Index,
                Uxi = disp.Dxi * 1000.0,
                Uyi = disp.Dyi * 1000.0,
                Uzi = disp.Dzi * 1000.0,
                Txi = disp.Rxi,
                Tyi = disp.Ryi,
                Tzi = disp.Rzi,
                Uxj = disp.Dxj * 1000.0,
                Uyj = disp.Dyj * 1000.0,
                Uzj = disp.Dzj * 1000.0,
                Txj = disp.Rxj,
                Tyj = disp.Ryj,
                Tzj = disp.Rzj
            };
        }
    }
}