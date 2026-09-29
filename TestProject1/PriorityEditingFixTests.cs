using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Linq;
using System.Reflection;

namespace TestProject1;

[TestClass]
[DoNotParallelize]
public class PriorityEditingFixTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;
    private static MainWindowViewModel Create()
    {
        var vm = new MainWindowViewModel { CurrentInputModel = new InputModel
        { PileLayoutItems = [], InputNodes = [], PileGroupSettlement = new PileGroupSettlement { RectLoads = [] },
            FoundationBeamInput = new FoundationBeamInput { Beams = [] } } };
        vm.MarkProjectReplaced(); return vm;
    }
    private static object? Run(MainWindowViewModel vm, string method, params object[] args) => typeof(MainWindowViewModel)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,args);
    private static UndoManager History(MainWindowViewModel vm) => (UndoManager)typeof(MainWindowViewModel)
        .GetField("_undoManager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
    private static bool Flag(MainWindowViewModel vm, string name) => (bool)typeof(MainWindowViewModel)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;

    [DataTestMethod] [DataRow("SortPileLayoutXFirst")] [DataRow("SortPileLayoutYFirst")]
    public void SortingKeepsLoadsAndNodesLinkedToSamePileAndSupportsUndo(string command)
    {
        using var vm = Create(); var input = vm.CurrentInputModel!;
        input.PileLayoutItems = [new PileLayoutDataItem { No=1,PileNo=1,X=10,Y=10 }, new PileLayoutDataItem { No=2,PileNo=2,X=0,Y=0 }];
        var originalFirst = input.PileLayoutItems[0].UniqueId;
        input.InputNodes.Add(new InputNode { LinkedPileNo=1 });
        input.PileGroupSettlement.RectLoads = [new RectLoad { LinkedPileNo=1,QA=101 },new RectLoad { LinkedPileNo=2,QA=202 },new RectLoad { LinkedPileNo=0,QA=303 }];
        Run(vm,command);
        Assert.AreEqual(originalFirst,input.PileLayoutItems[1].UniqueId);
        Assert.AreEqual(2,input.PileGroupSettlement.RectLoads[0].LinkedPileNo);
        Assert.AreEqual(1,input.PileGroupSettlement.RectLoads[1].LinkedPileNo);
        Assert.AreEqual(0,input.PileGroupSettlement.RectLoads[2].LinkedPileNo);
        Assert.AreEqual(101.0,input.PileGroupSettlement.RectLoads[0].QA);
        Assert.AreEqual(2,input.InputNodes[0].LinkedPileNo);
        vm.UndoCommand.Execute(null);
        Assert.AreEqual(originalFirst,vm.CurrentInputModel!.PileLayoutItems[0].UniqueId);
        Assert.AreEqual(1,vm.CurrentInputModel!.PileGroupSettlement.RectLoads[0].LinkedPileNo);
        vm.RedoCommand.Execute(null);
        Assert.AreEqual(originalFirst,vm.CurrentInputModel!.PileLayoutItems[1].UniqueId);
        Assert.AreEqual(2,vm.CurrentInputModel!.PileGroupSettlement.RectLoads[0].LinkedPileNo);
    }

    [DataTestMethod] [DataRow(false)] [DataRow(true)]
    public void InvalidResetDoesNotCommitAnyLoadOrHistory(bool overflowingForce)
    {
        using var vm = Create(); var input = vm.CurrentInputModel!;
        input.PileLayoutItems.Add(new PileLayoutDataItem { No=1,PileNo=1,X=0,Y=0,AxialForceVL0=10 });
        input.PileLayoutItems.Add(new PileLayoutDataItem { No=2,PileNo=2,X=overflowingForce ? 2 : double.MaxValue,Y=2,
            AxialForceVL0=overflowingForce ? double.MaxValue : 20, AxialForceVLAdditional=overflowingForce ? double.MaxValue : 0 });
        vm.IsGroupPileSettlementAnalysisDone=true; vm.MarkProjectReplaced();
        var loads = input.PileGroupSettlement.RectLoads; string mode=input.PileGroupSettlement.LoadingType;
        int version=vm.InputEditVersion, history=History(vm).History.Count;
        Run(vm,"ResetBeamAwareRectLoads");
        Assert.AreSame(loads,input.PileGroupSettlement.RectLoads); Assert.AreEqual(0,loads.Count);
        Assert.AreEqual(mode,input.PileGroupSettlement.LoadingType); Assert.IsTrue(vm.IsGroupPileSettlementAnalysisDone);
        Assert.IsFalse(vm.HasUnsavedWork); Assert.AreEqual(version,vm.InputEditVersion); Assert.AreEqual(history,History(vm).History.Count);
    }

    [TestMethod] public void ValidResetCreatesFiniteLoadsAndSupportsUndoRedo()
    {
        using var vm = Create(); var input=vm.CurrentInputModel!;
        input.PileLayoutItems.Add(new PileLayoutDataItem { No=1,PileNo=1,X=3,Y=5,AxialForceVL0=100,AxialForceVLAdditional=20 });
        Run(vm,"ResetBeamAwareRectLoads");
        var load=input.PileGroupSettlement.RectLoads.Single();
        Assert.AreEqual(120.0,load.QA); Assert.AreEqual(2.0,load.DX); Assert.AreEqual(2.0,load.DY);
        Assert.AreEqual(30.0,load.Q); Assert.AreEqual(1,load.LinkedPileNo); Assert.IsTrue(vm.HasUnsavedWork);
        vm.UndoCommand.Execute(null); Assert.AreEqual(0,vm.CurrentInputModel!.PileGroupSettlement.RectLoads.Count);
        vm.RedoCommand.Execute(null); Assert.AreEqual(120.0,vm.CurrentInputModel!.PileGroupSettlement.RectLoads[0].QA);
    }

    [TestMethod] public void DecliningResetPreservesExistingLoadsAndHistory()
    {
        using var vm=Create(); var input=vm.CurrentInputModel!;
        input.PileLayoutItems.Add(new PileLayoutDataItem { No=1,PileNo=1 });
        var load=new RectLoad { QA=9 }; input.PileGroupSettlement.RectLoads.Add(load);
        int version=vm.InputEditVersion, history=History(vm).History.Count;
        Run(vm,"ResetBeamAwareRectLoads");
        Assert.AreSame(load,input.PileGroupSettlement.RectLoads.Single()); Assert.IsFalse(vm.HasUnsavedWork);
        Assert.AreEqual(version,vm.InputEditVersion); Assert.AreEqual(history,History(vm).History.Count);
    }

    [TestMethod] public void InvalidDeletionKeepsCrossModeAndExistingLoad()
    {
        using var vm=Create(); var input=vm.CurrentInputModel!; input.PileGroupSettlement.LoadingType="個別十字";
        var load=new RectLoad(); input.PileGroupSettlement.RectLoads.Add(load);
        int version=vm.InputEditVersion, history=History(vm).History.Count;
        Run(vm,"DeleteRectLoad",new object()); Run(vm,"DeleteRectLoad",new RectLoad());
        Assert.AreSame(load,input.PileGroupSettlement.RectLoads.Single());
        Assert.AreEqual("個別十字",input.PileGroupSettlement.LoadingType); Assert.IsFalse(vm.HasUnsavedWork);
        Assert.AreEqual(version,vm.InputEditVersion); Assert.AreEqual(history,History(vm).History.Count);
    }

    [TestMethod] public void SuccessfulDeletionSwitchesModeAndUndoRestoresIt()
    {
        using var vm=Create(); var input=vm.CurrentInputModel!; input.PileGroupSettlement.LoadingType="個別十字";
        var load=new RectLoad { QA=22 }; input.PileGroupSettlement.RectLoads.Add(load);
        Run(vm,"DeleteRectLoad",load);
        Assert.AreEqual(0,input.PileGroupSettlement.RectLoads.Count); Assert.AreEqual("任意矩形",input.PileGroupSettlement.LoadingType);
        Assert.IsTrue(vm.HasUnsavedWork);
        vm.UndoCommand.Execute(null); Assert.AreEqual(22.0,vm.CurrentInputModel!.PileGroupSettlement.RectLoads.Single().QA);
        Assert.AreEqual("個別十字",vm.CurrentInputModel!.PileGroupSettlement.LoadingType);
        vm.RedoCommand.Execute(null); Assert.AreEqual(0,vm.CurrentInputModel!.PileGroupSettlement.RectLoads.Count);
    }

    [TestMethod] public void SwapBeamMarksEditAndUndoRedoRestoresEndpointsAndAngle()
    {
        using var vm=Create(); var input=vm.CurrentInputModel!;
        var i=Guid.NewGuid(); var j=Guid.NewGuid();
        input.FoundationBeamInput.Beams.Add(new FoundationBeam { IsSelected=true,NodeI_Type=NodeReferenceType.GeneralNode,
            NodeI_Id=i,NodeJ_Type=NodeReferenceType.PileLayout,NodeJ_Id=j,AngleBeta=30 });
        input.FoundationBeamInput.Beams.Add(new FoundationBeam { NodeI_Id=Guid.NewGuid(),AngleBeta=12 });
        int version=vm.InputEditVersion;
        Run(vm,"SwapBeamIJ");
        Assert.AreEqual(j,input.FoundationBeamInput.Beams[0].NodeI_Id); Assert.AreEqual(150.0,input.FoundationBeamInput.Beams[0].AngleBeta);
        Assert.AreEqual(12.0,input.FoundationBeamInput.Beams[1].AngleBeta);
        Assert.IsTrue(vm.HasUnsavedWork); Assert.AreEqual(version+1,vm.InputEditVersion); Assert.IsTrue(Flag(vm,"_settlementInputChanged"));
        vm.UndoCommand.Execute(null); Assert.AreEqual(i,vm.CurrentInputModel!.FoundationBeamInput.Beams[0].NodeI_Id);
        Assert.AreEqual(30.0,vm.CurrentInputModel!.FoundationBeamInput.Beams[0].AngleBeta);
        vm.RedoCommand.Execute(null); Assert.AreEqual(j,vm.CurrentInputModel!.FoundationBeamInput.Beams[0].NodeI_Id);
    }

    [TestMethod] public void RefusingSplitDiscardDoesNotMarkAnalysisInputsChanged()
    {
        using var vm=Create(); vm.IsElementSplit=true;
        int version=vm.InputEditVersion, history=History(vm).History.Count;
        Assert.IsFalse(Flag(vm,"_settlementInputChanged"));
        Assert.AreEqual(false,Run(vm,"CheckAndResetAnalysisResults"));
        Assert.IsFalse(Flag(vm,"_settlementInputChanged")); Assert.IsFalse(Flag(vm,"_horizontalInputChanged"));
        Assert.IsTrue(vm.IsElementSplit); Assert.IsFalse(vm.HasUnsavedWork);
        Assert.AreEqual(version,vm.InputEditVersion); Assert.AreEqual(history,History(vm).History.Count);
    }
}
