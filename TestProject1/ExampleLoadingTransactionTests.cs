using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TestProject1;

[TestClass]
[DoNotParallelize]
public class ExampleLoadingTransactionTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;
    private static Task Load(MainWindowViewModel vm, bool group, string name, string? supplementary = null) =>
        (Task)typeof(MainWindowViewModel).GetMethod(group ? "LoadGroupSettlementExampleAsync" : "LoadPileExampleAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, group ? [name, "Test"] : [name, "Test", supplementary])!;
    private static int History(MainWindowViewModel vm) => ((UndoManager)typeof(MainWindowViewModel)
        .GetField("_undoManager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!).History.Count;
    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
            task.GetAwaiter().OnCompleted(() => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    private static void Sta(Action action)
    {
        var error = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
        Assert.IsFalse(timedOut); Assert.IsNull(error, error?.ToString());
    }

    [DataTestMethod] [DataRow(false)] [DataRow(true)]
    public void MissingExamplePreservesModelPathSplitAndHistory(bool group) => Sta(() =>
    {
        using var vm = new MainWindowViewModel();
        vm.CurrentFilePath = "original.pile"; vm.IsElementSplit = true; vm.MarkProjectReplaced();
        var input = vm.CurrentInputModel!; int history = History(vm), version = vm.InputEditVersion;
        Assert.ThrowsException<FileNotFoundException>(() => Wait(Load(vm, group, "Missing_" + Guid.NewGuid().ToString("N"))));
        Assert.AreSame(input, vm.CurrentInputModel); Assert.AreEqual("original.pile", vm.CurrentFilePath);
        Assert.IsTrue(vm.IsElementSplit); Assert.IsFalse(vm.HasUnsavedWork);
        Assert.AreEqual(history, History(vm)); Assert.AreEqual(version, vm.InputEditVersion);
    });

    [TestMethod] public void ApplicationFailurePreservesLiveModelAndNotifications() => Sta(() =>
    {
        using var vm = new MainWindowViewModel(); vm.MarkProjectReplaced();
        vm.CurrentFilePath = "original.pile"; vm.IsElementSplit = true;
        var data = PileExampleLoader.LoadFromFile("PileExample3_1"); data.LoadCaseLevel1 = null!;
        string path = Path.Combine(Path.GetTempPath(), "PileExample_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(data));
        try
        {
            var original = vm.CurrentInputModel!; int history = History(vm);
            Assert.ThrowsException<NullReferenceException>(() => Wait(Load(vm, false, Path.ChangeExtension(path, null))));
            Assert.AreSame(original, vm.CurrentInputModel); Assert.AreEqual("original.pile", vm.CurrentFilePath);
            Assert.IsTrue(vm.IsElementSplit); Assert.IsFalse(vm.HasUnsavedWork); Assert.AreEqual(history, History(vm));
            Assert.IsFalse((bool)typeof(InputModel).GetField("_suppressSoilPileNotify", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original)!);
        }
        finally { File.Delete(path); }
    });

    [TestMethod] public void SupplementaryReadFailureDoesNotCommitPileExample() => Sta(() =>
    {
        using var vm = new MainWindowViewModel(); vm.MarkProjectReplaced();
        var original = vm.CurrentInputModel!; int history = History(vm);
        Assert.ThrowsException<FileNotFoundException>(() => Wait(Load(vm, false, "PileExample5", "Missing_" + Guid.NewGuid().ToString("N"))));
        Assert.AreSame(original, vm.CurrentInputModel); Assert.AreEqual(history, History(vm)); Assert.IsFalse(vm.HasUnsavedWork);
    });

    [TestMethod] public void ProjectReplacementDuringReadIsNotOverwritten() => Sta(() =>
    {
        using var vm = new MainWindowViewModel(); vm.MarkProjectReplaced();
        var pending = Load(vm, false, "PileExample3_1");
        var replacement = new InputModel(); vm.CurrentInputModel = replacement; vm.MarkProjectReplaced();
        int history = History(vm);
        Exception? failure = null;
        try { Wait(pending); } catch (Exception ex) { failure = ex; }
        Assert.IsInstanceOfType(failure, typeof(InvalidOperationException), failure?.ToString());
        Assert.AreSame(replacement, vm.CurrentInputModel); Assert.AreEqual(history, History(vm));
    });

    [DataTestMethod] [DataRow(false, "PileExample3_1")] [DataRow(true, "GroupSettlement5")]
    public void SuccessfulLoadGeneratesSoilPilesAndResetsFlags(bool group, string name) => Sta(() =>
    {
        using var vm = new MainWindowViewModel(); vm.MarkProjectReplaced(); vm.IsElementSplit = true;
        vm.CurrentFilePath = "original.pile";
        Wait(Load(vm, group, name));
        Assert.IsTrue(vm.CurrentInputModel!.PileLayoutItems.Count > 0);
        Assert.IsTrue(vm.CurrentInputModel!.ElementDivision.SoilPiles.Count > 0);
        Assert.IsFalse(vm.IsElementSplit); Assert.IsFalse(vm.HasUnsavedWork); Assert.IsNull(vm.CurrentFilePath);
        Assert.AreEqual("Test", vm.LoadedExampleName);
    });
}
