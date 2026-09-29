using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using PileDesign.Views;
using System;
using System.Linq;
using System.Reflection;

namespace TestProject1;
[TestClass, DoNotParallelize]
public class RemainingEditingFixTests
{
    private bool unattended;
    [TestInitialize] public void Init() { unattended=MessageService.IsUnattended; MessageService.IsUnattended=true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended=unattended;
    private static MainWindowViewModel Create()
    {
        var vm=new MainWindowViewModel();
        vm.CurrentInputModel=new InputModel {
            PileLayoutItems=[], InputNodes=[], GridXItems=[], GridYItems=[], LoadCasesInput=vm.CurrentInputModel!.LoadCasesInput,
            PileGroupSettlement=new PileGroupSettlement { RectLoads=[], SettlementSoilLayers=[] },
            FoundationBeamInput=new FoundationBeamInput { Beams=[], Nodes=[] } };
        vm.MarkProjectReplaced(); return vm;
    }
    private static object? Run(MainWindowViewModel vm,string method,params object[] args) => typeof(MainWindowViewModel)
        .GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(vm,args);
    private static int Count(MainWindowViewModel vm) => ((UndoManager)typeof(MainWindowViewModel)
        .GetField("_undoManager",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(vm)!).History.Count;

    [TestMethod] public void AlreadySortedCommandsDoNotChangeVersionHistoryOrSplitState()
    {
        using var vm=Create();
        vm.CurrentInputModel!.PileLayoutItems=[new PileLayoutDataItem {No=1,PileNo=1,X=0},new PileLayoutDataItem {No=2,PileNo=2,X=1}];
        vm.CurrentInputModel!.InputNodes=[new InputNode {X=0},new InputNode {X=1}];
        vm.CurrentInputModel!.FoundationBeamInput.Beams=[new FoundationBeam()];
        vm.IsElementSplit=true; int history=Count(vm),version=vm.InputEditVersion;
        foreach(var command in new[]{"SortPileLayoutXFirst","SortPileLayoutYFirst","SortInputNodesXFirst","SortInputNodesYFirst","SortBeamsByNode","SortBeamsByNo"}) Run(vm,command);
        Assert.IsTrue(vm.IsElementSplit); Assert.IsFalse(vm.HasUnsavedWork);
        Assert.AreEqual(history,Count(vm)); Assert.AreEqual(version,vm.InputEditVersion);
    }
    [TestMethod] public void GridEditsSupportConsecutiveUndoAndRedo()
    {
        using var vm=Create(); Run(vm,"AddGridX"); Run(vm,"AddGridX");
        Assert.AreEqual(7.2,vm.CurrentInputModel!.GridXItems[1].Coord);
        Run(vm,"DeleteGridX",vm.CurrentInputModel!.GridXItems[0]);
        Assert.AreEqual(1,vm.CurrentInputModel!.GridXItems.Count);
        vm.UndoCommand.Execute(null); Assert.AreEqual(2,vm.CurrentInputModel!.GridXItems.Count);
        vm.UndoCommand.Execute(null); Assert.AreEqual(1,vm.CurrentInputModel!.GridXItems.Count);
        vm.RedoCommand.Execute(null); Assert.AreEqual(2,vm.CurrentInputModel!.GridXItems.Count);
        vm.RedoCommand.Execute(null); Assert.AreEqual(1,vm.CurrentInputModel!.GridXItems.Count);
    }
    [TestMethod] public void OutsideRowsCannotDeleteOrCreateHistory()
    {
        using var vm=Create(); var node=new FoundationNode();vm.CurrentInputModel!.FoundationBeamInput.Nodes.Add(node);
        int history=Count(vm),version=vm.InputEditVersion;
        Run(vm,"DeleteFoundationBeam",new FoundationBeam()); Run(vm,"DeleteGridX",new GridDataItem());
        Run(vm,"DeleteGridY",new object()); Run(vm,"DeleteSettlementSoilLayer",new SettlementSoilLayer());
        Assert.AreSame(node,vm.CurrentInputModel!.FoundationBeamInput.Nodes.Single());
        Assert.AreEqual(history,Count(vm));Assert.AreEqual(version,vm.InputEditVersion); Assert.IsFalse(vm.HasUnsavedWork);
    }
    [DataTestMethod,DataRow(double.NaN),DataRow(double.PositiveInfinity),DataRow(double.NegativeInfinity)]
    public void GridSettersRejectNonfiniteWithoutReplacingPreviousValue(double value)
    {
        var grid=new GridDataItem {Coord=3,Spacing=5};
        Assert.ThrowsException<ArgumentOutOfRangeException>(()=>grid.Coord=value);
        Assert.ThrowsException<ArgumentOutOfRangeException>(()=>grid.Spacing=value);
        Assert.AreEqual(3.0,grid.Coord); Assert.AreEqual(5.0,grid.Spacing);
    }
    [DataTestMethod,DataRow(-1.0),DataRow(double.NaN),DataRow(double.PositiveInfinity)]
    public void InvalidSelectionDistanceProtectsSelection(double value)
    {
        using var vm=Create(); var pile=new PileLayoutDataItem {X=100,IsSelected=true};vm.CurrentInputModel!.PileLayoutItems.Add(pile);
        Assert.ThrowsException<ArgumentOutOfRangeException>(()=>vm.GridSelectionDistance=value);
        Run(vm,"SelectGridX",new GridDataItem {Coord=0});Assert.IsTrue(pile.IsSelected);Assert.AreEqual(0.1,vm.GridSelectionDistance);
        vm.GridSelectionDistance=0;Run(vm,"SelectGridX",new GridDataItem {Coord=0});Assert.IsFalse(pile.IsSelected);
    }
    [TestMethod] public void CoordinateOverflowDoesNotPartiallyRecalculateOrAddGrid()
    {
        using var vm=Create(); var grids=vm.CurrentInputModel!.GridXItems;
        grids.Add(new GridDataItem {Coord=double.MaxValue,Spacing=4});
        grids.Add(new GridDataItem {Coord=12,Spacing=double.MaxValue});
        int history=Count(vm);Run(vm,"RecalculateGrid",grids); Run(vm,"AddGridX");
        Assert.AreEqual(2,grids.Count);Assert.AreEqual(12.0,grids[1].Coord);Assert.AreEqual(4.0,grids[0].Spacing);
        Assert.AreEqual(history,Count(vm));Assert.IsFalse(vm.HasUnsavedWork);
    }
    [TestMethod] public void ScaledMeanHandlesExtremeFiniteCoordinates()
    {
        Assert.AreEqual(double.MaxValue,MainWindowViewModel.FiniteMean(new[]{double.MaxValue,double.MaxValue}));
        Assert.AreEqual(0.0,MainWindowViewModel.FiniteMean(new[]{double.MaxValue,-double.MaxValue}));
        Assert.AreEqual(2.0,MainWindowViewModel.FiniteMean(new[]{1.0,2.0,3.0}),1e-14);
        Assert.ThrowsException<ArgumentException>(()=>MainWindowViewModel.FiniteMean(new[]{double.NaN}));
    }
    [TestMethod] public void EmptyCenterAndNoSelectedFrontCasesDoNotCreateHistory()
    {
        using var vm=Create();int history=Count(vm),version=vm.InputEditVersion;
        Run(vm,"OnMoveForceActionPointToAverageCenter");
        Run(vm,"AutoIsFrontPilesWindow_AutoIsFrontPileCompleted",new object(),new AutoIsFrontPilesWindow.AutoIsFrontEventArgs {Angle=30,IsChecked=[false,false,false,false]});
        Assert.AreEqual(history,Count(vm));Assert.AreEqual(version,vm.InputEditVersion);Assert.IsFalse(vm.HasUnsavedWork);
    }
    [TestMethod] public void InvalidFrontSettingsAreCanceledWithoutEdits()
    {
        using var vm=Create();var args=new AutoIsFrontPilesWindow.AutoIsFrontEventArgs {Angle=double.NaN,IsChecked=[true,true,true,true]};
        Run(vm,"AutoIsFrontPilesWindow_AutoIsFrontPileCompleted",new object(),args);
        Assert.IsTrue(args.Cancel);Assert.IsFalse(vm.HasUnsavedWork);
    }
    [TestMethod] public void PileAdditionSupportsUndoRedo()
    {
        using var vm=Create();Run(vm,"OnAddPile");Assert.AreEqual(1,vm.CurrentInputModel!.PileLayoutItems.Count);
        vm.UndoCommand.Execute(null);Assert.AreEqual(0,vm.CurrentInputModel!.PileLayoutItems.Count);
        vm.RedoCommand.Execute(null);Assert.AreEqual(1,vm.CurrentInputModel!.PileLayoutItems.Count);
    }
    [TestMethod] public void BeamDeletionSupportsUndoRedo()
    {
        using var vm=Create();vm.CurrentInputModel!.FoundationBeamInput.Beams.Add(new FoundationBeam());
        Run(vm,"DeleteFoundationBeam",vm.CurrentInputModel!.FoundationBeamInput.Beams[0]);
        Assert.AreEqual(0,vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);
        vm.UndoCommand.Execute(null);Assert.AreEqual(1,vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);
        vm.RedoCommand.Execute(null);Assert.AreEqual(0,vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);
    }
    [TestMethod] public void RectLoadAdditionSupportsUndoRedo()
    {
        using var vm=Create();Run(vm,"AddRectLoad");Assert.AreEqual(1,vm.CurrentInputModel!.PileGroupSettlement.RectLoads.Count);
        vm.UndoCommand.Execute(null);Assert.AreEqual(0,vm.CurrentInputModel!.PileGroupSettlement.RectLoads.Count);
        vm.RedoCommand.Execute(null);Assert.AreEqual(1,vm.CurrentInputModel!.PileGroupSettlement.RectLoads.Count);
    }
    [TestMethod] public void SettlementLayerAdditionAndDeletionSupportUndoRedo()
    {
        using var vm=Create();Run(vm,"AddSettlementSoilLayer");
        Run(vm,"DeleteSettlementSoilLayer",vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers[0]);
        vm.UndoCommand.Execute(null);Assert.AreEqual(1,vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers.Count);
        vm.UndoCommand.Execute(null);Assert.AreEqual(0,vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers.Count);
        vm.RedoCommand.Execute(null);Assert.AreEqual(1,vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers.Count);
        vm.RedoCommand.Execute(null);Assert.AreEqual(0,vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers.Count);
    }

    [TestMethod] public void CenterMoveSupportsUndoRedoAndRepeatedMoveIsNoOp()
    {
        using var vm=Create();vm.CurrentInputModel!.PileLayoutItems=[new PileLayoutDataItem {X=2,Y=4,Z=6},new PileLayoutDataItem {X=4,Y=6,Z=8}];
        double old=vm.CurrentInputModel!.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointX;
        Run(vm,"OnMoveForceActionPointToAverageCenter");
        Assert.AreEqual(3.0,vm.CurrentInputModel!.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointX);
        Assert.AreEqual(7.0,vm.CurrentInputModel!.LoadCasesInput.LoadCaseLevel2Common.ForceActionPointAltitude);
        int history=Count(vm),version=vm.InputEditVersion;
        vm.IsElementSplit=true;Run(vm,"OnMoveForceActionPointToAverageCenter");
        Assert.IsTrue(vm.IsElementSplit);Assert.AreEqual(history,Count(vm));Assert.AreEqual(version,vm.InputEditVersion);
        vm.UndoCommand.Execute(null);Assert.AreEqual(old,vm.CurrentInputModel!.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointX);
        vm.RedoCommand.Execute(null);Assert.AreEqual(3.0,vm.CurrentInputModel!.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointX);
    }
    [TestMethod] public void TorsionalResetSupportsUndoAndRepeatedResetIsNoOp()
    {
        using var vm=Create();vm.CurrentInputModel!.FoundationBeamInput.EnsureDefaultMaterialAndSection();
        var section=vm.CurrentInputModel!.FoundationBeamInput.Sections[0];section.IxxFactor=2;section.TorsionalMoment=3;
        Run(vm,"ClearAllTorsionalStiffness");Assert.AreEqual(0.0,section.IxxFactor);Assert.AreEqual(0.0,section.TorsionalMoment);
        int history=Count(vm),version=vm.InputEditVersion;vm.IsElementSplit=true;Run(vm,"ClearAllTorsionalStiffness");
        Assert.IsTrue(vm.IsElementSplit);Assert.AreEqual(history,Count(vm));Assert.AreEqual(version,vm.InputEditVersion);
        vm.UndoCommand.Execute(null);Assert.AreEqual(2.0,vm.CurrentInputModel!.FoundationBeamInput.Sections[0].IxxFactor);
        vm.RedoCommand.Execute(null);Assert.AreEqual(0.0,vm.CurrentInputModel!.FoundationBeamInput.Sections[0].IxxFactor);
    }
    [DataTestMethod,DataRow(false),DataRow(true)]
    public void FrontFlagsAreCommittedOnlyAfterConfirmation(bool decline)
    {
        using var vm=Create();var pile=new PileLayoutDataItem();pile.IsFrontPiles[0]=false;vm.CurrentInputModel!.PileLayoutItems.Add(pile);
        vm.IsElementSplit=decline;var args=new AutoIsFrontPilesWindow.AutoIsFrontEventArgs {Angle=30,IsChecked=[true,false,false,false]};
        int history=Count(vm),version=vm.InputEditVersion;
        Run(vm,"AutoIsFrontPilesWindow_AutoIsFrontPileCompleted",new object(),args);
        if(decline) { Assert.IsFalse(pile.IsFrontPiles[0]);Assert.IsTrue(args.Cancel);Assert.IsTrue(vm.IsElementSplit);Assert.AreEqual(history,Count(vm));Assert.AreEqual(version,vm.InputEditVersion); }
        else { Assert.IsTrue(pile.IsFrontPiles[0]);vm.UndoCommand.Execute(null);Assert.IsFalse(vm.CurrentInputModel!.PileLayoutItems[0].IsFrontPiles[0]);vm.RedoCommand.Execute(null);Assert.IsTrue(vm.CurrentInputModel!.PileLayoutItems[0].IsFrontPiles[0]); }
    }
    [TestMethod] public void ClosingActualFrontDialogPreservesStateAndHistory()
    {
        var error=XamlSmokeTestSupport.RunOnStaThread(()=> {
            using var vm=Create();vm.IsElementSplit=true;int history=Count(vm),version=vm.InputEditVersion;
            var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(20)};
            bool closed=false;timer.Tick+=(_,_)=> {
                var window=System.Windows.Application.Current.Windows.Cast<System.Windows.Window>().OfType<AutoIsFrontPilesWindow>().FirstOrDefault();
                if(window==null)return;timer.Stop();window.Close();closed=true;
            };
            timer.Start();try {Run(vm,"AutoIsFrontPiles");}finally {timer.Stop();}
            Assert.IsTrue(closed);Assert.IsTrue(vm.IsElementSplit);Assert.IsFalse(vm.HasUnsavedWork);
            Assert.AreEqual(history,Count(vm));Assert.AreEqual(version,vm.InputEditVersion);
        },out bool timedOut);
        Assert.IsFalse(timedOut);Assert.IsNull(error,error?.ToString());
    }
    [TestMethod] public void EqualDivisionRestoresGeneratedNodesAndBeamsThroughUndoRedo()
    {
        using var vm=Create();var a=new InputNode {Type=NodeType.General,X=0};var b=new InputNode {Type=NodeType.General,X=4};
        vm.CurrentInputModel!.InputNodes=[a,b];vm.CurrentInputModel!.FoundationBeamInput.Beams=[new FoundationBeam {IsSelected=true,NodeI_Type=NodeReferenceType.GeneralNode,NodeI_Id=a.UniqueId,NodeJ_Type=NodeReferenceType.GeneralNode,NodeJ_Id=b.UniqueId}];
        vm.EqualDivisionCount=2;Run(vm,"EqualDivideElements");
        Assert.AreEqual(2,vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);Assert.AreEqual(3,vm.CurrentInputModel!.InputNodes.Count);
        vm.UndoCommand.Execute(null);Assert.AreEqual(1,vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);Assert.AreEqual(2,vm.CurrentInputModel!.InputNodes.Count);
        vm.RedoCommand.Execute(null);Assert.AreEqual(2,vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);Assert.AreEqual(3,vm.CurrentInputModel!.InputNodes.Count);
    }
}
