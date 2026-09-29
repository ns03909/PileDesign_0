using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using PileDesign.Views;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace TestProject1;

[TestClass]
[DoNotParallelize]
public class BeamPropertyEditingTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;
    private static MainWindowViewModel Create()
    {
        var vm = new MainWindowViewModel { CurrentInputModel = new InputModel
        { FoundationBeamInput = new FoundationBeamInput { Materials = [], Sections = [], Beams = [] } } };
        vm.MarkProjectReplaced();
        return vm;
    }
    private static UndoManager History(MainWindowViewModel vm) => (UndoManager)typeof(MainWindowViewModel)
        .GetField("_undoManager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
    private static void Run(MainWindowViewModel vm, string name, params object[] args) => typeof(MainWindowViewModel)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, args);

    [TestMethod] public void MaterialAddAndEditUndoRedoRestoresValues()
    {
        var vm = Create();
        var result = new BeamMaterialWizardWindow.MaterialWizardResult { Name = "First", YoungModulus = 100, ShearModulus = 40, PoissonRatio = 0.25 };
        Assert.IsTrue(vm.ApplyBeamMaterial(null, result, out var added)); Assert.AreEqual(1, added);
        vm.UndoCommand.Execute(null); Assert.AreEqual(0, vm.CurrentInputModel!.FoundationBeamInput.Materials.Count);
        vm.RedoCommand.Execute(null); Assert.AreEqual("First", vm.CurrentInputModel!.FoundationBeamInput.Materials[0].Name);
        result.Name = "Edited"; result.YoungModulus = 200;
        Assert.IsTrue(vm.ApplyBeamMaterial(1, result, out _));
        vm.UndoCommand.Execute(null);
        Assert.AreEqual("First", vm.CurrentInputModel!.FoundationBeamInput.Materials[0].Name);
        Assert.AreEqual(100.0, vm.CurrentInputModel!.FoundationBeamInput.Materials[0].YoungModulus);
        vm.RedoCommand.Execute(null); Assert.AreEqual(200.0, vm.CurrentInputModel!.FoundationBeamInput.Materials[0].YoungModulus);
        result.YoungModulus = 300;
        Assert.IsTrue(vm.ApplyBeamMaterial(1, result, out _));
        vm.UndoCommand.Execute(null); Assert.AreEqual(200.0, vm.CurrentInputModel!.FoundationBeamInput.Materials[0].YoungModulus);
        vm.UndoCommand.Execute(null); Assert.AreEqual(100.0, vm.CurrentInputModel!.FoundationBeamInput.Materials[0].YoungModulus);
    }

    [TestMethod] public void SectionAddAndEditUndoRedoRestoresValues()
    {
        var vm = Create();
        var result = new BeamSectionWizardWindow.SectionWizardResult { Name = "First", Width = 0.3, Height = 0.5, Area = 0.15, AFactor = 1, IxxFactor = 1 };
        Assert.IsTrue(vm.ApplyBeamSection(null, result, out var added)); Assert.AreEqual(1, added);
        vm.UndoCommand.Execute(null); Assert.AreEqual(0, vm.CurrentInputModel!.FoundationBeamInput.Sections.Count);
        vm.RedoCommand.Execute(null);
        result.Width = 0.6; result.AFactor = 0.7;
        Assert.IsTrue(vm.ApplyBeamSection(1, result, out _));
        vm.UndoCommand.Execute(null);
        Assert.AreEqual(0.3, vm.CurrentInputModel!.FoundationBeamInput.Sections[0].Width);
        Assert.AreEqual(1.0, vm.CurrentInputModel!.FoundationBeamInput.Sections[0].AFactor);
        vm.RedoCommand.Execute(null); Assert.AreEqual(0.7, vm.CurrentInputModel!.FoundationBeamInput.Sections[0].AFactor);
    }

    [DataTestMethod] [DataRow(true)] [DataRow(false)]
    public void DeleteUndoRestoresListAndBeamReference(bool material)
    {
        var vm = Create(); var input = vm.CurrentInputModel!.FoundationBeamInput;
        input.Materials = [new BeamMaterial { Name = "A" }, new BeamMaterial { Name = "B" }, new BeamMaterial { Name = "C" }];
        input.Sections = [new BeamSection { Name = "A" }, new BeamSection { Name = "B" }, new BeamSection { Name = "C" }];
        input.Beams.Add(new FoundationBeam { MaterialNo = 3, SectionNo = 3 });
        Run(vm, material ? "DeleteBeamMaterial" : "DeleteBeamSection", material ? (object)input.Materials[1] : input.Sections[1]);
        Assert.AreEqual(2, material ? input.Beams[0].MaterialNo : input.Beams[0].SectionNo);
        vm.UndoCommand.Execute(null); input = vm.CurrentInputModel!.FoundationBeamInput;
        Assert.AreEqual(3, material ? input.Materials.Count : input.Sections.Count);
        Assert.AreEqual("B", material ? input.Materials[1].Name : input.Sections[1].Name);
        Assert.AreEqual(3, material ? input.Beams[0].MaterialNo : input.Beams[0].SectionNo);
        vm.RedoCommand.Execute(null); input = vm.CurrentInputModel!.FoundationBeamInput;
        Assert.AreEqual(2, material ? input.Materials.Count : input.Sections.Count);
        Assert.AreEqual(2, material ? input.Beams[0].MaterialNo : input.Beams[0].SectionNo);
    }

    [DataTestMethod] [DataRow(true)] [DataRow(false)]
    public void OutsideOrUsedDeletionDoesNotEdit(bool material)
    {
        var vm = Create(); var input = vm.CurrentInputModel!.FoundationBeamInput;
        input.Materials.Add(new BeamMaterial()); input.Sections.Add(new BeamSection());
        input.Beams.Add(new FoundationBeam { MaterialNo = 1, SectionNo = 1 });
        int version = vm.InputEditVersion, history = History(vm).History.Count;
        string command = material ? "DeleteBeamMaterial" : "DeleteBeamSection";
        Run(vm, command, material ? (object)new BeamMaterial() : new BeamSection());
        Run(vm, command, material ? (object)input.Materials[0] : input.Sections[0]);
        Assert.AreEqual(1, input.Materials.Count); Assert.AreEqual(1, input.Sections.Count);
        Assert.AreEqual(1, input.Beams[0].MaterialNo); Assert.AreEqual(1, input.Beams[0].SectionNo);
        Assert.AreEqual(version, vm.InputEditVersion); Assert.AreEqual(history, History(vm).History.Count); Assert.IsFalse(vm.HasUnsavedWork);
    }

    [TestMethod] public void BulkEditUndoRedoAndNoOp()
    {
        var vm = Create(); var input = vm.CurrentInputModel!.FoundationBeamInput;
        input.Materials = [new BeamMaterial(), new BeamMaterial()]; input.Sections = [new BeamSection(), new BeamSection()];
        input.Beams = [new FoundationBeam { MaterialNo = 1, SectionNo = 1 }, new FoundationBeam { MaterialNo = 2, SectionNo = 1 }];
        var result = new EditBeamElementWindow.BeamElementEditResult { IsApplicableMaterialNo = true, MaterialNo = 2, IsApplicableSectionNo = true, SectionNo = 2 };
        Assert.IsTrue(vm.ApplyBeamElementEdit(input.Beams.ToArray(), result));
        int version = vm.InputEditVersion;
        Assert.IsFalse(vm.ApplyBeamElementEdit(input.Beams.ToArray(), result)); Assert.AreEqual(version, vm.InputEditVersion);
        vm.UndoCommand.Execute(null); input = vm.CurrentInputModel!.FoundationBeamInput;
        Assert.AreEqual(1, input.Beams[0].MaterialNo); Assert.AreEqual(2, input.Beams[1].MaterialNo);
        Assert.IsTrue(input.Beams.All(b => b.SectionNo == 1));
        vm.RedoCommand.Execute(null); Assert.IsTrue(vm.CurrentInputModel!.FoundationBeamInput.Beams.All(b => b.MaterialNo == 2 && b.SectionNo == 2));
    }

    [TestMethod] public void DecliningSplitDiscardPreservesInputAndHistory()
    {
        var vm = Create(); var input = vm.CurrentInputModel!.FoundationBeamInput;
        input.Materials = [new BeamMaterial(), new BeamMaterial()]; input.Sections = [new BeamSection()];
        input.Beams = [new FoundationBeam { MaterialNo = 1 }]; vm.IsElementSplit = true;
        int version = vm.InputEditVersion, history = History(vm).History.Count;
        Assert.IsFalse(vm.ApplyBeamMaterial(null, new() { Name = "New" }, out _));
        Assert.IsFalse(vm.ApplyBeamSection(null, new() { Name = "New" }, out _));
        Assert.IsFalse(vm.ApplyBeamElementEdit(input.Beams.ToArray(), new() { IsApplicableMaterialNo = true, MaterialNo = 2 }));
        Assert.AreEqual(2, input.Materials.Count); Assert.AreEqual(1, input.Sections.Count); Assert.AreEqual(1, input.Beams[0].MaterialNo);
        Assert.IsTrue(vm.IsElementSplit); Assert.IsFalse(vm.HasUnsavedWork);
        Assert.AreEqual(version, vm.InputEditVersion); Assert.AreEqual(history, History(vm).History.Count);
    }

    [TestMethod] public void DuplicateFreeAndEmptySelectionDoNotSaveOrInvalidate()
    {
        var vm = Create(); vm.CurrentInputModel!.FoundationBeamInput.Beams.Add(new FoundationBeam());
        vm.IsElementSplit = true; int version = vm.InputEditVersion, history = History(vm).History.Count;
        Run(vm, "OnDeleteDupulicateElements"); Run(vm, "EditBeamElements");
        Assert.IsTrue(vm.IsElementSplit); Assert.IsFalse(vm.HasUnsavedWork);
        Assert.AreEqual(version, vm.InputEditVersion); Assert.AreEqual(history, History(vm).History.Count);
    }

    [TestMethod] public void UnchangedWizardValuesAndInvalidBulkReferencePreserveHistory()
    {
        var vm = Create(); var input = vm.CurrentInputModel!.FoundationBeamInput;
        input.Materials.Add(new BeamMaterial { Name = "Same", YoungModulus = 100, ShearModulus = 40, PoissonRatio = 0.25 });
        input.Sections.Add(new BeamSection()); input.Beams.Add(new FoundationBeam { MaterialNo = 1 });
        var section = input.Sections[0];
        vm.IsElementSplit = true; int version = vm.InputEditVersion, history = History(vm).History.Count;
        Assert.IsTrue(vm.ApplyBeamMaterial(1, new() { Name = "Same", YoungModulus = 100, ShearModulus = 40, PoissonRatio = 0.25 }, out _));
        Assert.IsTrue(vm.ApplyBeamSection(1, new()
        {
            Name = section.Name, Width = section.Width, Height = section.Height, Area = section.Area,
            ShearAreaY = section.ShearAreaY, ShearAreaZ = section.ShearAreaZ, TorsionalMoment = section.TorsionalMoment,
            MomentOfInertiaYY = section.MomentOfInertiaYY, MomentOfInertiaZZ = section.MomentOfInertiaZZ,
            AFactor = section.AFactor, AyFactor = section.AyFactor, AzFactor = section.AzFactor,
            IxxFactor = section.IxxFactor, IyyFactor = section.IyyFactor, IzzFactor = section.IzzFactor
        }, out _));
        Assert.IsFalse(vm.ApplyBeamElementEdit(input.Beams.ToArray(), new() { IsApplicableMaterialNo = true, MaterialNo = 0 }));
        Assert.IsFalse(vm.ApplyBeamElementEdit(input.Beams.ToArray(), new() { IsApplicableMaterialNo = true, MaterialNo = 2 }));
        Assert.AreEqual(version, vm.InputEditVersion); Assert.AreEqual(history, History(vm).History.Count);
        Assert.IsTrue(vm.IsElementSplit); Assert.IsFalse(vm.HasUnsavedWork);
    }

    [TestMethod] public void ActualDuplicateDeletionUndoRedoRestoresBothBeams()
    {
        var vm = Create(); var input = vm.CurrentInputModel!.FoundationBeamInput;
        input.Beams = [new FoundationBeam(), new FoundationBeam()];
        Run(vm, "OnDeleteDupulicateElements"); Assert.AreEqual(1, input.Beams.Count);
        vm.UndoCommand.Execute(null); Assert.AreEqual(2, vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);
        vm.RedoCommand.Execute(null); Assert.AreEqual(1, vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);
    }

    [DataTestMethod] [DataRow("OpenMaterialWizard")] [DataRow("OpenSectionWizard")] [DataRow("EditBeamElements")]
    public void CancellingActualDialogDoesNotInvalidate(string command)
    {
        var error = XamlSmokeTestSupport.RunOnStaThread(() =>
        {
            var vm = Create(); vm.IsElementSplit = true;
            vm.CurrentInputModel!.FoundationBeamInput.Beams.Add(new FoundationBeam { IsSelected = true });
            int version = vm.InputEditVersion, history = History(vm).History.Count;
            var timer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(20) };
            bool closed = false;
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.Cast<Window>().FirstOrDefault(w =>
                    w is BeamMaterialWizardWindow or BeamSectionWizardWindow or EditBeamElementWindow);
                if (dialog == null) return;
                timer.Stop(); dialog.DialogResult = false; closed = true;
            };
            timer.Start();
            try { Run(vm, command); } finally { timer.Stop(); }
            Assert.IsTrue(closed); Assert.IsTrue(vm.IsElementSplit); Assert.IsFalse(vm.HasUnsavedWork);
            Assert.AreEqual(version, vm.InputEditVersion); Assert.AreEqual(history, History(vm).History.Count);
        }, out bool timedOut);
        Assert.IsFalse(timedOut); Assert.IsNull(error, error?.ToString());
    }
}
