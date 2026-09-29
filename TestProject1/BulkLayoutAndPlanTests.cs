using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using PileDesign.Views;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace TestProject1;

[TestClass]
[DoNotParallelize]
public class BulkLayoutAndPlanTests
{
    private static void Run(MainWindowViewModel vm, string method, params object[] args) =>
        typeof(MainWindowViewModel).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, args);

    [TestMethod] public void BulkEditRecordsOneUndoAndRejectsInvalidEditsWithoutHistory()
    {
        bool unattended = MessageService.IsUnattended; MessageService.IsUnattended = true;
        try
        {
            var pile = new PileLayoutDataItem { IsSelected = true, GroupPileFactor = 0.5 };
            var vm = new MainWindowViewModel { CurrentInputModel = new InputModel { PileLayoutItems = [pile] } };
            vm.MarkProjectReplaced(); int version = vm.InputEditVersion;
            var bad = new EditPileLayoutWindow.EditPileLayoutEventArgs { IsApplicablePileGroupFactor = true, PileGroupFactor = 2 };
            Run(vm, "EditPileLayoutWindow_EditPileLayoutCompleted", null!, bad);
            Assert.IsTrue(bad.Cancel); Assert.AreEqual(version, vm.InputEditVersion); Assert.IsFalse(vm.HasUnsavedWork);
            var good = new EditPileLayoutWindow.EditPileLayoutEventArgs { IsApplicablePileGroupFactor = true, PileGroupFactor = 0.75 };
            Run(vm, "EditPileLayoutWindow_EditPileLayoutCompleted", null!, good);
            Assert.IsFalse(good.Cancel); Assert.AreEqual(0.75, pile.GroupPileFactor);
            Assert.IsTrue(vm.HasUnsavedWork); Assert.AreEqual(version + 1, vm.InputEditVersion);
            vm.UndoCommand.Execute(null);
            Assert.AreEqual(0.5, vm.CurrentInputModel.PileLayoutItems[0].GroupPileFactor);
        }
        finally { MessageService.IsUnattended = unattended; }
    }

    [TestMethod] public void NumericErrorsAndRejectedEditKeepTheWindowInput()
    {
        var error = XamlSmokeTestSupport.RunOnStaThread(() =>
        {
            var window = new EditPileLayoutWindow(new MainWindowViewModel());
            try
            {
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                var field = (TextBox)window.FindName("TextBoxPileTopLevel");
                var check = (CheckBox)window.FindName("CheckBoxPileTopLevel");
                check.IsChecked = true;
                field.SetCurrentValue(TextBox.TextProperty, "invalid");
                Assert.IsFalse(window.CommitNumericInputs());
                field.SetCurrentValue(TextBox.TextProperty, "12");
                Assert.IsTrue(window.CommitNumericInputs());
                string retainedText = field.Text;
                bool closed = false; window.Closed += (_, _) => closed = true;
                window.EditPileLayoutCompleted += (_, e) => { Assert.AreEqual(12.0, e.PileTopLevel); e.Cancel = true; };
                typeof(EditPileLayoutWindow).GetMethod("OkButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [null, new RoutedEventArgs()]);
                Assert.IsFalse(closed); Assert.AreEqual(retainedText, field.Text);
            }
            finally { window.Close(); }
        }, out bool timedOut);
        Assert.IsFalse(timedOut); Assert.IsNull(error, error?.ToString());
    }

    [DataTestMethod] [DataRow(-1.0)] [DataRow(double.NaN)] [DataRow(double.PositiveInfinity)]
    public void InvalidMarginIsRejected(double margin) =>
        Assert.ThrowsException<ArgumentException>(() => BoundingBoxCalculator.Calculate([new PileLayoutDataItem()], margin));

    [TestMethod] public void ExpandedBoundsOverflowIsRejected()
    {
        Assert.ThrowsException<ArgumentException>(() => BoundingBoxCalculator.Calculate([new PileLayoutDataItem { X = double.MaxValue }], double.MaxValue));
        var box = BoundingBoxCalculator.Calculate([new PileLayoutDataItem { X = 1, Y = 2 }, new PileLayoutDataItem { X = 3, Y = 4 }], 1);
        Assert.AreEqual(0.0, box.MinX); Assert.AreEqual(5.0, box.MaxY);
    }

    [TestMethod] public void InvalidGeneratedLoadDoesNotAddLoadOrUndo()
    {
        bool unattended = MessageService.IsUnattended; MessageService.IsUnattended = true;
        try
        {
            var vm = new MainWindowViewModel { CurrentInputModel = new InputModel
            { PileLayoutItems = [new PileLayoutDataItem { X = 0, Y = 0, AxialForceVL0 = double.MaxValue },
                new PileLayoutDataItem { X = 2, Y = 2, AxialForceVL0 = double.MaxValue }],
                PileGroupSettlement = new PileGroupSettlement { LoadingType = "任意矩形", RectLoads = [] } } };
            vm.MarkProjectReplaced(); int version = vm.InputEditVersion;
            Run(vm, "OnAdjustRectLoadPlan");
            Assert.AreEqual(0, vm.CurrentInputModel.PileGroupSettlement.RectLoads.Count);
            Assert.IsFalse(vm.HasUnsavedWork); Assert.AreEqual(version, vm.InputEditVersion);
        }
        finally { MessageService.IsUnattended = unattended; }
    }

    [DataTestMethod]
    [DataRow("OnAdjustRectLoadPlan")]
    [DataRow("OnAdjustEmbedmentPlan")]
    public void InvalidPlanMarginPreservesInputAndAnalysisState(string method)
    {
        bool unattended = MessageService.IsUnattended; MessageService.IsUnattended = true;
        try
        {
            var layer = new EmbedmentDataItem { X1 = 9, X2 = 10, Y1 = 9, Y2 = 10 };
            var vm = new MainWindowViewModel { CurrentInputModel = new InputModel
            { PileLayoutItems = [new PileLayoutDataItem(), new PileLayoutDataItem { X = 2, Y = 2 }],
                PileGroupSettlement = new PileGroupSettlement { LoadingType = "任意矩形", RectLoads = [] },
                EmbedmentInput = new EmbedmentInput { EmbedmentLayers = [layer] } },
                RectLoadPileDistance = -1, EmbedmentPileDistance = -1 };
            vm.MarkProjectReplaced(); vm.IsElementSplit = true; int version = vm.InputEditVersion;
            Run(vm, method);
            Assert.AreEqual(9.0, layer.X1); Assert.AreEqual(0, vm.CurrentInputModel.PileGroupSettlement.RectLoads.Count);
            Assert.IsTrue(vm.IsElementSplit); Assert.IsFalse(vm.HasUnsavedWork); Assert.AreEqual(version, vm.InputEditVersion);
        }
        finally { MessageService.IsUnattended = unattended; }
    }

    [TestMethod] public void LayoutRestoresUnitsAndRejectsDuplicateIndicesBeforeChangingWidths()
    {
        var error = XamlSmokeTestSupport.RunOnStaThread(() =>
        {
            var grid = new DataGrid();
            foreach (var name in new[] { "A", "B", "C" }) grid.Columns.Add(new DataGridTextColumn { Header = name, Width = new DataGridLength(50) });
            DataGridColumnSetting[] settings = [new() { Index = 0, Header = "A", Width = 2, WidthUnit = DataGridLengthUnitType.Star, DisplayIndex = 2 },
                new() { Index = 1, Header = "B", Width = 1, WidthUnit = DataGridLengthUnitType.Auto, DisplayIndex = 0 },
                new() { Index = 2, Header = "C", Width = 80, DisplayIndex = 1 }];
            Assert.IsTrue(LayoutService.ApplyColumnSettings(settings, grid));
            Assert.IsTrue(grid.Columns[0].Width.IsStar); Assert.AreEqual(2.0, grid.Columns[0].Width.Value);
            Assert.IsTrue(grid.Columns[1].Width.IsAuto); Assert.IsTrue(grid.Columns[2].Width.IsAbsolute);
            var widths = grid.Columns.Select(c => c.Width).ToArray();
            settings[1].Index = 0;
            Assert.IsFalse(LayoutService.ApplyColumnSettings(settings, grid));
            CollectionAssert.AreEqual(widths, grid.Columns.Select(c => c.Width).ToArray());
            settings[1].Index = 1; settings[1].DisplayIndex = 2;
            Assert.IsFalse(LayoutService.ApplyColumnSettings(settings, grid));
        }, out bool timedOut);
        Assert.IsFalse(timedOut); Assert.IsNull(error, error?.ToString());
    }
}
