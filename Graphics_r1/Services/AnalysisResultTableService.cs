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

            var beams = model.Beams?.ToList() ?? new List<PileDesign.FEM.Beam>();
            var nodes = model.Nodes?.ToList() ?? new List<PileDesign.FEM.Node>();

            if (beams.Count > 0)
            {
                var forceRows = beams
                    .Select((beam, idx) =>
                    {
                        int n1 = nodes.IndexOf(beam.NodeI) + 1;
                        int n2 = nodes.IndexOf(beam.NodeJ) + 1;
                        var bf = beam.CumulativeForce ?? new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                        return ElementSectionForceRow.From(idx + 1, n1, n2, beam, bf);
                    })
                    .Cast<object>()
                    .ToList();

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

            if (beams.Count > 0)
            {
                var dispRows = beams
                    .Select((beam, idx) =>
                    {
                        int n1 = nodes.IndexOf(beam.NodeI) + 1;
                        int n2 = nodes.IndexOf(beam.NodeJ) + 1;
                        var bd = beam.CumulativeDisp ?? new BeamDisp(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                        return ElementSectionDispRow.From(idx + 1, n1, n2, beam, bd);
                    })
                    .Cast<object>()
                    .ToList();

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

            if (nodes.Count > 0)
            {
                var nodeForceRows = nodes
                    .Select((node, idx) =>
                    {
                        var react = node.CumulativeReaction ?? new NodeReaction(0, 0, 0, 0, 0, 0);
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
                        var disp = node.CumulativeDisp ?? new NodeDisp(0, 0, 0, 0, 0, 0);
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