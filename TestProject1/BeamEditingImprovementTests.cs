using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Media.Media3D;

namespace TestProject1;

[TestClass]
[DoNotParallelize]
public class BeamEditingImprovementTests
{
    [TestMethod] public void ProgressWindowCompletesTheSearchAndCloses()
    {
        bool unattended = MessageService.IsUnattended; MessageService.IsUnattended = false;
        try
        {
            var error = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var segments = Enumerable.Range(0, 100).Select(i => (new Point3D(i * 10, 0, 0), new Point3D(i * 10 + 1, 0, 0))).ToArray();
                var vm = new MainWindowViewModel();
                var method = typeof(MainWindowViewModel).GetMethod("RunIntersectionSearch", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var result = (System.Collections.Generic.List<MainWindowViewModel.BeamIntersection>)method.Invoke(vm, [segments, 0.005])!;
                Assert.AreEqual(0, result.Count);
            }, out bool timedOut);
            Assert.IsFalse(timedOut); Assert.IsNull(error, error?.ToString());
        }
        finally { MessageService.IsUnattended = unattended; }
    }

    [TestMethod] public void NearestNodeRespectsTypePriorityAndStableTieBreak()
    {
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var general = Guid.NewGuid();
        (NodeReferenceType Type, Guid Id, Point3D Pos)[] nodes =
        [ (NodeReferenceType.PileLayout, high, new Point3D(0.8, 0, 0)),
          (NodeReferenceType.GeneralNode, general, new Point3D(0, 0, 0)),
          (NodeReferenceType.PileLayout, low, new Point3D(0.2, 0, 0)) ];
        foreach (var candidates in new[] { nodes, nodes.Reverse().ToArray() })
            Assert.AreEqual(low, new NodePositionIndex(candidates).Find(new Point3D(), 1)!.Value.Id);
        var index = new NodePositionIndex([(NodeReferenceType.GeneralNode, high, new Point3D(0.2, 0, 0)),
            (NodeReferenceType.GeneralNode, low, new Point3D(-0.2, 0, 0))]);
        Assert.AreEqual(low, index.Find(new Point3D(), 1)!.Value.Id);
        Assert.IsNull(index.Find(new Point3D(10, 0, 0), 1));
        index.Add((NodeReferenceType.GeneralNode, general, new Point3D(10, 0, 0)));
        Assert.AreEqual(general, index.Find(new Point3D(10, 0, 0), 0)!.Value.Id);
    }

    [TestMethod] public void DuplicateDetectionUsesVisibilityAndIgnoresAnalysisAngle()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var first = new FoundationBeam { NodeI_Id = a, NodeJ_Id = b, MemberAngle = 10 };
        var analysed = first.CreateSegment(first.NodeI_Type, a, first.NodeJ_Type, b); analysed.MemberAngle = 30;
        var hidden = first.CreateSegment(first.NodeI_Type, a, first.NodeJ_Type, b); hidden.IsVisible = false;
        var result = MainWindowViewModel.FindDuplicateBeams([first, analysed, hidden]);
        CollectionAssert.AreEqual(new[] { analysed }, result.ToRemove);
        Assert.AreEqual(1, result.Differing.Count); Assert.AreSame(hidden, result.Differing[0].Other);
    }

    [TestMethod] public void LongBeamKeepsDistinctNearbyAndNearEndSplitPoints()
    {
        bool unattended = MessageService.IsUnattended; MessageService.IsUnattended = true;
        try
        {
            var input = new InputModel { InputNodes = [], PileLayoutItems = [], FoundationBeamInput = new FoundationBeamInput { Beams = [] } };
            var vm = new MainWindowViewModel { CurrentInputModel = input, EditDistanceThreshold = 0.005 };
            var start = new InputNode { Type = NodeType.General, X = 0 }; var end = new InputNode { Type = NodeType.General, X = 1e6 };
            input.InputNodes.Add(start); input.InputNodes.Add(end);
            foreach (double x in new[] { 0.1, 500000.0, 500000.1 }) input.InputNodes.Add(new InputNode { Type = NodeType.General, X = x });
            input.FoundationBeamInput.Beams.Add(new FoundationBeam { IsSelected = true,
                NodeI_Type = NodeReferenceType.GeneralNode, NodeI_Id = start.UniqueId,
                NodeJ_Type = NodeReferenceType.GeneralNode, NodeJ_Id = end.UniqueId });
            vm.OnSplitElementsByNodes();
            Assert.AreEqual(4, input.FoundationBeamInput.Beams.Count);
        }
        finally { MessageService.IsUnattended = unattended; }
    }

    [TestMethod] public void ZeroToleranceKeepsCollinearDecimalsAndRejectsOffAxisPoints()
    {
        bool unattended = MessageService.IsUnattended; MessageService.IsUnattended = true;
        try
        {
            var input = new InputModel { InputNodes = [], PileLayoutItems = [], FoundationBeamInput = new FoundationBeamInput { Beams = [] } };
            var vm = new MainWindowViewModel { CurrentInputModel = input, EditDistanceThreshold = 0 };
            var start = new InputNode { Type = NodeType.General, X = 0 }; var end = new InputNode { Type = NodeType.General, X = 1e6 };
            input.InputNodes.Add(start); input.InputNodes.Add(end);
            foreach (double x in new[] { 0.1, 500000.0, 500000.1 }) input.InputNodes.Add(new InputNode { Type = NodeType.General, X = x });
            input.InputNodes.Add(new InputNode { Type = NodeType.General, X = 700000, Y = 1e-10 });
            input.FoundationBeamInput.Beams.Add(new FoundationBeam { IsSelected = true,
                NodeI_Type = NodeReferenceType.GeneralNode, NodeI_Id = start.UniqueId,
                NodeJ_Type = NodeReferenceType.GeneralNode, NodeJ_Id = end.UniqueId });
            vm.OnSplitElementsByNodes();
            Assert.AreEqual(4, input.FoundationBeamInput.Beams.Count);
        }
        finally { MessageService.IsUnattended = unattended; }
    }

    private sealed class ProgressCallback(Action<AnalysisProgress> callback) : IProgress<AnalysisProgress>
    { public void Report(AnalysisProgress value) => callback(value); }

    [TestMethod] public void SearchPrunesSeparatedSegmentsAndReportsWork()
    {
        var segments = Enumerable.Range(0, 1000).Select(i => (new Point3D(i * 10, 0, 0), new Point3D(i * 10 + 1, 0, 0))).ToArray();
        AnalysisProgress? last = null;
        var result = MainWindowViewModel.SearchBeamIntersections(segments, 0, CancellationToken.None, new ProgressCallback(p => last = p));
        Assert.AreEqual(0, result.Count); Assert.IsNotNull(last); Assert.AreEqual(100.0, last.Percentage);
        StringAssert.Contains(last.CurrentStep, "499,500");
        StringAssert.Contains(last.CurrentStep, "詳細判定 0");
    }

    [TestMethod] public void SearchCanBeCancelledDuringWork()
    {
        using var cancellation = new CancellationTokenSource();
        var segments = Enumerable.Range(0, 100).Select(i => (new Point3D(i, 0, 0), new Point3D(i + 1, 0, 0))).ToArray();
        Assert.ThrowsException<OperationCanceledException>(() => MainWindowViewModel.SearchBeamIntersections(segments, 0, cancellation.Token,
            new ProgressCallback(_ => cancellation.Cancel())));
    }

    [TestMethod] public void RangeFilteringPreservesSkewIntersectionsWithinTolerance()
    {
        var result = MainWindowViewModel.SearchBeamIntersections([
            (new Point3D(-1, 0, 0), new Point3D(1, 0, 0)),
            (new Point3D(0, -1, 0.004), new Point3D(0, 1, 0.004)),
            (new Point3D(100, 100, 100), new Point3D(101, 101, 101)) ], 0.005, CancellationToken.None);
        Assert.AreEqual(1, result.Count); Assert.AreEqual(0.5, result[0].TA); Assert.AreEqual(0.5, result[0].TB);
    }

    [TestMethod] public void ManyBeamsCanShareOnePointWithoutHittingTheGeneratedItemLimit()
    {
        var segments = Enumerable.Range(0, 500).Select(i =>
        {
            double angle = Math.PI * i / 500;
            return (new Point3D(-Math.Cos(angle), -Math.Sin(angle), 0), new Point3D(Math.Cos(angle), Math.Sin(angle), 0));
        }).ToArray();
        Assert.AreEqual(500 * 499 / 2, MainWindowViewModel.SearchBeamIntersections(segments, 0.005, CancellationToken.None).Count);
    }
}
