using PileDesign.Common;
using PileDesign.FEM;

namespace PileDesign.Models.Results
{
    public sealed class SoilSpringForceRow
    {
        [ResultColumn("SpringIdx", 0, tooltip: "水平地盤ばねの通し番号（1 始まり）")] public int SpringIndex { get; init; }
        [ResultColumn("SpringName", 1, tooltip: "水平地盤ばねの名称")] public string SpringName { get; init; } = "";
        [ResultColumn("Node1Idx", 2, tooltip: "杭側（i 端）の節点番号")] public int Node1Index { get; init; }
        [ResultColumn("Node2Idx", 3, tooltip: "地盤側（j 端）の節点番号")] public int Node2Index { get; init; }

        // 相対変位 (mm)
        [ResultColumn("RelUx(mm)", 10, "N3", "杭側 − 地盤側の相対変位（全体座標系 X 方向）。ばねが縮んだ量")] public double RelUx { get; init; }
        [ResultColumn("RelUy(mm)", 11, "N3", "杭側 − 地盤側の相対変位（全体座標系 Y 方向）。ばねが縮んだ量")] public double RelUy { get; init; }
        [ResultColumn("RelUz(mm)", 12, "N3", "杭側 − 地盤側の相対変位（全体座標系 Z 方向、鉛直）")] public double RelUz { get; init; }

        // 地盤反力 (NodeI端)
        [ResultColumn("Fx(kN)", 20, "N2", "このばねが負担する地盤反力（全体座標系 X 方向）")] public double Fx { get; init; }
        [ResultColumn("Fy(kN)", 21, "N2", "このばねが負担する地盤反力（全体座標系 Y 方向）")] public double Fy { get; init; }
        [ResultColumn("Fz(kN)", 22, "N2", "このばねが負担する地盤反力（全体座標系 Z 方向、鉛直）")] public double Fz { get; init; }

        // 水平合力
        [ResultColumn("|Fh|(kN)", 30, "N2", "水平合成地盤反力 √(Fx²+Fy²)")] public double FhAbs { get; init; }
        [ResultColumn("|RelUh|(mm)", 31, "N3", "水平合成相対変位 √(RelUx²+RelUy²)")] public double RelUhAbs { get; init; }

        // 単位長さ当たり地盤反力 (kN/m) — 分担長は SoilReactionUtil で算出 (FEM 実梁長ベース)
        [ResultColumn("Lt(m)",       40, "N3", "分担長。この節点に接続する梁要素の長さの半分の合計")] public double TributaryLength { get; init; }
        [ResultColumn("Fx/Lt(kN/m)", 41, "N2", "単位長さあたりの地盤反力（X 方向）。深さ方向の分布を比べるときに使う")] public double FxPerLt { get; init; }
        [ResultColumn("Fy/Lt(kN/m)", 42, "N2", "単位長さあたりの地盤反力（Y 方向）。深さ方向の分布を比べるときに使う")] public double FyPerLt { get; init; }
        [ResultColumn("Fz/Lt(kN/m)", 43, "N2", "単位長さあたりの地盤反力（Z 方向、鉛直）")] public double FzPerLt { get; init; }
        [ResultColumn("|Fh|/Lt(kN/m)", 44, "N2", "単位長さあたりの水平合成地盤反力")] public double FhAbsPerLt { get; init; }

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
