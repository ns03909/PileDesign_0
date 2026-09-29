using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;

namespace PileDesign.Services;

/// <summary>Indexes nodes along their widest axis; resolves by type priority, distance, then stable ID.</summary>
internal sealed class NodePositionIndex
{
    private readonly record struct Entry(double Coordinate, long Sequence, (NodeReferenceType Type, Guid Id, Point3D Pos) Node);
    private readonly SortedSet<Entry> _nodes = new(Comparer<Entry>.Create((a,b) =>
    {
        int order = a.Coordinate.CompareTo(b.Coordinate);
        return order != 0 ? order : a.Sequence.CompareTo(b.Sequence);
    }));
    private long _sequence;
    private readonly int _axis;
    private double Coordinate(Point3D p) => _axis == 0 ? p.X : _axis == 1 ? p.Y : p.Z;
    public NodePositionIndex(IEnumerable<(NodeReferenceType Type, Guid Id, Point3D Pos)> nodes,
        System.Threading.CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var snapshot = new List<(NodeReferenceType Type, Guid Id, Point3D Pos)>();
        foreach (var node in nodes)
        {
            token.ThrowIfCancellationRequested();
            if (double.IsFinite(node.Pos.X) && double.IsFinite(node.Pos.Y) && double.IsFinite(node.Pos.Z)) snapshot.Add(node);
        }
        _axis = SpatialAxis.Choose(snapshot.Select(n => n.Pos), token);
        foreach (var node in snapshot) { token.ThrowIfCancellationRequested(); Add(node); }
    }
    public void Add((NodeReferenceType Type, Guid Id, Point3D Pos) node)
    {
        if (!double.IsFinite(node.Pos.X) || !double.IsFinite(node.Pos.Y) || !double.IsFinite(node.Pos.Z)) return;
        _nodes.Add(new Entry(Coordinate(node.Pos), _sequence++, node));
    }
    private static int Priority(NodeReferenceType type) => type switch
    { NodeReferenceType.PileLayout => 0, NodeReferenceType.GeneralNode => 1, _ => 2 };

    public (NodeReferenceType Type, Guid Id, Point3D Pos)? Find(Point3D point, double tolerance,
        System.Threading.CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z) ||
            !double.IsFinite(tolerance) || tolerance < 0) return null;
        double min = Coordinate(point) - tolerance, max = Coordinate(point) + tolerance;
        (NodeReferenceType Type, Guid Id, Point3D Pos)? best = null;
        double bestDistance = double.PositiveInfinity;
        foreach (var entry in _nodes.GetViewBetween(new Entry(min, long.MinValue, default), new Entry(max, long.MaxValue, default)))
        {
            var node = entry.Node;
            token.ThrowIfCancellationRequested();
            if (Math.Abs(node.Pos.X - point.X) > tolerance || Math.Abs(node.Pos.Y - point.Y) > tolerance || Math.Abs(node.Pos.Z - point.Z) > tolerance) continue;
            double distance = (node.Pos - point).Length;
            if (!double.IsFinite(distance) || distance > tolerance) continue;
            if (best == null || Priority(node.Type) < Priority(best.Value.Type) ||
                (Priority(node.Type) == Priority(best.Value.Type) &&
                (distance < bestDistance || (distance == bestDistance && node.Id.CompareTo(best.Value.Id) < 0))))
            { best = node; bestDistance = distance; }
        }
        return best;
    }
}
