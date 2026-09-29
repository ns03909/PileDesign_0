using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;

namespace TestProject1;

/// <summary>
/// 沈下用土層の表のセル編集 (<see cref="MainWindowViewModel.CommitSettlementSoilLayerCellEdit"/>)。
///
/// <para><b>値が変わらない確定では Undo の履歴を作らない。</b> セルに入って出ただけ・同じ値を打ち直しただけで履歴が
/// 増えると、Ctrl+Z を押しても何も戻らない段が挟まる。以前はこの表の編集は画面側で受けていて履歴自体を作らず
/// (Ctrl+Z で戻らない)、層厚も書き込み前の下端で計算していた。</para>
///
/// <para>表の列と同じ書式 (StringFormat) のバインディングを TextBox に張り、DataGrid が渡すのと同じ
/// 引数で呼ぶ。</para>
/// </summary>
[TestClass]
[DoNotParallelize]
public class SettlementSoilLayerEditUndoTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

    private static void Sta(Action action)
    {
        var error = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
        Assert.IsFalse(timedOut); Assert.IsNull(error, error?.ToString());
    }

    private static int History(MainWindowViewModel vm) => ((UndoManager)typeof(MainWindowViewModel)
        .GetField("_undoManager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!).History.Count;

    /// <summary>上端 +0.00、下端 -2.00 / -5.00 の 2 層。変形係数は表示で丸まる値にしておく。</summary>
    private static MainWindowViewModel Create()
    {
        var input = new InputModel { PileGroupSettlement = new PileGroupSettlement() };
        input.PileGroupSettlement.SoilLayersTopAltitude = 0.0;
        input.PileGroupSettlement.SettlementSoilLayers =
        [
            new SettlementSoilLayer { BottomAltitude = -2.0, Thickness = 2.0, Ek = 12345.6, PoissonsRatio = 0.3, Note = null! },
            new SettlementSoilLayer { BottomAltitude = -5.0, Thickness = 3.0, Ek = 20000, PoissonsRatio = 0.3, Note = "" },
        ];
        var vm = new MainWindowViewModel { CurrentInputModel = input };
        vm.MarkProjectReplaced();
        return vm;
    }

    /// <summary>
    /// 表の列と同じ書式でセルを編集状態にし、<paramref name="typed"/> を打ち込んで (null なら触らずに) 確定する。
    /// 戻り値は DataGrid が受け取る「確定を取り消したか」。
    /// </summary>
    private static bool Commit(MainWindowViewModel vm, SettlementSoilLayer layer, string path, string? format, string? typed)
    {
        var box = new TextBox { DataContext = layer };
        box.SetBinding(TextBox.TextProperty, new Binding(path)
        { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit, StringFormat = format });
        if (typed != null) box.Text = typed;
        var args = new DataGridCellEditEndingEventArgs(new DataGridTextColumn(), new DataGridRow { Item = layer }, box, DataGridEditAction.Commit);
        vm.CommitSettlementSoilLayerCellEdit(args);
        // DataGrid は取り消されなければ、このあと自分でも書き込む
        if (!args.Cancel) box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        return args.Cancel;
    }

    /// <summary><b>本題。</b> セルに入って出ただけ・同じ値を打ち直しただけでは履歴を作らず、「再解析が必要」にもしない。</summary>
    [TestMethod]
    public void UnchangedCommits_DoNotAddHistory() => Sta(() =>
    {
        using var vm = Create();
        var layer = vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers[1];
        int history = History(vm);

        Commit(vm, layer, nameof(SettlementSoilLayer.BottomAltitude), "{0:+0.00;-0.00;+0.00}", typed: null);
        Commit(vm, layer, nameof(SettlementSoilLayer.BottomAltitude), "{0:+0.00;-0.00;+0.00}", typed: "-5");
        Commit(vm, layer, nameof(SettlementSoilLayer.PoissonsRatio), "N2", typed: " 0.30 ");
        Commit(vm, layer, nameof(SettlementSoilLayer.Note), null, typed: "");

        Assert.AreEqual(history, History(vm), "値の変わらない確定で Undo の履歴が増えている");
        Assert.IsFalse(vm.HasUnsavedWork, "値の変わらない確定で未保存の印が立っている");
        Assert.AreEqual(-5.0, layer.BottomAltitude);
    });

    /// <summary>
    /// 表示で丸めてある値 (変形係数 12345.6 → 「12,346」) のセルを、触らずに確定しても値を丸めないこと。
    /// 表示の文字をそのまま書き戻すと、読めずに入力の誤りになるか、丸めた値が履歴なしで入る。
    /// </summary>
    [TestMethod]
    public void UntouchedRoundedCell_KeepsTheExactValue() => Sta(() =>
    {
        using var vm = Create();
        var layer = vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers[0];
        int history = History(vm);

        Assert.IsFalse(Commit(vm, layer, nameof(SettlementSoilLayer.Ek), "N0", typed: null));

        Assert.AreEqual(12345.6, layer.Ek, "触っていないセルの確定で値が丸められた");
        Assert.AreEqual(history, History(vm));
    });

    /// <summary>値が変わる確定は履歴に残り、Undo 1 回で戻ること。層厚は書き込んだあとの下端で計算すること。</summary>
    [TestMethod]
    public void ChangedCommit_AddsOneStep_AndUndoRestores() => Sta(() =>
    {
        using var vm = Create();
        var layers = vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers;
        int history = History(vm);

        Assert.IsFalse(Commit(vm, layers[1], nameof(SettlementSoilLayer.BottomAltitude), "{0:+0.00;-0.00;+0.00}", typed: "-6.5"));

        Assert.IsTrue(History(vm) > history, "値の変わる確定が履歴に残っていない");
        Assert.AreEqual(-6.5, layers[1].BottomAltitude);
        Assert.AreEqual(4.5, layers[1].Thickness, 1e-12, "層厚が書き込む前の下端で計算されている");

        vm.UndoCommand.Execute(null);
        var restored = vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers;
        Assert.AreEqual(-5.0, restored[1].BottomAltitude, "Undo で戻らない");
        Assert.AreEqual(3.0, restored[1].Thickness, 1e-12);
    });

    /// <summary>下端Z が 1 つ上の層の下端以上なら確定を取り消し、値も履歴も変えない。</summary>
    [TestMethod]
    public void BottomAboveTheLayerAbove_IsRejectedWithoutHistory() => Sta(() =>
    {
        using var vm = Create();
        var layers = vm.CurrentInputModel!.PileGroupSettlement.SettlementSoilLayers;
        int history = History(vm);

        var box = new TextBox { DataContext = layers[1] };
        box.SetBinding(TextBox.TextProperty, new Binding(nameof(SettlementSoilLayer.BottomAltitude))
        { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
        box.Text = "-1";
        var args = new DataGridCellEditEndingEventArgs(new DataGridTextColumn(), new DataGridRow { Item = layers[1] }, box, DataGridEditAction.Commit);
        vm.CommitSettlementSoilLayerCellEdit(args);

        Assert.IsTrue(args.Cancel, "上の層より上の下端を受け付けている");
        Assert.AreEqual(-5.0, layers[1].BottomAltitude);
        Assert.AreEqual(history, History(vm));
    });

    /// <summary>比べ方の規則。NaN どうしは同じ、文字列の空と null は同じ、読めない値は「変わった」(安全側)。</summary>
    [TestMethod]
    public void IsSameCellValue_Rules()
    {
        Assert.IsTrue(MainWindowViewModel.IsSameCellValue(1.5, 1.5));
        Assert.IsTrue(MainWindowViewModel.IsSameCellValue(double.NaN, double.NaN));
        Assert.IsFalse(MainWindowViewModel.IsSameCellValue(1.5, 1.50001));
        Assert.IsTrue(MainWindowViewModel.IsSameCellValue(null, ""));
        Assert.IsFalse(MainWindowViewModel.IsSameCellValue("a", "b"));
        Assert.IsFalse(MainWindowViewModel.IsSameCellValue(1.5, null), "読めない値を「同じ」としている");
    }
}
