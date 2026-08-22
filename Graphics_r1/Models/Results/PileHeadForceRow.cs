using PileDesign.FEM;

namespace PileDesign.Models.Results
{
    /// <summary>
    /// 杭頭応力テーブル行（最も上の杭要素のi端断面力）
    /// </summary>
    public sealed class PileHeadForceRow
    {
        [ResultColumn("杭No", 0, tooltip: "杭配置の番号")] public int PileNo { get; init; }
        [ResultColumn("要素名", 1, tooltip: "杭頭の断面力を取り出した梁要素（最も上の杭要素）の名称")] public string ElementName { get; init; } = "";
        [ResultColumn("節点名", 2, tooltip: "杭頭節点の名称")] public string NodeName { get; init; } = "";

        // 座標
        [ResultColumn("X(m)", 3, "N3", "杭頭節点の全体座標系 X 座標（水平）")] public double X { get; init; }
        [ResultColumn("Y(m)", 4, "N3", "杭頭節点の全体座標系 Y 座標（水平）")] public double Y { get; init; }
        [ResultColumn("Z(m)", 5, "N3", "杭頭節点の全体座標系 Z 座標（鉛直、上向きが正）")] public double Z { get; init; }

        // 断面力 (kN / kNm)
        [ResultColumn("Fx(kN)", 10, "N2", "杭頭の軸力。引張が正で、圧縮軸力は −Fx として扱う")] public double Fx { get; init; }
        [ResultColumn("Fy(kN)", 11, "N2", "杭頭の部材座標系 y 軸方向のせん断力")] public double Fy { get; init; }
        [ResultColumn("Fz(kN)", 12, "N2", "杭頭の部材座標系 z 軸方向のせん断力")] public double Fz { get; init; }
        [ResultColumn("Mx(kNm)", 13, "N2", "杭頭のねじりモーメント（杭軸まわり）")] public double Mx { get; init; }
        [ResultColumn("My(kNm)", 14, "N2", "杭頭の部材座標系 y 軸まわりの曲げモーメント")] public double My { get; init; }
        [ResultColumn("Mz(kNm)", 15, "N2", "杭頭の部材座標系 z 軸まわりの曲げモーメント")] public double Mz { get; init; }

        // 包絡
        [ResultColumn("|M|max(kNm)", 20, "N2", "この要素の両端の水平合成曲げモーメント √(My²+Mz²) の大きい方")] public double MabsMax { get; init; }
        [ResultColumn("|V|max(kN)", 21, "N2", "この要素の両端の水平合成せん断力 √(Fy²+Fz²) の大きい方")] public double FabsMax { get; init; }

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
