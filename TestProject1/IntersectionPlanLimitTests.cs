using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Media.Media3D;

namespace TestProject1;

[TestClass]
public class IntersectionPlanLimitTests
{
    [TestMethod]
    public void SplitPlanStopsAtRemainingGenerationBudget()
    {
        using var vm = new MainWindowViewModel();
        var beam = new FoundationBeam { NodeI_Type = NodeReferenceType.GeneralNode, NodeI_Id = Guid.NewGuid(),
            NodeJ_Type = NodeReferenceType.GeneralNode, NodeJ_Id = Guid.NewGuid() };
        var positions = new Dictionary<(NodeReferenceType, Guid), Point3D>
        { [(beam.NodeI_Type,beam.NodeI_Id)] = new Point3D(0,0,0), [(beam.NodeJ_Type,beam.NodeJ_Id)] = new Point3D(10,0,0) };
        var points = new List<(NodeReferenceType, Guid, double)>();
        for (int i=1;i<=3;i++)
        {
            var id = Guid.NewGuid(); points.Add((NodeReferenceType.GeneralNode,id,i*0.2));
            positions[(NodeReferenceType.GeneralNode,id)] = new Point3D(i*2,0,0);
        }
        var method = typeof(MainWindowViewModel).GetMethod("SplitBeamAtPoints", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var failure = Assert.ThrowsException<TargetInvocationException>(() => method.Invoke(vm,[beam,points,positions,CancellationToken.None,2]));
        Assert.IsInstanceOfType(failure.InnerException, typeof(ArgumentException));
        var result = (List<FoundationBeam>)method.Invoke(vm,[beam,points,positions,CancellationToken.None,4])!;
        Assert.AreEqual(4,result.Count);
        Assert.IsFalse(vm.HasUnsavedWork);
    }
}
