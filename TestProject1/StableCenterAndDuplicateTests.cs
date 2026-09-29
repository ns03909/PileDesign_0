using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
namespace TestProject1;
[TestClass]
public class StableCenterAndDuplicateTests
{
    [TestMethod] public void CentroidRemainsFiniteForExtremeCoordinates()
    {
        var model=new InputModel {PileLayoutItems=[new PileLayoutDataItem {X=double.MaxValue,Y=double.MaxValue},new PileLayoutDataItem {X=double.MaxValue,Y=-double.MaxValue}]};
        Assert.AreEqual(double.MaxValue,model.GetCentroid().X);Assert.AreEqual(0.0,model.GetCentroid().Y);
    }
    [TestMethod] public void WeightedCenterMatchesOrdinaryFormula()
    {
        var rows=new[]{(new Point3D(2,4,6),2.0),(new Point3D(8,10,12),3.0)};
        var actual=StableNumerics.WeightedCenter(rows);Assert.AreEqual(5.6,actual.X,1e-13);Assert.AreEqual(7.6,actual.Y,1e-13);Assert.AreEqual(9.6,actual.Z,1e-13);
    }
    [TestMethod] public void WeightedCenterAvoidsOverflowInLoadsAndCoordinateProducts()
    {
        var actual=StableNumerics.WeightedCenter(new[]{(new Point3D(double.MaxValue,4,0),double.MaxValue),(new Point3D(double.MaxValue,8,0),double.MaxValue)});
        Assert.AreEqual(double.MaxValue,actual.X);Assert.AreEqual(6.0,actual.Y);
        var tiny=StableNumerics.WeightedCenter(new[]{(new Point3D(1e-300,0,0),1.0),(new Point3D(0,0,0),-1.0+1e-15)});
        Assert.IsTrue(double.IsFinite(tiny.X));Assert.IsTrue(tiny.X>0);
    }
    [TestMethod] public void ZeroAndSignedWeightsKeepExistingMeaning()
    {
        Assert.AreEqual(new Point3D(),StableNumerics.WeightedCenter(new[]{(new Point3D(1,0,0),1.0),(new Point3D(2,0,0),-1.0)}));
        Assert.AreEqual(0.0,StableNumerics.WeightedCenter(new[]{(new Point3D(1,0,0),2.0),(new Point3D(2,0,0),-1.0)}).X);
        Assert.ThrowsException<ArgumentException>(()=>StableNumerics.WeightedCenter(new[]{(new Point3D(),double.NaN)}));
    }
    [TestMethod] public void PlannerMatchesOriginalGreedyAlgorithmIncludingBoundaryAndChains()
    {
        var random=new Random(42);var nodes=new List<InputNode>();
        for(int i=0;i<400;i++) {double x=random.Next(0,10)*1e-6;double y=random.Next(0,10)*1e-6;nodes.Add(new InputNode {X=x,Y=y,Z=random.Next(0,5)*1e-6});}
        nodes.AddRange(new[]{new InputNode {X=100},new InputNode {X=100+0.75e-6},new InputNode {X=100+1.5e-6},new InputNode {X=0},new InputNode {X=1e-6}});
        var expected=new Dictionary<Guid,Guid>();var removed=new HashSet<int>();
        for(int i=0;i<nodes.Count;i++) {if(removed.Contains(i))continue;for(int j=i+1;j<nodes.Count;j++) {if(removed.Contains(j))continue;if(Math.Abs(nodes[i].X-nodes[j].X)<1e-6&&Math.Abs(nodes[i].Y-nodes[j].Y)<1e-6&&Math.Abs(nodes[i].Z-nodes[j].Z)<1e-6){expected[nodes[j].UniqueId]=nodes[i].UniqueId;removed.Add(j);}}}
        var actual=DuplicateNodePlanner.Build(nodes);Assert.AreEqual(expected.Count,actual.Count);foreach(var pair in expected)Assert.AreEqual(pair.Value,actual[pair.Key]);
    }
    [TestMethod] public void PlannerHandlesLargeDistinctAndCoincidentInputs()
    {
        var nodes=Enumerable.Range(0,20000).Select(i=>new InputNode {Y=i}).ToArray();Assert.AreEqual(0,DuplicateNodePlanner.Build(nodes).Count);
        foreach(var node in nodes)node.Y=0;var map=DuplicateNodePlanner.Build(nodes);Assert.AreEqual(nodes.Length-1,map.Count);Assert.IsTrue(map.Values.All(id=>id==nodes[0].UniqueId));
    }
}
