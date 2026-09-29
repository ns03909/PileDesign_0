using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Linq;
using System.Reflection;

namespace TestProject1;

[TestClass]
public class BeamIntersectionSplitTests
{
    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ThreeBeamsShareOneIntersection(bool existingNode, bool pileIntersection)
    {
        var input = new InputModel { InputNodes = [], PileLayoutItems = [],
            FoundationBeamInput = new FoundationBeamInput { Beams = [], Nodes = [] } };
        var vm = new MainWindowViewModel { CurrentInputModel = input, EditDistanceThreshold = 0 };
        Guid centerId = Guid.Empty;
        var centerType = NodeReferenceType.GeneralNode;
        if (existingNode)
        {
            if (pileIntersection)
            {
                var pile = new PileLayoutDataItem { PileNo = 1, X = 0, Y = 0, Z = 0 };
                input.PileLayoutItems.Add(pile);
                centerId = pile.UniqueId;
                centerType = NodeReferenceType.PileLayout;
            }
            else
            {
                var node = new InputNode { Type = NodeType.General };
                input.InputNodes.Add(node);
                centerId = node.UniqueId;
            }
        }
        foreach (var (x, y) in new[] { (2.0, 0.0), (0.0, 2.0), (2.0, 2.0) })
        {
            var a = new InputNode { X = -x, Y = -y, Type = NodeType.General };
            var b = new InputNode { X = x, Y = y, Type = NodeType.General };
            input.InputNodes.Add(a);
            input.InputNodes.Add(b);
            input.FoundationBeamInput.Beams.Add(new FoundationBeam
            {
                NodeI_Type = NodeReferenceType.GeneralNode, NodeI_Id = a.UniqueId,
                NodeJ_Type = NodeReferenceType.GeneralNode, NodeJ_Id = b.UniqueId,
                IsSelected = true, IsVisible = false, MaterialNo = 4, SectionNo = 5,
                SectionName = "custom", Width = 1.2, Height = 2.3,
                YoungModulus = 123, ShearModulus = 45, AngleBeta = 30,
            });
        }
        typeof(MainWindowViewModel).GetMethod("SplitElementsAtIntersections", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        Assert.AreEqual(pileIntersection ? 6 : 7, input.InputNodes.Count);
        Assert.AreEqual(6, input.FoundationBeamInput.Beams.Count);
        if (!existingNode) centerId = input.InputNodes.Single(n => n.X == 0 && n.Y == 0).UniqueId;
        foreach (var beam in input.FoundationBeamInput.Beams)
        {
            Assert.IsTrue((beam.NodeI_Type == centerType && beam.NodeI_Id == centerId) ||
                (beam.NodeJ_Type == centerType && beam.NodeJ_Id == centerId));
            var a = input.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id)!.Value;
            var b = input.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id)!.Value;
            Assert.IsTrue(a.X != b.X || a.Y != b.Y || a.Z != b.Z);
            Assert.IsFalse(beam.IsVisible);
            Assert.AreEqual(4, beam.MaterialNo);
            Assert.AreEqual(5, beam.SectionNo);
            Assert.AreEqual("custom", beam.SectionName);
            Assert.AreEqual(1.2, beam.Width);
            Assert.AreEqual(2.3, beam.Height);
            Assert.AreEqual(123.0, beam.YoungModulus);
            Assert.AreEqual(45.0, beam.ShearModulus);
            Assert.AreEqual(30.0, beam.AngleBeta);
        }
    }
}
