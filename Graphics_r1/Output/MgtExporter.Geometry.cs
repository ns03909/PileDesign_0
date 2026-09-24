using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PileDesign.FEM;
using Material = PileDesign.FEM.Material;

namespace PileDesign.Output
{
    // MGT のジオメトリ関連セクション（NODE, MATERIAL, SECTION, ELEMENT）を出力する partial。
    public partial class MgtExporter
    {
        private static void WriteNodes(StreamWriter writer, ExportContext ctx)
        {
            writer.WriteLine("*NODE    ; Nodes");
            writer.WriteLine("; iNO, X, Y, Z");
            foreach (var (node, id) in ctx.NodeIdMap)
            {
                double x = node.Coord.X;
                double y = node.Coord.Y;
                double z = node.Coord.Z;
                // 地盤境界節点はX方向に1mオフセット（ElasticLink長さゼロ回避のため）
                if (ctx.SoilBoundaryNodes.Contains(node))
                {
                    x += SoilNodeOffsetX;
                }
                writer.WriteLine($"   {id,5}, {x}, {y}, {z}");
            }
            // Y方向用の仮想地盤節点（杭節点からY方向に+1m）
            foreach (var (spring, yId) in ctx.SpringYNodeIds)
            {
                if (spring.NodeI == null) continue;
                double x = spring.NodeI.Coord.X;
                double y = spring.NodeI.Coord.Y + SoilNodeOffsetY;
                double z = spring.NodeI.Coord.Z;
                writer.WriteLine($"   {yId,5}, {x}, {y}, {z}");
            }
            writer.WriteLine();
        }

        private static void WriteMaterials(StreamWriter writer, ExportContext ctx)
        {
            writer.WriteLine("*MATERIAL    ; Material");
            writer.WriteLine("; iMAT, TYPE, MNAME, SPHEAT, HEATCO, PLAST, TUNIT, bMASS, DAMPRATIO, [DATA1]");
            writer.WriteLine("; [DATA1] : 2, ELAST, POISN, THERMAL, DEN, MASS");
            foreach (var (material, id) in ctx.MaterialIdMap)
            {
                string name = $"{"Mat" + id,-18}";
                // DATA1 type=2: ユーザー定義弾性特性（1行形式）
                writer.WriteLine($"   {id,4}, CONC , {name}, 0, 0, , C, NO, 0.02, 2, {material.E:E2}, {material.P:F4}, 1.200E-005, 0, 0");
            }
            writer.WriteLine();
        }

        /// <summary>
        /// 断面を<b>値 (VALUE 型)</b> で書く。解析モデルの断面性能 (AX・AY・AZ・IX・IY・IZ) をそのまま渡す。
        ///
        /// 以前は断面積から円の直径を逆算し、すべての梁を寸法入力の円形断面 (DBUSER, SR) として書いていた。
        /// midas は円の寸法から断面性能を計算し直すので、解析モデルの値と合わなかった。
        /// <list type="bullet">
        /// <item>杭は EA・EI・GJ から作った等価断面 (鉄筋・鋼管を含む) なので、I も J も同じ面積の円とは違う。</item>
        /// <item>基礎梁は矩形なのに円として書かれ、曲げ剛性・ねじり剛性・せん断断面積がすべて違っていた。</item>
        /// </list>
        /// 形状 (<see cref="RepresentativeShape"/>) は応力を出す点の位置を決めるためだけに書く。剛性は 2 行目の値で決まる。
        /// </summary>
        private static void WriteSections(StreamWriter writer, ExportContext ctx)
        {
            writer.WriteLine("*SECTION    ; Section");
            writer.WriteLine("; iSEC, TYPE, SNAME, [OFFSET], bSD, bWE, SHAPE, BLT, D1, ..., D8, iCEL              ; 1st line - VALUE");
            writer.WriteLine(";       AREA, ASy, ASz, Ixx, Iyy, Izz                                               ; 2nd line");
            writer.WriteLine(";       CyP, CyM, CzP, CzM, QyB, QzB, PERI_OUT, PERI_IN, Cy, Cz                     ; 3rd line");
            writer.WriteLine(";       Y1, Y2, Y3, Y4, Z1, Z2, Z3, Z4, Zyy, Zzz                                    ; 4th line");
            writer.WriteLine("; [OFFSET] : OFFSET, iCENT, iREF, iHORZ, HUSER, iVERT, VUSER");
            foreach (var (section, id) in ctx.SectionIdMap)
            {
                string name = $"{"Sec" + id,-18}";
                var shape = RepresentativeShape.Of(section);
                writer.WriteLine($"   {id,4}, VALUE , {name}, CC, 0, 0, 0, 0, 0, 0, YES, NO, {shape.Code} , 2, "
                                 + $"{V(shape.D1)}, {V(shape.D2)}, 0, 0, 0, 0, 0, 0, 0");
                writer.WriteLine($"       {V(section.AX)}, {V(section.AY)}, {V(section.AZ)}, {V(section.IX)}, {V(section.IY)}, {V(section.IZ)}");
                writer.WriteLine($"       {V(shape.CyP)}, {V(shape.CyP)}, {V(shape.CzP)}, {V(shape.CzP)}, {V(shape.QyB)}, {V(shape.QzB)}, "
                                 + $"{V(shape.PeriOut)}, 0, {V(shape.CyP)}, {V(shape.CzP)}");
                writer.WriteLine($"       {V(shape.Y[0])}, {V(shape.Y[1])}, {V(shape.Y[2])}, {V(shape.Y[3])}, "
                                 + $"{V(shape.Z[0])}, {V(shape.Z[1])}, {V(shape.Z[2])}, {V(shape.Z[3])}, "
                                 + $"{V(shape.CzP > 0 ? section.IY / shape.CzP : 0)}, {V(shape.CyP > 0 ? section.IZ / shape.CyP : 0)}");
            }
            writer.WriteLine();
        }

        /// <summary>断面性能の数値の書式 (有効数字 10 桁。桁を落として剛性を丸めない)。</summary>
        private static string V(double value) => value.ToString("G10", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// 応力を出す点の位置を決めるための代表形状。<b>剛性には使わない</b> (剛性は断面性能の値をそのまま渡す)。
        ///
        /// せん断断面積が断面積の 5/6 の断面は矩形 (基礎梁。解析モデルが (5/6)bh としている) とみなし、
        /// 幅と高さを断面積と断面二次モーメントから逆算する (Iyy = B·H³/12 → H = √(12·Iyy/A))。
        /// それ以外 (杭の等価断面など) は同じ面積の円とする。
        /// </summary>
        internal readonly record struct RepresentativeShape(
            string Code, double D1, double D2, double CyP, double CzP, double QyB, double QzB, double PeriOut,
            double[] Y, double[] Z)
        {
            internal static RepresentativeShape Of(Section s)
            {
                bool rectangular = s.AX > 0
                                   && Math.Abs(s.AY - s.AX * 5.0 / 6.0) <= 1e-9 * s.AX
                                   && Math.Abs(s.AZ - s.AX * 5.0 / 6.0) <= 1e-9 * s.AX;
                if (rectangular && s.IY > 0 && s.IZ > 0)
                {
                    double h = Math.Sqrt(12.0 * s.IY / s.AX);   // 高さ (局所 z 方向)
                    double b = Math.Sqrt(12.0 * s.IZ / s.AX);   // 幅 (局所 y 方向)
                    return new RepresentativeShape("SB", h, b, b / 2, h / 2, b * b / 8, h * h / 8, 2 * (b + h),
                        [-b / 2, b / 2, b / 2, -b / 2], [h / 2, h / 2, -h / 2, -h / 2]);
                }

                double d = Math.Sqrt(Math.Max(s.AX, 0) / Math.PI) * 2;
                double r = d / 2;
                double c = r / Math.Sqrt(2);   // 45° の点
                return new RepresentativeShape("SR", d, 0, r, r, d * d / 12, d * d / 12, Math.PI * d,
                    [-c, c, c, -c], [c, c, -c, -c]);
            }
        }

        /// <summary>
        /// 梁要素を書く。<b>書けない梁が 1 本でもあれば、梁と理由を示して出力を止める</b>
        /// (<see cref="InvalidOperationException"/>。一時ファイルに書いているので、保存先は前の内容のまま)。
        ///
        /// 以前は断面・材料・節点の参照が見つからない梁を黙って飛ばしていた。出力は成功するので、
        /// 参照が壊れた解析モデルでは、梁の一部が無い MGT ファイルを正常な成果物として渡していた。
        /// </summary>
        private void WriteElements(StreamWriter writer, ExportContext ctx)
        {
            writer.WriteLine("*ELEMENT    ; Elements");
            writer.WriteLine("; iEL, TYPE, iMAT, iPRO, iN1, iN2, ANGLE, iSUB");
            int elemId = 1;
            var problems = new List<string>();
            foreach (var beam in _anaModel.Beams)
            {
                string? problem =
                    beam.Section == null ? "断面がありません"
                    : beam.Section.Material == null ? "断面に材料がありません"
                    : beam.NodeI == null || !ctx.NodeIdMap.ContainsKey(beam.NodeI) ? "始端の節点が解析モデルの節点にありません"
                    : beam.NodeJ == null || !ctx.NodeIdMap.ContainsKey(beam.NodeJ) ? "終端の節点が解析モデルの節点にありません"
                    : !ctx.MaterialIdMap.ContainsKey(beam.Section.Material) ? "材料が材料の一覧にありません"
                    : !ctx.SectionIdMap.ContainsKey(beam.Section) ? "断面が断面の一覧にありません"
                    : null;
                if (problem != null)
                {
                    problems.Add($"・梁「{beam.Name}」: {problem}");
                    continue;
                }

                int nodeI = ctx.NodeIdMap[beam.NodeI!];
                int nodeJ = ctx.NodeIdMap[beam.NodeJ!];
                int mId = ctx.MaterialIdMap[beam.Section!.Material];
                int sId = ctx.SectionIdMap[beam.Section];
                writer.WriteLine($"   {elemId,5}, BEAM  , {mId,4}, {sId,5}, {nodeI,5}, {nodeJ,5}, {0,5}, {0,5}");
                elemId++;
            }

            if (problems.Count > 0)
                throw new InvalidOperationException(
                    $"解析モデルの梁 {problems.Count} 本を MGT に書けないため、出力を止めました (書くと梁の欠けたモデルになります)。\n"
                    + string.Join("\n", problems.Take(10))
                    + (problems.Count > 10 ? $"\n…ほか {problems.Count - 10} 本" : "")
                    + "\n解析モデルを作り直す (杭要素分割・解析をやり直す) と直ることがあります。");
            writer.WriteLine();
        }
    }
}
