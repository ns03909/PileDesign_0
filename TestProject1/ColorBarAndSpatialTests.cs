using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using PileDesign.ViewModels;
using PileDesign.Models;
using PileDesign.Models.InputData;
using System;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace TestProject1;

[TestClass]
public class ColorBarAndSpatialTests
{
    [TestMethod]
    public void AxisAndNodeIndexPreparationCanBeCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        System.Collections.Generic.IEnumerable<Point3D> Points()
        {
            yield return new Point3D();
            cancellation.Cancel();
            yield return new Point3D(0,1,0);
        }
        Assert.ThrowsException<OperationCanceledException>(() => SpatialAxis.Choose(Points(), cancellation.Token));
        using var other = new CancellationTokenSource();
        System.Collections.Generic.IEnumerable<(NodeReferenceType, Guid, Point3D)> Nodes()
        {
            yield return (NodeReferenceType.GeneralNode, Guid.NewGuid(), new Point3D());
            other.Cancel();
            yield return (NodeReferenceType.GeneralNode, Guid.NewGuid(), new Point3D(0,1,0));
        }
        Assert.ThrowsException<OperationCanceledException>(() => new NodePositionIndex(Nodes(), other.Token));
    }

    [TestMethod]
    public void DynamicNodeInsertionPreservesNearestAndTypePriority()
    {
        var index = new NodePositionIndex([]);
        var expected = Guid.NewGuid();
        for (int i=9999;i>=0;i--) index.Add((NodeReferenceType.GeneralNode, i == 5000 ? expected : Guid.NewGuid(), new Point3D(i,0,0)));
        Assert.AreEqual(expected, index.Find(new Point3D(5000.01,0,0), 0.1)!.Value.Id);
        var pile = Guid.NewGuid(); index.Add((NodeReferenceType.PileLayout,pile,new Point3D(5000.05,0,0)));
        Assert.AreEqual(pile, index.Find(new Point3D(5000,0,0), 0.1)!.Value.Id);
    }

    [TestMethod]
    public void TinyRangesRetainRainbowAndDivergingColors()
    {
        var rainbow = ColorBarUtils.GetColorBarGeometries([1e-20, 2e-20], mode: ColorBarUtils.ColorBarMode.Rainbow);
        Assert.IsTrue(rainbow.Select(b => b.Color).Distinct().Count() > 3);
        foreach (double scale in new[] {1e-20, double.Epsilon})
        {
            var diverging = ColorBarUtils.GetColorBarGeometries([-scale,scale]);
            Assert.IsTrue(diverging[0].Color.B > diverging[0].Color.R);
            Assert.IsTrue(diverging[^1].Color.R > diverging[^1].Color.B);
        }
        var subnormal = ColorBarUtils.GetColorBarGeometries([0.0, double.Epsilon]);
        Assert.AreEqual(0.0, subnormal[0].BottomRange);
        Assert.AreEqual(double.Epsilon, subnormal[^1].TopRange);
    }
    [TestMethod]
    public void NodeMatchingCanBeCancelledDuringPlanConstruction()
    {
        var id = Guid.NewGuid();
        var index = new NodePositionIndex([(NodeReferenceType.GeneralNode, id, new Point3D(0, 20, 0)),
            (NodeReferenceType.GeneralNode, Guid.NewGuid(), new Point3D(0, 40, 0))]);
        Assert.AreEqual(id, index.Find(new Point3D(0, 20, 0), 0)!.Value.Id);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsException<OperationCanceledException>(() => index.Find(new Point3D(0, 20, 0), 1, cancellation.Token));
    }

    [TestMethod]
    public void ExtremeRangesStayFiniteAndBounded()
    {
        foreach (var values in new[] { new[] {-double.MaxValue, double.MaxValue},
            new[] {0.0, double.Epsilon}, new[] {double.MaxValue / 2, double.MaxValue}, new[] {1.0, 20.0} })
        {
            var bands = ColorBarUtils.GetColorBarGeometries(values, int.MaxValue);
            Assert.IsTrue(bands.Count > 0 && bands.Count <= 256);
            Assert.IsTrue(bands.All(b => double.IsFinite(b.BottomRange) && double.IsFinite(b.TopRange) && b.TopRange > b.BottomRange));
            Assert.IsTrue(bands[0].BottomRange <= values.Min());
            Assert.IsTrue(bands[^1].TopRange >= values.Max());
            for (int i=1;i<bands.Count;i++) Assert.AreEqual(bands[i-1].TopRange, bands[i].BottomRange);
        }
    }

    [TestMethod]
    public void PaletteMatchesOriginalInterpolation()
    {
        string[] hex = ["#1F3FCB", "#1F9FE8", "#1FCB6F", "#E8E81F", "#E89F1F", "#CB1F1F"];
        var colors = hex.Select(h => (Color)ColorConverter.ConvertFromString(h)).ToArray();
        for (int n=0;n<=1000;n++)
        {
            double value = n / 1000.0;
            int i = Enumerable.Range(0,5).First(i => value >= i / 5.0 && value <= (i+1) / 5.0);
            double t = (value - i / 5.0) / ((i+1) / 5.0 - i / 5.0);
            var expected = Color.FromRgb((byte)(colors[i].R+t*(colors[i+1].R-colors[i].R)),
                (byte)(colors[i].G+t*(colors[i+1].G-colors[i].G)), (byte)(colors[i].B+t*(colors[i+1].B-colors[i].B)));
            Assert.AreEqual(expected, ColorBar.GetColor(value));
        }
    }

    private sealed class Capture : IProgress<AnalysisProgress>
    {
        public AnalysisProgress? Last;
        public void Report(AnalysisProgress p) => Last = p;
    }

    [TestMethod]
    public void SweepPrunesAlongYAndZ()
    {
        foreach (int axis in new[] {1,2})
        {
            Point3D Point(double v) => axis == 1 ? new Point3D(0,v,0) : new Point3D(0,0,v);
            var segments = Enumerable.Range(0,1000).Select(i => (Point(i*10),Point(i*10+1))).ToArray();
            var progress = new Capture();
            Assert.AreEqual(0, MainWindowViewModel.SearchBeamIntersections(segments,0,CancellationToken.None,progress).Count);
            StringAssert.Contains(progress.Last!.CurrentStep, "詳細判定 0");
        }
    }
}
