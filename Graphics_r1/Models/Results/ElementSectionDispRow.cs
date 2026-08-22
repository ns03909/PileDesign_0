using PileDesign.FEM;

namespace PileDesign.Models.Results
{
    public sealed class ElementSectionDispRow
    {
        [ResultColumn("ElemIdx", 0, tooltip: "梁要素の通し番号（1 始まり）")] public int ElementIndex { get; init; }
        [ResultColumn("ElemName", 1, tooltip: "梁要素の名称")] public string ElementName { get; init; } = "";
        [ResultColumn("Node1Idx", 2, tooltip: "i 端（始点側）の節点番号")] public int Node1Index { get; init; }
        [ResultColumn("Node2Idx", 3, tooltip: "j 端（終点側）の節点番号")] public int Node2Index { get; init; }

        // i端
        [ResultColumn("Uxi(mm)", 10, "N3", "i 端（始点側）の部材軸方向の変位。杭では杭軸（ほぼ鉛直）方向の伸縮")] public double Uxi { get; init; }
        [ResultColumn("Uyi(mm)", 11, "N3", "i 端（始点側）の部材座標系 y 軸方向の変位。杭では水平 2 方向の一方")] public double Uyi { get; init; }
        [ResultColumn("Uzi(mm)", 12, "N3", "i 端（始点側）の部材座標系 z 軸方向の変位。杭では水平 2 方向の他方")] public double Uzi { get; init; }
        [ResultColumn("Txi(rad)", 13, "N5", "i 端（始点側）の部材軸まわりの回転角（ねじれ角）")] public double Txi { get; init; }
        [ResultColumn("Tyi(rad)", 14, "N5", "i 端（始点側）の部材座標系 y 軸まわりの回転角（たわみ角）")] public double Tyi { get; init; }
        [ResultColumn("Tzi(rad)", 15, "N5", "i 端（始点側）の部材座標系 z 軸まわりの回転角（たわみ角）")] public double Tzi { get; init; }

        // j端
        [ResultColumn("Uxj(mm)", 20, "N3", "j 端（終点側）の部材軸方向の変位。杭では杭軸（ほぼ鉛直）方向の伸縮")] public double Uxj { get; init; }
        [ResultColumn("Uyj(mm)", 21, "N3", "j 端（終点側）の部材座標系 y 軸方向の変位。杭では水平 2 方向の一方")] public double Uyj { get; init; }
        [ResultColumn("Uzj(mm)", 22, "N3", "j 端（終点側）の部材座標系 z 軸方向の変位。杭では水平 2 方向の他方")] public double Uzj { get; init; }
        [ResultColumn("Txj(rad)", 23, "N5", "j 端（終点側）の部材軸まわりの回転角（ねじれ角）")] public double Txj { get; init; }
        [ResultColumn("Tyj(rad)", 24, "N5", "j 端（終点側）の部材座標系 y 軸まわりの回転角（たわみ角）")] public double Tyj { get; init; }
        [ResultColumn("Tzj(rad)", 25, "N5", "j 端（終点側）の部材座標系 z 軸まわりの回転角（たわみ角）")] public double Tzj { get; init; }

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

        /// <summary>
        /// RotationalSpring（杭頭リンク要素）から断面変位行を生成
        /// </summary>
        public static ElementSectionDispRow FromSpring(
            int elementIndex,
            int node1Index,
            int node2Index,
            RotationalSpring spring,
            BeamDisp disp)
        {
            return new ElementSectionDispRow
            {
                ElementIndex = elementIndex,
                ElementName = spring.Name,
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