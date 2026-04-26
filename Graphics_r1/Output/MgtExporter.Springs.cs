using System.Collections.Generic;
using System.IO;
using System.Linq;
using PileDesign.FEM;
using PileDesign.Models.InputData;

namespace PileDesign.Output
{
    // MGT のばね関連セクション（FORCES-DEFORMATION FUNCTION, ELASTICLINK）を出力する partial。
    public partial class MgtExporter
    {
        // MULTI LINEAR用サンプリング変位点（m）
        private static readonly double[] MultiLinearDispSamples =
        [
            0.0, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1.0
        ];

        private void WriteForcesDeformationFunction(StreamWriter writer, ExportContext ctx)
        {
            var funcIds = ctx.SpringFunctionIds;
            if (funcIds.Count == 0) return;

            // 節点から地盤反力情報への参照を構築
            var reactionByNode = new Dictionary<Node, HorizontalSoilReactionItem>(ReferenceEqualityComparer.Instance);
            foreach (var beam in _anaModel.Beams)
            {
                if (beam.HorizontalSoilReactionItem == null) continue;
                if (beam.NodeI != null && !reactionByNode.ContainsKey(beam.NodeI))
                    reactionByNode[beam.NodeI] = beam.HorizontalSoilReactionItem;
                if (beam.NodeJ != null && !reactionByNode.ContainsKey(beam.NodeJ))
                    reactionByNode[beam.NodeJ] = beam.HorizontalSoilReactionItem;
            }

            writer.WriteLine("*FORCES-DEFORMATION FUNCTION    ; Forces-Deformation Function");
            writer.WriteLine("; FUNC=NAME, FTYPE, SYMM, ID");
            writer.WriteLine(";        X1, Y1, X2, Y2, ...");

            foreach (var (spring, fid) in funcIds)
            {
                // 地盤反力情報を取得
                HorizontalSoilReactionItem reaction = null;
                if (spring.NodeI != null) reactionByNode.TryGetValue(spring.NodeI, out reaction);

                double py = reaction?.PyFrontTop ?? 0;
                double B = reaction?.B ?? 1;
                double tributary = reaction != null ? (reaction.ZTop - reaction.ZBtm) * 0.5 : 1;

                // SYMM=YES: 正側のみ定義、負側は自動ミラーリング
                writer.WriteLine($"   FUNC={fid}, FORCE, YES, 0");

                var pairs = new List<string>();
                foreach (var y in MultiLinearDispSamples)
                {
                    double force;
                    if (reaction != null && py > 0)
                    {
                        double p = reaction.GetP(y, py); // kN/m2
                        force = p * B * tributary; // kN
                    }
                    else
                    {
                        // フォールバック: 線形ばねの剛性使用
                        var ke = spring.KeTan;
                        double k = ke?[0, 0] ?? 0;
                        force = k * y;
                    }
                    pairs.Add($"{y,10:F4}, {force,12:F4}");
                }

                // 3ペア/行で出力
                for (int i = 0; i < pairs.Count; i += 3)
                {
                    var linePairs = pairs.Skip(i).Take(3);
                    writer.WriteLine($"         {string.Join(", ", linePairs)}");
                }
            }
            writer.WriteLine();
        }

        private void WriteElasticLinks(StreamWriter writer, ExportContext ctx)
        {
            var nodeIdMap = ctx.NodeIdMap;
            var funcIds = ctx.SpringFunctionIds;
            var springYNodeIds = ctx.SpringYNodeIds;

            bool hasHorizontal = _anaModel.HorizontalSoilSprings != null && _anaModel.HorizontalSoilSprings.Count > 0;
            bool hasPenalty = _anaModel.PenaltySprings != null && _anaModel.PenaltySprings.Count > 0;
            if (!hasHorizontal && !hasPenalty) return;

            writer.WriteLine("*ELASTICLINK    ; Elastic Link");
            writer.WriteLine("; iNO, iNODE1, iNODE2, LINK, ANGLE, DIR, FUNCTION, bSHEAR, DRENDI, GROUP                         ; MULTI LINEAR");
            writer.WriteLine("; iNO, iNODE1, iNODE2, LINK, ANGLE, R_SDx, R_SDy, R_SDz, R_SRx, R_SRy, R_SRz, SDx, SDy, SDz, SRx, SRy, SRz, bSHEAR, DRy, DRz, GROUP ; GEN");
            int linkId = 1;

            // 水平地盤ばね: MULTI LINEAR（X方向リンク、Y方向リンク（Y解析済みの場合））
            if (hasHorizontal && _anaModel.HorizontalSoilSprings != null)
            {
                // X方向リンク（軸方向=DIR 0、既存のNodeJはX方向にオフセット済み）
                foreach (var spring in _anaModel.HorizontalSoilSprings)
                {
                    if (spring.NodeI == null || spring.NodeJ == null) continue;
                    if (!nodeIdMap.TryGetValue(spring.NodeI, out int nodeI)) continue;
                    if (!nodeIdMap.TryGetValue(spring.NodeJ, out int nodeJ)) continue;
                    if (!funcIds.TryGetValue(spring, out int fid)) continue;

                    writer.WriteLine($"   {linkId,5}, {nodeI,5}, {nodeJ,5}, MULTI LINEAR, 0, 0, {fid}, NO, 0.5, ");
                    linkId++;
                }
                // Y方向リンク（軸方向=DIR 0、仮想Y節点にY方向オフセット）
                foreach (var spring in _anaModel.HorizontalSoilSprings)
                {
                    if (spring.NodeI == null) continue;
                    if (!nodeIdMap.TryGetValue(spring.NodeI, out int nodeI)) continue;
                    if (!springYNodeIds.TryGetValue(spring, out int yNodeId)) continue;
                    if (!funcIds.TryGetValue(spring, out int fid)) continue;

                    writer.WriteLine($"   {linkId,5}, {nodeI,5}, {yNodeId,5}, MULTI LINEAR, 0, 0, {fid}, NO, 0.5, ");
                    linkId++;
                }
            }

            // ペナルティばね: RIGID（同位置接続を許容、杭頭の剛体連結用）
            if (hasPenalty && _anaModel.PenaltySprings != null)
            {
                foreach (var spring in _anaModel.PenaltySprings)
                {
                    if (spring.NodeI == null || spring.NodeJ == null) continue;
                    if (!nodeIdMap.TryGetValue(spring.NodeI, out int nodeI)) continue;
                    if (!nodeIdMap.TryGetValue(spring.NodeJ, out int nodeJ)) continue;

                    // RIGID形式: iNO, iNODE1, iNODE2, LINK, ANGLE, bSHEAR, DRy, DRz, GROUP
                    writer.WriteLine($"   {linkId,5}, {nodeI,5}, {nodeJ,5}, RIGID, 0, NO, 0, 0, ");
                    linkId++;
                }
            }
            writer.WriteLine();
        }
    }
}
