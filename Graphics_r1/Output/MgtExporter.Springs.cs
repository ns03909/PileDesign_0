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

            // 液状化の低減率 βL は掛けない。mgt はモデルを 1 つ書き出す形式で、どの荷重ケース
            // (レベル・液状化の有無) の状態を書くかを持たないため、非液状化のばねを出力する。
            //
            // 節点 → その節点を持つ杭の群杭の影響 (群杭係数 ξ・杭間隔比 R/B)。
            // ξ・R/B は杭配置ごとの入力で、反力項目 (土層-杭セットで共有) には入っていないため、
            // 杭から引き当てる。引き当てられない節点は単杭扱い (None)。
            var effectByNode = new Dictionary<Node, GroupPileEffect>(ReferenceEqualityComparer.Instance);
            if (_anaModel.InputModel?.PileLayoutItems != null)
            {
                foreach (var pile in _anaModel.InputModel.PileLayoutItems)
                {
                    if (pile?.PileNodes == null) continue;
                    var pileEffect = GroupPileEffect.For(pile);
                    foreach (var node in pile.PileNodes)
                    {
                        if (node != null) effectByNode[node] = pileEffect;
                    }
                }
            }

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

                var effect = GroupPileEffect.None;
                if (spring.NodeI != null && effectByNode.TryGetValue(spring.NodeI, out var found)) effect = found;

                double py = reaction?.GetPyFor(isTop: true, isFront: true, effect) ?? 0;
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
                        double p = reaction.GetP(y, py, kh0: reaction.GetKh0For(effect)); // kN/m2
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

        /// <summary>
        /// ばね (弾性リンク) を書く。<b>書けないばねが 1 つでもあれば、ばねと理由を示して出力を止める</b>
        /// (<see cref="InvalidOperationException"/>。梁 (<see cref="WriteElements"/>) と同じ扱い)。
        ///
        /// 以前は節点・荷重-変形関数・Y 方向の仮想節点が引けないばねを黙って飛ばしていた。出力は成功するので、
        /// 参照が壊れた解析モデルでは、地盤ばね・杭頭の連結が欠けた MGT ファイルを正常な成果物として渡していた。
        /// </summary>
        private void WriteElasticLinks(StreamWriter writer, ExportContext ctx)
        {
            var nodeIdMap = ctx.NodeIdMap;
            var funcIds = ctx.SpringFunctionIds;
            var springYNodeIds = ctx.SpringYNodeIds;

            bool hasHorizontal = _anaModel.HorizontalSoilSprings != null && _anaModel.HorizontalSoilSprings.Count > 0;
            bool hasPenalty = _anaModel.PenaltySprings != null && _anaModel.PenaltySprings.Count > 0;
            if (!hasHorizontal && !hasPenalty) return;

            // 書く前に全部確かめる (途中まで書いてから止めても、一時ファイルなので保存先は前の内容のまま)。
            var problems = new List<string>();
            string? NodeProblem(Node? n, string side)
                => n == null ? $"{side}の節点がありません"
                 : !nodeIdMap.ContainsKey(n) ? $"{side}の節点 ({NodeLabel(n)}) が解析モデルの節点にありません"
                 : null;
            bool hasY = HasYDirectionAnalysis();
            if (hasHorizontal)
                for (int i = 0; i < _anaModel.HorizontalSoilSprings!.Count; i++)
                {
                    var s = _anaModel.HorizontalSoilSprings[i];
                    string? p = s == null ? "ばねがありません (空の要素)"
                        : NodeProblem(s.NodeI, "杭側") ?? NodeProblem(s.NodeJ, "地盤側")
                          ?? (!funcIds.ContainsKey(s) ? "荷重-変形関数が割り当てられていません" : null)
                          ?? (hasY && !springYNodeIds.ContainsKey(s) ? "Y 方向の仮想地盤節点が割り当てられていません" : null);
                    if (p != null) problems.Add($"・水平地盤ばね{SpringLabel(s, i)}: {p}");
                }
            if (hasPenalty)
                for (int i = 0; i < _anaModel.PenaltySprings!.Count; i++)
                {
                    var s = _anaModel.PenaltySprings[i];
                    string? p = s == null ? "ばねがありません (空の要素)"
                        : NodeProblem(s.NodeI, "始端") ?? NodeProblem(s.NodeJ, "終端");
                    if (p != null) problems.Add($"・杭頭の連結ばね{SpringLabel(s, i)}: {p}");
                }
            ThrowIfCannotWrite("ばね", "個", problems, "ばねの欠けたモデル (地盤ばね・杭頭の連結が無い) になります");

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
                    // 参照は上で確かめ済み (欠けていれば出力を止めている)。
                    int nodeI = nodeIdMap[spring.NodeI], nodeJ = nodeIdMap[spring.NodeJ], fid = funcIds[spring];
                    writer.WriteLine($"   {linkId,5}, {nodeI,5}, {nodeJ,5}, MULTI LINEAR, 0, 0, {fid}, NO, 0.5, ");
                    linkId++;
                }
                // Y方向リンク（軸方向=DIR 0、仮想Y節点にY方向オフセット）
                foreach (var spring in _anaModel.HorizontalSoilSprings)
                {
                    // Y 方向を解析していなければ仮想節点は作らない (Y 方向のリンクは書かない)。
                    if (!springYNodeIds.TryGetValue(spring, out int yNodeId)) continue;
                    int nodeI = nodeIdMap[spring.NodeI], fid = funcIds[spring];

                    writer.WriteLine($"   {linkId,5}, {nodeI,5}, {yNodeId,5}, MULTI LINEAR, 0, 0, {fid}, NO, 0.5, ");
                    linkId++;
                }
            }

            // ペナルティばね: RIGID（同位置接続を許容、杭頭の剛体連結用）
            if (hasPenalty && _anaModel.PenaltySprings != null)
            {
                foreach (var spring in _anaModel.PenaltySprings)
                {
                    int nodeI = nodeIdMap[spring.NodeI], nodeJ = nodeIdMap[spring.NodeJ];

                    // RIGID形式: iNO, iNODE1, iNODE2, LINK, ANGLE, bSHEAR, DRy, DRz, GROUP
                    writer.WriteLine($"   {linkId,5}, {nodeI,5}, {nodeJ,5}, RIGID, 0, NO, 0, 0, ");
                    linkId++;
                }
            }
            writer.WriteLine();
        }
    }
}
