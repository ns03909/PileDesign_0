using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    public sealed class AnalysisResultTableService
    {
        public IReadOnlyList<ResultTable> BuildTables(
            AnaModel model,
            LoadCase? loadCase,
            LoadCombination? loadCombination,
            bool isLiquefaction,
            int step)
        {
            var tables = new List<ResultTable>();

            var beams = model.Beams?.ToList() ?? [];
            var nodes = model.Nodes?.ToList() ?? [];
            var rotSprings = model.RotationalSprings?.ToList() ?? [];

            // 結果検索用のヘルパー: BeamResultsから該当する結果を取得
            BeamResult? FindBeamResult(Beam beam) =>
                beam.BeamResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == isLiquefaction &&
                    r.Step == step &&
                    (loadCase == null || r.LoadCase?.LoadName == loadCase.LoadName) &&
                    (loadCombination == null || r.LoadCombination?.Name == loadCombination.Name));

            // 結果検索用のヘルパー: RotationalSpringResultsから該当する結果を取得
            RotationalSpringResult? FindRotSpringResult(RotationalSpring rs) =>
                rs.RotationalSpringResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == isLiquefaction &&
                    r.Step == step &&
                    (loadCase == null || r.LoadCase?.LoadName == loadCase.LoadName) &&
                    (loadCombination == null || r.LoadCombination?.Name == loadCombination.Name));

            // 結果検索用のヘルパー: NodeResultsから該当する結果を取得
            NodeResult? FindNodeResult(PileDesign.FEM.Node node) =>
                node.NodeResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == isLiquefaction &&
                    r.Step == step &&
                    (loadCase == null || r.LoadCase?.LoadName == loadCase.LoadName) &&
                    (loadCombination == null || r.LoadCombination?.Name == loadCombination.Name));

            if (beams.Count > 0 || rotSprings.Count > 0)
            {
                var forceRows = new List<object>();

                // 梁要素
                for (int idx = 0; idx < beams.Count; idx++)
                {
                    var beam = beams[idx];
                    int n1 = nodes.IndexOf(beam.NodeI) + 1;
                    int n2 = nodes.IndexOf(beam.NodeJ) + 1;
                    var result = FindBeamResult(beam);
                    var bf = result?.CumulativeForce ?? beam.CumulativeForce ?? new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    forceRows.Add(ElementSectionForceRow.From(idx + 1, n1, n2, beam, bf));
                }

                // リンク要素（杭頭接合ばね）
                for (int idx = 0; idx < rotSprings.Count; idx++)
                {
                    var rs = rotSprings[idx];
                    int n1 = nodes.IndexOf(rs.NodeI) + 1;
                    int n2 = nodes.IndexOf(rs.NodeJ) + 1;
                    var result = FindRotSpringResult(rs);
                    var bf = result?.CumulativeForce ?? rs.CumulativeForce ?? new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    forceRows.Add(ElementSectionForceRow.FromSpring(beams.Count + idx + 1, n1, n2, rs, bf));
                }

                tables.Add(new ResultTable
                {
                    Name = "梁断面力",
                    Category = "BeamForce",
                    Columns = ResultColumnReflectionCache.GetColumns(typeof(ElementSectionForceRow)),
                    Rows = forceRows,
                    LoadCaseName = loadCase?.LoadName ?? "",
                    LoadCombinationName = loadCombination?.Name ?? "",
                    IsLiquefaction = isLiquefaction
                });
            }

            if (beams.Count > 0 || rotSprings.Count > 0)
            {
                var dispRows = new List<object>();

                // 梁要素
                for (int idx = 0; idx < beams.Count; idx++)
                {
                    var beam = beams[idx];
                    int n1 = nodes.IndexOf(beam.NodeI) + 1;
                    int n2 = nodes.IndexOf(beam.NodeJ) + 1;
                    var result = FindBeamResult(beam);
                    var bd = result?.CumulativeDisp ?? beam.CumulativeDisp ?? new BeamDisp(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    dispRows.Add(ElementSectionDispRow.From(idx + 1, n1, n2, beam, bd));
                }

                // リンク要素（杭頭接合ばね）
                for (int idx = 0; idx < rotSprings.Count; idx++)
                {
                    var rs = rotSprings[idx];
                    int n1 = nodes.IndexOf(rs.NodeI) + 1;
                    int n2 = nodes.IndexOf(rs.NodeJ) + 1;
                    var result = FindRotSpringResult(rs);
                    var bd = result?.CumulativeDisp ?? rs.CumulativeDisp ?? new BeamDisp(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    dispRows.Add(ElementSectionDispRow.FromSpring(beams.Count + idx + 1, n1, n2, rs, bd));
                }

                tables.Add(new ResultTable
                {
                    Name = "梁断面変位",
                    Category = "BeamDisp",
                    Columns = ResultColumnReflectionCache.GetColumns(typeof(ElementSectionDispRow)),
                    Rows = dispRows,
                    LoadCaseName = loadCase?.LoadName ?? "",
                    LoadCombinationName = loadCombination?.Name ?? "",
                    IsLiquefaction = isLiquefaction
                });
            }

            // 杭頭応力テーブル（リンク要素の j 端 = PileNode 側）
            if (rotSprings.Count > 0)
            {
                var pileHeadRows = new List<object>();
                for (int idx = 0; idx < rotSprings.Count; idx++)
                {
                    var rs = rotSprings[idx];
                    var result = FindRotSpringResult(rs);
                    var bf = result?.CumulativeForce ?? rs.CumulativeForce ?? new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    pileHeadRows.Add(PileHeadForceRow.FromPileTop(idx + 1, rs, bf));
                }

                tables.Add(new ResultTable
                {
                    Name = "杭頭応力",
                    Category = "PileHeadForce",
                    Columns = ResultColumnReflectionCache.GetColumns(typeof(PileHeadForceRow)),
                    Rows = pileHeadRows,
                    LoadCaseName = loadCase?.LoadName ?? "",
                    LoadCombinationName = loadCombination?.Name ?? "",
                    IsLiquefaction = isLiquefaction
                });
            }

            // 杭接合節点応力テーブル（リンク要素の i 端 = CapNode 側）
            if (rotSprings.Count > 0)
            {
                var capNodeRows = new List<object>();
                for (int idx = 0; idx < rotSprings.Count; idx++)
                {
                    var rs = rotSprings[idx];
                    var result = FindRotSpringResult(rs);
                    var bf = result?.CumulativeForce ?? rs.CumulativeForce ?? new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    capNodeRows.Add(PileHeadForceRow.FromCapNode(idx + 1, rs, bf));
                }

                tables.Add(new ResultTable
                {
                    Name = "杭接合節点応力",
                    Category = "CapNodeForce",
                    Columns = ResultColumnReflectionCache.GetColumns(typeof(PileHeadForceRow)),
                    Rows = capNodeRows,
                    LoadCaseName = loadCase?.LoadName ?? "",
                    LoadCombinationName = loadCombination?.Name ?? "",
                    IsLiquefaction = isLiquefaction
                });
            }

            if (nodes.Count > 0)
            {
                var nodeForceRows = nodes
                    .Select((node, idx) =>
                    {
                        // 保存された結果から取得、なければ現在の値を使用
                        var result = FindNodeResult(node);
                        var react = result?.CumulativeReaction ?? node.CumulativeReaction ?? new NodeReaction(0, 0, 0, 0, 0, 0);
                        return NodeForceRow.From(idx + 1, node, react);
                    })
                    .Cast<object>()
                    .ToList();

                tables.Add(new ResultTable
                {
                    Name = "節点力",
                    Category = "NodeForce",
                    Columns = ResultColumnReflectionCache.GetColumns(typeof(NodeForceRow)),
                    Rows = nodeForceRows,
                    LoadCaseName = loadCase?.LoadName ?? "",
                    LoadCombinationName = loadCombination?.Name ?? "",
                    IsLiquefaction = isLiquefaction
                });
            }

            if (nodes.Count > 0)
            {
                var nodeDispRows = nodes
                    .Select((node, idx) =>
                    {
                        // 保存された結果から取得、なければ現在の値を使用
                        var result = FindNodeResult(node);
                        var disp = result?.CumulativeDisp ?? node.CumulativeDisp ?? new NodeDisp(0, 0, 0, 0, 0, 0);
                        return NodeDisplacementRow.From(idx + 1, node, disp);
                    })
                    .Cast<object>()
                    .ToList();

                tables.Add(new ResultTable
                {
                    Name = "節点変位",
                    Category = "NodeDisp",
                    Columns = ResultColumnReflectionCache.GetColumns(typeof(NodeDisplacementRow)),
                    Rows = nodeDispRows,
                    LoadCaseName = loadCase?.LoadName ?? "",
                    LoadCombinationName = loadCombination?.Name ?? "",
                    IsLiquefaction = isLiquefaction
                });
            }

            return tables;
        }
    }
}