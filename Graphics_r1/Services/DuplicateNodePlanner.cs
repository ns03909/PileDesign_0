using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services;

internal static class DuplicateNodePlanner
{
    internal static Dictionary<Guid, Guid> Build(IReadOnlyList<InputNode> nodes, double tolerance = 1e-6)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        int axis = SpatialAxis.Choose(nodes.Select(n => n.Point3D));
        double Coordinate(InputNode n) => axis == 0 ? n.X : axis == 1 ? n.Y : n.Z;
        var candidates = new SortedSet<(double Coordinate, int Index)>();
        for (int i = 0; i < nodes.Count; i++) candidates.Add((Coordinate(nodes[i]), i));
        var merged = new HashSet<int>();
        var map = new Dictionary<Guid, Guid>();
        // Iterate in original order: keep the same first surviving node as the former nested loops.
        for (int i = 0; i < nodes.Count; i++)
        {
            if (merged.Contains(i)) continue;
            var node = nodes[i];
            var nearby = candidates.GetViewBetween((Coordinate(node) - tolerance, int.MinValue),
                (Coordinate(node) + tolerance, int.MaxValue)).ToArray();
            foreach (var candidate in nearby)
            {
                int j = candidate.Index;
                if (j <= i || Math.Abs(node.X - nodes[j].X) >= tolerance ||
                    Math.Abs(node.Y - nodes[j].Y) >= tolerance || Math.Abs(node.Z - nodes[j].Z) >= tolerance) continue;
                map[nodes[j].UniqueId] = node.UniqueId;
                merged.Add(j);
                candidates.Remove(candidate);
            }
            candidates.Remove((Coordinate(node), i));
        }
        return map;
    }
}
