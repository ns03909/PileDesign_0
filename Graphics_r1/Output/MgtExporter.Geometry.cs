using System;
using System.Collections.Generic;
using System.IO;
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

        private static void WriteSections(StreamWriter writer, ExportContext ctx)
        {
            writer.WriteLine("*SECTION    ; Section");
            foreach (var (section, id) in ctx.SectionIdMap)
            {
                string name = $"{"Sec" + id,-18}";
                double reprDim = Math.Sqrt(section.AX / Math.PI) * 2; // 断面積から直径を推定
                writer.WriteLine($"   {id,4}, DBUSER    , {name}, CC, 0, 0, 0, 0, 0, 0, YES, NO, SR , 2, {reprDim:G6}, 0, 0, 0, 0, 0, 0, 0, 0, 0");
            }
            writer.WriteLine();
        }

        private void WriteElements(StreamWriter writer, ExportContext ctx)
        {
            writer.WriteLine("*ELEMENT    ; Elements");
            writer.WriteLine("; iEL, TYPE, iMAT, iPRO, iN1, iN2, ANGLE, iSUB");
            int elemId = 1;
            foreach (var beam in _anaModel.Beams)
            {
                if (beam.Section == null || beam.Section.Material == null) continue;
                if (!ctx.NodeIdMap.TryGetValue(beam.NodeI, out int nodeI)) continue;
                if (!ctx.NodeIdMap.TryGetValue(beam.NodeJ, out int nodeJ)) continue;
                if (!ctx.MaterialIdMap.TryGetValue(beam.Section.Material, out int mId)) continue;
                if (!ctx.SectionIdMap.TryGetValue(beam.Section, out int sId)) continue;

                writer.WriteLine($"   {elemId,5}, BEAM  , {mId,4}, {sId,5}, {nodeI,5}, {nodeJ,5}, {0,5}, {0,5}");
                elemId++;
            }
            writer.WriteLine();
        }
    }
}
