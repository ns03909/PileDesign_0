using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Reflection;

namespace TestProject1;

/// <summary>
/// 検定を始めるときに、解析結果 (<see cref="MainWindowViewModel.CurrentModel"/>) と、それを解いたときの入力
/// (<see cref="MainWindowViewModel.ResultInputModel"/>) が 1 組かを確かめること。
///
/// 2 つは別々に持っていて、組が崩れても検定の計算は進み、別の条件の入力で限界値を引いた結果になる。
/// 組になっていなければ検定せず、理由を示す (空の結果 = NG なし、として返さない)。
/// </summary>
[TestClass]
[DoNotParallelize]
public class EvaluationPairingTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

    private static void SetField(MainWindowViewModel vm, string name, object? value)
        => typeof(MainWindowViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);

    private static MainWindowViewModel Create(AnaModel current, AnalysisResultSet? set, bool horizontalChanged)
    {
        var vm = new MainWindowViewModel { CurrentInputModel = new InputModel() };
        vm.CurrentModel = current;
        SetField(vm, "_currentResultSet", set);
        SetField(vm, "_horizontalInputChanged", horizontalChanged);
        return vm;
    }

    [TestMethod]
    public void APairedResult_HasNoProblem()
    {
        var model = new AnaModel();
        var vm = Create(model, new AnalysisResultSet { InputSnapshot = new InputModel(), AnaModel = model }, horizontalChanged: true);
        Assert.IsNull(vm.DescribeEvaluationPairingProblem(), "組になっている (編集後でも控えがある) のに止めている");
    }

    /// <summary><b>本題 1。</b> 控えがあるのに、表示中の解析モデルが控えのものと違えば止める。</summary>
    [TestMethod]
    public void AModelFromAnotherRun_IsRejected()
    {
        var vm = Create(new AnaModel(), new AnalysisResultSet { InputSnapshot = new InputModel(), AnaModel = new AnaModel() }, false);

        StringAssert.Contains(vm.DescribeEvaluationPairingProblem(), "別の解析");
        var ex = Assert.ThrowsException<InvalidOperationException>(() => EvaluationService.BuildEvaluationResult(vm, factored: true),
            "組になっていないのに空の結果 (NG なし) を返している");
        StringAssert.Contains(ex.Message, "別の解析");
        StringAssert.Contains(EvaluationService.BuildEvaluationText(vm, factored: true, displayFilter: 2), "検定できません");
    }

    /// <summary><b>本題 2。</b> 控えが無いのに解析のあとにモデル側の入力が編集されていれば止める (編集中の入力で代用するため)。</summary>
    [TestMethod]
    public void NoSnapshotAndEditedInput_IsRejected()
    {
        StringAssert.Contains(Create(new AnaModel(), null, horizontalChanged: true).DescribeEvaluationPairingProblem(), "控えが無く");
        Assert.IsNull(Create(new AnaModel(), null, horizontalChanged: false).DescribeEvaluationPairingProblem(),
            "控えの無い古いファイルでも、編集していなければ検定できる (従来どおり)");
    }
}
