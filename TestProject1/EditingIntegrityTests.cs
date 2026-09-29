using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using PileDesign.Views;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
namespace TestProject1;
[TestClass,DoNotParallelize]
public class EditingIntegrityTests
{
    private bool unattended;
    [TestInitialize] public void Init(){ unattended=MessageService.IsUnattended;MessageService.IsUnattended=true; }
    [TestCleanup] public void Cleanup()=>MessageService.IsUnattended=unattended;
    private static MainWindowViewModel Create(){var vm=new MainWindowViewModel();var m=vm.CurrentInputModel!;m.PileLayoutItems=[];m.InputNodes=[];m.FoundationBeamInput.Beams=[];m.FoundationBeamInput.Nodes=[];m.PileGroupSettlement.RectLoads=[];vm.MarkProjectReplaced();return vm;}
    private static object? Run(MainWindowViewModel vm,string name,params object[] args)=>typeof(MainWindowViewModel).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(vm,args);
    [TestMethod] public void NodeConversionPreservesBeamEndpointsThroughUndoRedo()
    {
        using var vm=Create();var m=vm.CurrentInputModel!;var pile=new PileLayoutDataItem {No=1,PileNo=1,X=2,IsSelected=true};var node=new InputNode {X=4,IsSelected=true};m.PileLayoutItems.Add(pile);m.InputNodes.Add(node);
        m.FoundationBeamInput.Beams.Add(new FoundationBeam {NodeI_Type=NodeReferenceType.PileLayout,NodeI_Id=pile.UniqueId,NodeJ_Type=NodeReferenceType.GeneralNode,NodeJ_Id=node.UniqueId});
        Run(vm,"ConvertNodeType");var beam=m.FoundationBeamInput.Beams[0];Assert.AreEqual(NodeReferenceType.GeneralNode,beam.NodeI_Type);Assert.AreEqual(m.InputNodes[0].UniqueId,beam.NodeI_Id);Assert.AreEqual(NodeReferenceType.PileLayout,beam.NodeJ_Type);Assert.AreEqual(m.PileLayoutItems[0].UniqueId,beam.NodeJ_Id);
        vm.UndoCommand.Execute(null);Assert.AreEqual(pile.UniqueId,vm.CurrentInputModel!.FoundationBeamInput.Beams[0].NodeI_Id);vm.RedoCommand.Execute(null);Assert.AreEqual(NodeReferenceType.GeneralNode,vm.CurrentInputModel!.FoundationBeamInput.Beams[0].NodeI_Type);
    }
    [TestMethod] public void PileDeletionRemapsSurvivorsAndUnlinksDeletedPile()
    {
        using var vm=Create();var m=vm.CurrentInputModel!;m.PileLayoutItems=[new PileLayoutDataItem {No=1,PileNo=1,IsSelected=true},new PileLayoutDataItem {No=2,PileNo=2,X=4}];m.InputNodes=[new InputNode {Type=NodeType.Pile,LinkedPileNo=1},new InputNode {LinkedPileNo=2}];m.PileGroupSettlement.RectLoads=[new RectLoad {LinkedPileNo=1,QA=12},new RectLoad {LinkedPileNo=2,QA=34}];
        Run(vm,"DeletePiles");Assert.IsNull(m.InputNodes[0].LinkedPileNo);Assert.AreEqual(NodeType.General,m.InputNodes[0].Type);Assert.AreEqual(1,m.InputNodes[1].LinkedPileNo);Assert.AreEqual(0,m.PileGroupSettlement.RectLoads[0].LinkedPileNo);Assert.AreEqual(1,m.PileGroupSettlement.RectLoads[1].LinkedPileNo);Assert.AreEqual(12.0,m.PileGroupSettlement.RectLoads[0].QA);
        vm.UndoCommand.Execute(null);Assert.AreEqual(2,vm.CurrentInputModel!.PileLayoutItems.Count);Assert.AreEqual(2,vm.CurrentInputModel!.PileGroupSettlement.RectLoads[1].LinkedPileNo);vm.RedoCommand.Execute(null);Assert.AreEqual(1,vm.CurrentInputModel!.PileLayoutItems.Count);
    }
    [TestMethod] public void DuplicateMergeCannotCollapseBeam()
    {
        using var vm=Create();var a=new InputNode();var b=new InputNode {X=0.5e-6};vm.CurrentInputModel!.InputNodes=[a,b];vm.CurrentInputModel!.FoundationBeamInput.Beams=[new FoundationBeam {NodeI_Type=NodeReferenceType.GeneralNode,NodeI_Id=a.UniqueId,NodeJ_Type=NodeReferenceType.GeneralNode,NodeJ_Id=b.UniqueId}];Run(vm,"DeleteDuplicateInputNodes");Assert.AreEqual(2,vm.CurrentInputModel!.InputNodes.Count);Assert.IsFalse(vm.HasUnsavedWork);Assert.AreEqual(b.UniqueId,vm.CurrentInputModel!.FoundationBeamInput.Beams[0].NodeJ_Id);
    }
    [DataTestMethod,DataRow("DeletePiles"),DataRow("DeleteBeams"),DataRow("ConvertNodeType"),DataRow("DeleteDuplicateInputNodes")]
    public void EmptyOperationsKeepAnalysisState(string name){using var vm=Create();vm.IsElementSplit=true;int version=vm.InputEditVersion;Run(vm,name);Assert.IsTrue(vm.IsElementSplit);Assert.IsFalse(vm.HasUnsavedWork);Assert.AreEqual(version,vm.InputEditVersion);}
    [TestMethod] public void NodeAddAndCopyHaveCompleteHistory(){using var vm=Create();Run(vm,"AddInputNode");Run(vm,"CopyInputNode",vm.CurrentInputModel!.InputNodes[0]);vm.UndoCommand.Execute(null);Assert.AreEqual(1,vm.CurrentInputModel!.InputNodes.Count);vm.UndoCommand.Execute(null);Assert.AreEqual(0,vm.CurrentInputModel!.InputNodes.Count);vm.RedoCommand.Execute(null);vm.RedoCommand.Execute(null);Assert.AreEqual(2,vm.CurrentInputModel!.InputNodes.Count);}
    [TestMethod] public async Task NodeMoveHasCompleteHistory(){using var vm=Create();vm.CurrentInputModel!.InputNodes=[new InputNode {X=1,IsSelected=true}];await (Task)Run(vm,"MoveCopyWindow_MoveCopyCompletedAsync",new object(),new MoveCopyWindow.MoveCopyEventArgs {IsMove=true,DX=3,IsInputNodesIncluded=true})!;Assert.AreEqual(4.0,vm.CurrentInputModel!.InputNodes[0].X);vm.UndoCommand.Execute(null);Assert.AreEqual(1.0,vm.CurrentInputModel!.InputNodes[0].X);vm.RedoCommand.Execute(null);Assert.AreEqual(4.0,vm.CurrentInputModel!.InputNodes[0].X);}

    [TestMethod] public void ReactionPreparationRejectsLateInvalidCaseWithoutPartialChanges()
    {
        using var vm=Create();vm.CurrentInputModel!.PileLayoutItems=[new PileLayoutDataItem {X=-1},new PileLayoutDataItem {X=1}];
        var editor=new AutoOverturningMomentViewModel(vm) {OverturningMoment1=2,OverturningMoment2=double.NaN,IsApplicableE1s=[true,false,false,false],IsApplicableE2s=[true,false,false,false],IsApplicableVL=false};
        Assert.IsFalse(editor.TryApplyReactions());Assert.AreEqual(0.0,vm.CurrentInputModel!.PileLayoutItems[0].AxialForceLevel1s[0]);Assert.IsFalse(vm.HasUnsavedWork);
        editor.IsApplicableE2s=[false,false,false,false];Assert.IsTrue(editor.TryApplyReactions());Assert.AreEqual(-1000.0,vm.CurrentInputModel!.PileLayoutItems[0].AxialForceLevel1s[0]);
        vm.UndoCommand.Execute(null);Assert.AreEqual(0.0,vm.CurrentInputModel!.PileLayoutItems[0].AxialForceLevel1s[0]);vm.RedoCommand.Execute(null);Assert.AreEqual(-1000.0,vm.CurrentInputModel!.PileLayoutItems[0].AxialForceLevel1s[0]);
        int version=vm.InputEditVersion;Assert.IsTrue(editor.TryApplyReactions());Assert.AreEqual(version,vm.InputEditVersion);
    }
    [TestMethod] public void ForeignNodeDeletionDoesNotDeleteMatchingBeam()
    {
        using var vm=Create();var a=new InputNode();vm.CurrentInputModel!.InputNodes.Add(a);var beam=new FoundationBeam {NodeI_Type=NodeReferenceType.GeneralNode,NodeI_Id=a.UniqueId};vm.CurrentInputModel!.FoundationBeamInput.Beams.Add(beam);
        Run(vm,"DeleteInputNode",new InputNode {UniqueId=a.UniqueId});Assert.AreSame(beam,vm.CurrentInputModel!.FoundationBeamInput.Beams.Single());Assert.IsFalse(vm.HasUnsavedWork);
    }
    [DataTestMethod,DataRow(false),DataRow(true)]
    public void PanelValidationAndNoOpProtectInput(bool multiple)
    {
        using var vm=Create();var pile=new PileLayoutDataItem {IsSelected=true,GroupPileFactor=0.5};vm.CurrentInputModel!.PileLayoutItems.Add(pile);
        if(multiple) vm.CurrentInputModel!.PileLayoutItems.Add(new PileLayoutDataItem {IsSelected=true,GroupPileFactor=0.5});
        Run(vm,multiple?"BuildMultiPileProperties":"BuildPileProperties",multiple?(object)vm.CurrentInputModel!.PileLayoutItems.ToList():pile);
        var item=vm.SelectedItemProperties.First(p=>p.Name=="\u7fa4\u676d\u4fc2\u6570 \u03be");
        vm.IsElementSplit=true;int version=vm.InputEditVersion;
        item.CommitAction!(item,"0.5");item.CommitAction(item,"NaN");item.CommitAction(item,"1.1");
        Assert.IsTrue(vm.IsElementSplit);Assert.AreEqual(version,vm.InputEditVersion);Assert.AreEqual(0.5,pile.GroupPileFactor);
        vm.IsElementSplit=false;item.CommitAction(item,"0.7");Assert.AreEqual(0.7,pile.GroupPileFactor);vm.UndoCommand.Execute(null);Assert.AreEqual(0.5,vm.CurrentInputModel!.PileLayoutItems[0].GroupPileFactor);vm.RedoCommand.Execute(null);Assert.AreEqual(0.7,vm.CurrentInputModel!.PileLayoutItems[0].GroupPileFactor);
    }
    [TestMethod] public void CoefficientApplicationNoOpAndUndoRedo()
    {
        using var vm=Create();var editor=new GroupPileFactorViewModel(vm) {PileSpacingDiaRatio=3};
        editor.OnApplyPileDistanceFactorToModelsAllPiles();Assert.IsFalse(vm.HasUnsavedWork);
        vm.CurrentInputModel!.PileLayoutItems=[new PileLayoutDataItem {PileSpacingFactor=2}];editor.OnApplyPileDistanceFactorToModelsAllPiles();Assert.AreEqual(3.0,vm.CurrentInputModel!.PileLayoutItems[0].PileSpacingFactor);
        int version=vm.InputEditVersion;editor.OnApplyPileDistanceFactorToModelsAllPiles();Assert.AreEqual(version,vm.InputEditVersion);
        vm.UndoCommand.Execute(null);Assert.AreEqual(2.0,vm.CurrentInputModel!.PileLayoutItems[0].PileSpacingFactor);vm.RedoCommand.Execute(null);Assert.AreEqual(3.0,vm.CurrentInputModel!.PileLayoutItems[0].PileSpacingFactor);
    }
    [TestMethod] public void EmbedmentEditsSupportUndoRedoAndDecliningChanges()
    {
        using var vm=Create();vm.CurrentInputModel!.EmbedmentInput.EmbedmentLayers=[];vm.CurrentInputModel!.EmbedmentInput.EmbedmentLayersCount=0;
        vm.EmbedmentLayerCount=2;Assert.AreEqual(2,vm.CurrentInputModel!.EmbedmentInput.EmbedmentLayers.Count);vm.EmbedmentBottomAltitude=-12;
        vm.UndoCommand.Execute(null);Assert.AreEqual(0.0,vm.EmbedmentBottomAltitude);vm.UndoCommand.Execute(null);Assert.AreEqual(0,vm.EmbedmentLayerCount);
        vm.RedoCommand.Execute(null);vm.RedoCommand.Execute(null);Assert.AreEqual(-12.0,vm.EmbedmentBottomAltitude);
        vm.IsElementSplit=true;int version=vm.InputEditVersion;vm.EmbedmentBottomAltitude=-12;Assert.AreEqual(version,vm.InputEditVersion);vm.EmbedmentLayerCount=3;Assert.AreEqual(2,vm.EmbedmentLayerCount);Assert.IsTrue(vm.IsElementSplit);
        Assert.ThrowsException<ArgumentOutOfRangeException>(()=>vm.EmbedmentBottomAltitude=double.NaN);
    }
    [TestMethod] public void DisplayModeChangeIsSavedAndUndoable()
    {
        using var vm=Create();bool old=vm.IsAxialForceVariationMode;vm.IsAxialForceVariationMode=!old;Assert.IsTrue(vm.HasUnsavedWork);
        vm.UndoCommand.Execute(null);Assert.AreEqual(old,vm.IsAxialForceVariationMode);Assert.AreEqual(old,PileDesign.Common.AxialForceModeContext.IsVariationMode);
        vm.RedoCommand.Execute(null);Assert.AreEqual(!old,vm.IsAxialForceVariationMode);vm.IsAxialForceVariationMode=old;
    }

    [TestMethod] public void SettlementLayerCopyRejectsInvalidLayerAndSupportsUndoRedo()
    {
        using var vm=Create();var ground=vm.CurrentInputModel!.GroundsInput[0];vm.CurrentInputModel!.PileGroupSettlement.LoadingPlaneAltitude=0;
        ground.GroundLayers=[new GroundLayerInput {BottomAltitude=-5,Density=18,Vs=150,PoissonsRatio=0.3},new GroundLayerInput {BottomAltitude=-10,Density=18,Vs=0,PoissonsRatio=0.3}];
        vm.SelectedGroundInputModelNo=1;Run(vm,"GroundInputCopyToSettlementGroundLayers");Assert.AreEqual(0,vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers.Count);Assert.IsFalse(vm.HasUnsavedWork);
        ground.GroundLayers[1].Vs=200;Run(vm,"GroundInputCopyToSettlementGroundLayers");Assert.AreEqual(2,vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers.Count);int version=vm.InputEditVersion;
        Run(vm,"GroundInputCopyToSettlementGroundLayers");Assert.AreEqual(version,vm.InputEditVersion);
        vm.UndoCommand.Execute(null);Assert.AreEqual(0,vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers.Count);vm.RedoCommand.Execute(null);Assert.AreEqual(2,vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers.Count);
    }
    [TestMethod] public void CancelingOverturningDialogPreservesHistory()
    {
        var error=XamlSmokeTestSupport.RunOnStaThread(()=> {
            using var vm=Create();vm.IsElementSplit=true;int version=vm.InputEditVersion;
            var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(20)};bool closed=false;
            timer.Tick+=(_,_)=>{var window=System.Windows.Application.Current.Windows.Cast<System.Windows.Window>().OfType<AutoOverturningMomentWindow>().FirstOrDefault();if(window==null)return;timer.Stop();window.Close();closed=true;};
            timer.Start();try {Run(vm,"AutoOverturningMoment");}finally{timer.Stop();}
            Assert.IsTrue(closed);Assert.IsTrue(vm.IsElementSplit);Assert.IsFalse(vm.HasUnsavedWork);Assert.AreEqual(version,vm.InputEditVersion);
        },out bool timedOut);Assert.IsFalse(timedOut);Assert.IsNull(error,error?.ToString());
    }
    [TestMethod] public void BulkPileEditRecordsBothStates()
    {
        using var vm=Create();vm.CurrentInputModel!.PileLayoutItems=[new PileLayoutDataItem {IsSelected=true,GroupPileFactor=0.5}];
        Run(vm,"EditPileLayoutWindow_EditPileLayoutCompleted",new object(),new EditPileLayoutWindow.EditPileLayoutEventArgs {IsApplicablePileGroupFactor=true,PileGroupFactor=0.7});
        Assert.AreEqual(0.7,vm.CurrentInputModel!.PileLayoutItems[0].GroupPileFactor);vm.UndoCommand.Execute(null);Assert.AreEqual(0.5,vm.CurrentInputModel!.PileLayoutItems[0].GroupPileFactor);vm.RedoCommand.Execute(null);Assert.AreEqual(0.7,vm.CurrentInputModel!.PileLayoutItems[0].GroupPileFactor);
    }
    [TestMethod] public void SharedEditApiSkipsNoChangeAndDeclinedConfirmation()
    {
        using var vm=Create();bool ran=false;Assert.IsFalse(vm.TryApplyInputEdit(false,()=>ran=true));vm.IsElementSplit=true;
        Assert.IsFalse(vm.TryApplyInputEdit(true,()=>ran=true,confirmElementSplit:true));Assert.IsFalse(ran);Assert.IsFalse(vm.HasUnsavedWork);
    }

    [TestMethod] public void EmbedmentBindingHandlesSelectionWithoutMouseAndRestoresDeclinedValue()
    {
        var error=XamlSmokeTestSupport.RunOnStaThread(()=> {
            using var vm=Create();vm.CurrentInputModel!.EmbedmentInput.EmbedmentLayers=[];vm.CurrentInputModel!.EmbedmentInput.EmbedmentLayersCount=0;
            var field=new System.Windows.Controls.ComboBox {DataContext=vm,ItemsSource=new[]{0,1,2,3,4,5}};
            field.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,new System.Windows.Data.Binding("EmbedmentLayerCount") {Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged,ValidatesOnExceptions=true});
            field.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.DataBind);
            field.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,1);
            field.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedItemProperty)!.UpdateSource();
            Assert.AreEqual(1,vm.EmbedmentLayerCount);Assert.AreEqual(1,vm.CurrentInputModel!.EmbedmentInput.EmbedmentLayers.Count);
            vm.IsElementSplit=true;field.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,2);field.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedItemProperty)!.UpdateSource();
            field.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.DataBind);
            Assert.AreEqual(1,vm.EmbedmentLayerCount);Assert.AreEqual(1,field.SelectedItem);Assert.IsTrue(vm.IsElementSplit);
        },out bool timedOut);Assert.IsFalse(timedOut);Assert.IsNull(error,error?.ToString());
    }
}
