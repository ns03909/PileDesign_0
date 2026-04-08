using PileDesign.FEM;

namespace PileDesign.Models.Results
{
    /// <summary>
    /// 杭頭応力テーブル行（最も上の杭要素のi端断面力）
    /// </summary>
    public sealed class PileHeadForceRow
    {
        [ResultColumn("杭No", 0)] public int PileNo { get; init; }
        [ResultColumn("要素名", 1)] public string ElementName { get; init; } = "";
        [ResultColumn("節点名", 2)] public string NodeName { get; init; } = "";

        // 座標
        [ResultColumn("X(m)", 3, "N3")] public double X { get; init; }
        [ResultColumn("Y(m)", 4, "N3")] public double Y { get; init; }
        [ResultColumn("Z(m)", 5, "N3")] public double Z { get; init; }

        // 断面力 (kN / kNm)
        [ResultColumn("Fx(kN)", 10, "N2")] public double Fx { get; init; }
        [ResultColumn("Fy(kN)", 11, "N2")] public double Fy { get; init; }
        [ResultColumn("Fz(kN)", 12, "N2")] public double Fz { get; init; }
        [ResultColumn("Mx(kNm)", 13, "N2")] public double Mx { get; init; }
        [ResultColumn("My(kNm)", 14, "N2")] public double My { get; init; }
        [ResultColumn("Mz(kNm)", 15, "N2")] public double Mz { get; init; }

        // 包絡
        [ResultColumn("|M|max(kNm)", 20, "N2")] public double MabsMax { get; init; }
        [ResultColumn("|V|max(kN)", 21, "N2")] public double FabsMax { get; init; }

        /// <summary>
        /// 杭頭応力行を生成（最も上の杭要素の i 端応力）
        /// </summary>
        public static PileHeadForceRow FromBeamIEnd(int pileNo, Beam beam, BeamForce bf)
        {
            var node = beam.NodeI;
            return new PileHeadForceRow
            {
                PileNo = pileNo,
                ElementName = beam.Name,
                NodeName = node?.Name ?? "",
                X = node?.Coord.X ?? 0,
                Y = node?.Coord.Y ?? 0,
                Z = node?.Coord.Z ?? 0,
                Fx = bf.Fxi,
                Fy = bf.Fyi,
                Fz = bf.Fzi,
                Mx = bf.Mxi,
                My = bf.Myi,
                Mz = bf.Mzi,
                MabsMax = bf.MabsMax,
                FabsMax = bf.FabsMax
            };
        }
    }
}
