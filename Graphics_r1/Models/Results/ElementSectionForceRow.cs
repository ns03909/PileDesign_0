using PileDesign.FEM;

namespace PileDesign.Models.Results
{
    public sealed class ElementSectionForceRow
    {
        [ResultColumn("ElemIdx", 0, tooltip: "梁要素の通し番号（1 始まり）")] public int ElementIndex { get; init; }
        [ResultColumn("ElemName", 1, tooltip: "梁要素の名称")] public string ElementName { get; init; } = "";
        [ResultColumn("Node1Idx", 2, tooltip: "i 端（始点側）の節点番号")] public int Node1Index { get; init; }
        [ResultColumn("Node2Idx", 3, tooltip: "j 端（終点側）の節点番号")] public int Node2Index { get; init; }

        // i端
        [ResultColumn("Fxi(kN)", 10, "N1", "i 端（始点側）の軸力。引張が正で、圧縮軸力は −Fx として扱う")] public double Fxi { get; init; }
        [ResultColumn("Fyi(kN)", 11, "N1", "i 端（始点側）の部材座標系 y 軸方向のせん断力")] public double Fyi { get; init; }
        [ResultColumn("Fzi(kN)", 12, "N1", "i 端（始点側）の部材座標系 z 軸方向のせん断力")] public double Fzi { get; init; }
        [ResultColumn("Mxi(kNm)", 13, "N1", "i 端（始点側）のねじりモーメント（部材軸まわり）")] public double Mxi { get; init; }
        [ResultColumn("Myi(kNm)", 14, "N1", "i 端（始点側）の部材座標系 y 軸まわりの曲げモーメント")] public double Myi { get; init; }
        [ResultColumn("Mzi(kNm)", 15, "N1", "i 端（始点側）の部材座標系 z 軸まわりの曲げモーメント")] public double Mzi { get; init; }

        // j端
        [ResultColumn("Fxj(kN)", 20, "N1", "j 端（終点側）の軸力。引張が正で、圧縮軸力は −Fx として扱う")] public double Fxj { get; init; }
        [ResultColumn("Fyj(kN)", 21, "N1", "j 端（終点側）の部材座標系 y 軸方向のせん断力")] public double Fyj { get; init; }
        [ResultColumn("Fzj(kN)", 22, "N1", "j 端（終点側）の部材座標系 z 軸方向のせん断力")] public double Fzj { get; init; }
        [ResultColumn("Mxj(kNm)", 23, "N1", "j 端（終点側）のねじりモーメント（部材軸まわり）")] public double Mxj { get; init; }
        [ResultColumn("Myj(kNm)", 24, "N1", "j 端（終点側）の部材座標系 y 軸まわりの曲げモーメント")] public double Myj { get; init; }
        [ResultColumn("Mzj(kNm)", 25, "N1", "j 端（終点側）の部材座標系 z 軸まわりの曲げモーメント")] public double Mzj { get; init; }

        // 包絡（絶対最大）
        [ResultColumn("|M|max(kNm)", 30, "N1", "両端の水平合成曲げモーメント √(My²+Mz²) の大きい方")] public double MabsMax { get; init; }
        [ResultColumn("|V|max(kN)", 31, "N1", "両端の水平合成せん断力 √(Fy²+Fz²) の大きい方")] public double FabsMax { get; init; }

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

        /// <summary>
        /// RotationalSpring（杭頭リンク要素）から断面力行を生成
        /// </summary>
        public static ElementSectionForceRow FromSpring(
            int elementIndex,
            int node1Index,
            int node2Index,
            RotationalSpring spring,
            BeamForce bf)
        {
            return new ElementSectionForceRow
            {
                ElementIndex = elementIndex,
                ElementName = spring.Name,
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