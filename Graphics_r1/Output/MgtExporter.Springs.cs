using System.Collections.Generic;
using System.IO;
using System.Linq;
using PileDesign.FEM;

namespace PileDesign.Output
{
    // MGT のばね関連セクション（FORCES-DEFORMATION FUNCTION, ELASTICLINK）を出力する partial。
    // どのばねをどの向き・どの荷重-変形関係で書くかは MgtExporter.SoilLinks.cs (BuildLinkPlan) で決める。
    public partial class MgtExporter
    {
        private static void WriteForcesDeformationFunction(StreamWriter writer, ExportContext ctx)
        {
            var functions = ctx.Links.Functions;
            if (functions.Count == 0) return;

            writer.WriteLine("*FORCES-DEFORMATION FUNCTION    ; Forces-Deformation Function");
            writer.WriteLine("; FUNC=NAME, FTYPE, SYMM, ID");
            writer.WriteLine(";        X1, Y1, X2, Y2, ...");

            for (int f = 0; f < functions.Count; f++)
            {
                var (curve, symmetric) = functions[f];
                // SYMM=YES: 正側のみ定義、負側は自動ミラーリング。NO: 負側 (杭の鉛直ばねの引抜き) も書く
                writer.WriteLine($"   FUNC={f + 1}, FORCE, {(symmetric ? "YES" : "NO")}, 0");
                var pairs = curve.Select(c => $"{c.Disp,10:F4}, {c.Force,12:F4}").ToList();
                // 3ペア/行で出力
                for (int i = 0; i < pairs.Count; i += 3)
                    writer.WriteLine($"         {string.Join(", ", pairs.Skip(i).Take(3))}");
            }
            writer.WriteLine();
        }

        /// <summary>
        /// ばね (弾性連結) を書く。書けないばねは <see cref="BuildLinkPlan"/> が見つけて出力を止めている。
        /// 杭頭の連結ばね (ペナルティ) の参照はここで確かめる。
        /// </summary>
        private void WriteElasticLinks(StreamWriter writer, ExportContext ctx)
        {
            var nodeIdMap = ctx.NodeIdMap;
            var plan = ctx.Links;
            var penalty = _anaModel.PenaltySprings ?? [];
            if (plan.SoilLinks.Count == 0 && plan.PileHeads.Count == 0 && penalty.Count == 0) return;

            // 書く前に全部確かめる (途中まで書いてから止めても、一時ファイルなので保存先は前の内容のまま)。
            var problems = new List<string>();
            string? NodeProblem(Node? n, string side)
                => n == null ? $"{side}の節点がありません"
                 : !nodeIdMap.ContainsKey(n) ? $"{side}の節点 ({NodeLabel(n)}) が解析モデルの節点にありません"
                 : null;
            for (int i = 0; i < penalty.Count; i++)
            {
                var s = penalty[i];
                string? p = s == null ? "ばねがありません (空の要素)"
                    : NodeProblem(s.NodeI, "始端") ?? NodeProblem(s.NodeJ, "終端");
                if (p != null) problems.Add($"・杭頭の連結ばね{SpringLabel(s, i)}: {p}");
            }
            ThrowIfCannotWrite("ばね", "個", problems, "ばねの欠けたモデル (地盤ばね・杭頭の連結が無い) になります");

            writer.WriteLine("*ELASTICLINK    ; Elastic Link");
            writer.WriteLine("; iNO, iNODE1, iNODE2, LINK, ANGLE, DIR, FUNCTION, bSHEAR, DRENDI, GROUP                         ; MULTI LINEAR");
            writer.WriteLine("; iNO, iNODE1, iNODE2, LINK, ANGLE, R_SDx, R_SDy, R_SDz, R_SRx, R_SRy, R_SRz, SDx, SDy, SDz, SRx, SRy, SRz, bSHEAR, DRy, DRz, GROUP ; GEN");
            int linkId = 1;

            // 地盤ばね: MULTI LINEAR。連結の軸 (DIR 0) は杭側の節点から地盤側の節点へ向く
            // (X: 地盤節点を X に +1 m、Y: 仮想節点を Y に +1 m、Z: 仮想節点を Z に +1 m ずらしてある)
            foreach (var direction in new[] { LinkDirection.X, LinkDirection.Y, LinkDirection.Z })
                foreach (var link in plan.SoilLinks.Where(l => l.Direction == direction))
                {
                    writer.WriteLine($"   {linkId,5}, {nodeIdMap[link.PileSide],5}, {link.GroundNodeId,5}, MULTI LINEAR, 0, 0, {link.FunctionId}, NO, 0.5, ");
                    linkId++;
                }

            // 杭頭の回転ばね: キャップ節点と杭頭節点を GEN で結ぶ (解析の RotationalSpring と同じ剛性)。
            // 水平 2 方向とねじりは強く結び (解析のペナルティと同じ 1e8)、鉛直は剛体連結 (杭頭節点の Uz は代表節点に従う) に任せて 0。
            // 回転 (Rx・Ry) は杭頭の回転剛性。以前は書いておらず、杭頭とキャップが MGT ではつながっていなかった。
            foreach (var head in plan.PileHeads)
            {
                var s = head.Spring;
                double sdx = s.TieUx ? PileHeadTie : 0, sdy = s.TieUy ? PileHeadTie : 0, sdz = s.TieUz ? PileHeadTie : 0;
                double srz = s.TieRz ? PileHeadTie : 0;
                writer.WriteLine($"   {linkId,5}, {nodeIdMap[s.NodeI],5}, {nodeIdMap[s.NodeJ],5}, GEN, 0, NO, NO, NO, NO, NO, NO, "
                                 + $"{V(sdx)}, {V(sdy)}, {V(sdz)}, {V(head.KTheta)}, {V(head.KTheta)}, {V(srz)}, NO, 0.5, 0.5, ");
                linkId++;
            }

            // ペナルティばね: RIGID（同位置接続を許容、杭頭の剛体連結用）
            foreach (var spring in penalty)
            {
                int nodeI = nodeIdMap[spring.NodeI], nodeJ = nodeIdMap[spring.NodeJ];

                // RIGID形式: iNO, iNODE1, iNODE2, LINK, ANGLE, bSHEAR, DRy, DRz, GROUP
                writer.WriteLine($"   {linkId,5}, {nodeI,5}, {nodeJ,5}, RIGID, 0, NO, 0, 0, ");
                linkId++;
            }
            writer.WriteLine();
        }
    }
}
