using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Reflection;

namespace TestProject1;

[TestClass]
[DoNotParallelize]
public class BeamSplitGuardTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() { MessageService.IsUnattended = _unattended; }

    private static MainWindowViewModel Create() => new()
    { CurrentInputModel = new InputModel { InputNodes = [], PileLayoutItems = [], FoundationBeamInput = new FoundationBeamInput { Beams = [], Nodes = [] } } };
    private static FoundationBeam AddBeam(MainWindowViewModel vm, double x1, double y1, double x2, double y2)
    {
        var a = new InputNode { Type = NodeType.General, X = x1, Y = y1 };
        var b = new InputNode { Type = NodeType.General, X = x2, Y = y2 };
        vm.CurrentInputModel!.InputNodes.Add(a); vm.CurrentInputModel!.InputNodes.Add(b);
        var beam = new FoundationBeam { IsSelected = true, NodeI_Type = NodeReferenceType.GeneralNode, NodeI_Id = a.UniqueId,
            NodeJ_Type = NodeReferenceType.GeneralNode, NodeJ_Id = b.UniqueId, IsVisible = false };
        vm.CurrentInputModel!.FoundationBeamInput.Beams.Add(beam);
        return beam;
    }
    private static void Run(MainWindowViewModel vm, string method) =>
        typeof(MainWindowViewModel).GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(vm, null);

    [DataTestMethod]
    [DataRow("OnSplitElementsByNodes")]
    [DataRow("EqualDivideElements")]
    [DataRow("SplitElementsAtIntersections")]
    public void EmptySelectionDoesNotInvalidateOrSave(string method)
    {
        var vm = Create(); vm.MarkProjectReplaced(); vm.IsElementSplit = true;
        int version = vm.InputEditVersion;
        Run(vm, method);
        Assert.IsFalse(vm.HasUnsavedWork); Assert.IsTrue(vm.IsElementSplit);
        Assert.AreEqual(version, vm.InputEditVersion);
    }

    [DataTestMethod]
    [DataRow("OnSplitElementsByNodes")]
    [DataRow("EqualDivideElements")]
    [DataRow("SplitElementsAtIntersections")]
    public void MissingEndpointDoesNotPartiallyEdit(string method)
    {
        var vm = Create(); AddBeam(vm, -2, 0, 2, 0);
        var bad = AddBeam(vm, 0, -2, 0, 2); bad.NodeJ_Id = Guid.NewGuid();
        vm.MarkProjectReplaced(); int version = vm.InputEditVersion;
        Run(vm, method);
        Assert.AreEqual(4, vm.CurrentInputModel!.InputNodes.Count);
        Assert.AreEqual(2, vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);
        Assert.IsFalse(vm.HasUnsavedWork); Assert.AreEqual(version, vm.InputEditVersion);
    }

    [TestMethod] public void NoIntersectionAndNoIntermediateNodeLeaveHistoryUnchanged()
    {
        var vm = Create(); AddBeam(vm, 0, 0, 2, 0); AddBeam(vm, 0, 2, 2, 2);
        vm.MarkProjectReplaced(); vm.IsElementSplit = true; int version = vm.InputEditVersion;
        Run(vm, "OnSplitElementsByNodes"); Run(vm, "SplitElementsAtIntersections");
        Assert.IsTrue(vm.IsElementSplit); Assert.IsFalse(vm.HasUnsavedWork); Assert.AreEqual(version, vm.InputEditVersion);
    }

    [TestMethod] public void OverflowAfterPlanningIntersectionLeavesNoNewNodes()
    {
        var vm = Create(); AddBeam(vm, -2, 0, 2, 0); AddBeam(vm, 0, -2, 0, 2);
        AddBeam(vm, -1e100, 0, 1e100, 0); AddBeam(vm, 0, -1e100, 0, 1e100);
        vm.MarkProjectReplaced(); Run(vm, "SplitElementsAtIntersections");
        Assert.AreEqual(8, vm.CurrentInputModel!.InputNodes.Count); Assert.AreEqual(4, vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);
        Assert.IsFalse(vm.HasUnsavedWork);
    }

    [TestMethod] public void EqualDivisionLimitsAreCheckedBeforeGeneration()
    {
        Assert.IsNull(MoveCopyValidation.DescribeSplitCountProblem(1, 50000));
        Assert.IsNotNull(MoveCopyValidation.DescribeSplitCountProblem(1, 50001));
        Assert.IsNotNull(MoveCopyValidation.DescribeSplitCountProblem(int.MaxValue, int.MaxValue));
        var vm = Create(); AddBeam(vm, 0, 0, 2, 0); vm.EqualDivisionCount = int.MaxValue;
        vm.MarkProjectReplaced(); Run(vm, "EqualDivideElements");
        Assert.AreEqual(2, vm.CurrentInputModel!.InputNodes.Count); Assert.IsFalse(vm.HasUnsavedWork);
    }

    [TestMethod] public void CancelledSplitDoesNotCommitPlannedNodes()
    {
        var vm = Create(); AddBeam(vm, -2, 0, 2, 0); AddBeam(vm, 0, -2, 0, 2);
        vm.MarkProjectReplaced(); vm.IsElementSplit = true;
        Run(vm, "SplitElementsAtIntersections"); Run(vm, "EqualDivideElements");
        Assert.AreEqual(4, vm.CurrentInputModel!.InputNodes.Count); Assert.AreEqual(2, vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);
        Assert.IsTrue(vm.IsElementSplit); Assert.IsFalse(vm.HasUnsavedWork);
    }

    [TestMethod] public void ValidEqualDivisionCreatesOneEditAndPreservesVisibility()
    {
        var vm = Create(); AddBeam(vm, 0, 0, 2, 0); vm.MarkProjectReplaced(); int version = vm.InputEditVersion;
        Run(vm, "EqualDivideElements");
        Assert.AreEqual(3, vm.CurrentInputModel!.InputNodes.Count); Assert.AreEqual(2, vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);
        Assert.IsTrue(vm.HasUnsavedWork); Assert.AreEqual(version + 1, vm.InputEditVersion);
        foreach (var beam in vm.CurrentInputModel!.FoundationBeamInput.Beams) Assert.IsFalse(beam.IsVisible);
    }

    [TestMethod] public void NonFiniteCandidateDoesNotSplit()
    {
        var vm = Create(); AddBeam(vm, 0, 0, 2, 0);
        var candidate = new InputNode { Type = NodeType.General };
        // Simulate corrupt legacy state without bypassing the guard in normal setters.
        typeof(InputNode).GetField("_x", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(candidate, double.NaN);
        vm.CurrentInputModel!.InputNodes.Add(candidate);
        vm.MarkProjectReplaced(); Run(vm, "OnSplitElementsByNodes");
        Assert.AreEqual(1, vm.CurrentInputModel!.FoundationBeamInput.Beams.Count); Assert.IsFalse(vm.HasUnsavedWork);
    }
}
