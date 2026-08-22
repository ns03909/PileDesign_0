using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 沈下解析結果の破棄（③）と MGT エクスポートのモデル組み直し（④）の検証。
    /// </summary>
    [TestClass]
    public class SettlementDiscardAndExportTests
    {
        private static (MainWindowViewModel vm, InputModel input)? Build()
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (inputModel == null) return null;

            var vm = new MainWindowViewModel { CurrentInputModel = inputModel };
            inputModel.AttachViewModel(vm);
            return (vm, inputModel);
        }

        /// <summary>
        /// MGT 出力は現在の入力からモデルを組み直すこと。
        /// CurrentModel（解析時のスナップショット）をそのまま出すと、
        /// 入力を編集したあとに画面と違う形状が出てしまう。
        /// </summary>
        [TestMethod]
        public void MgtExport_BuildsModelFromCurrentInput()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            var modelling = new AnalysisModelling(input);
            var analyzed = new AnaModel(
                input, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            vm.CurrentModel = analyzed;
            vm.IsHorizontalAnalysisDone = true;
            vm.CaptureAnalysisResultSet();   // 以降 CurrentModel は切り離された複製

            var exportModel = vm.BuildExportModelFromCurrentInput();

            Assert.IsNotNull(exportModel);
            Assert.AreNotSame(vm.CurrentModel, exportModel,
                "解析時のスナップショットをそのまま出力しようとしている");
            Assert.AreSame(input, exportModel.InputModel,
                "現在の入力から組まれていない");
            Assert.IsTrue(exportModel.Nodes.Count > 0 && exportModel.Beams.Count > 0,
                "モデルが組めていない");
        }

        /// <summary>沈下解析結果を持たない状態では確認ダイアログを出さずに続行できること。</summary>
        [TestMethod]
        public void NoSettlementResults_DoesNotBlockEditing()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            var pgs = input.PileGroupSettlement;
            Assert.AreEqual(0, pgs?.CaseRecords?.Count ?? 0, "前提: 沈下結果が無い");
            Assert.IsFalse(vm.IsVerticalAnalysisDone);
            Assert.IsFalse(vm.IsGroupPileSettlementAnalysisDone);
            Assert.IsFalse(vm.IsElementSplit);

            // BypassUiPromptsForTesting 相当の仕組みが無いので、
            // 「破棄対象が無ければダイアログを出さない」ことを述語で確認する
            Assert.IsFalse(vm.HasSettlementResultsForTest(),
                "破棄対象が無いのに沈下結果ありと判定されている");
        }

        /// <summary>沈下解析結果があれば破棄対象と判定されること（入力の中に残さない）。</summary>
        [TestMethod]
        public void SettlementResults_AreDetectedAsDiscardTarget()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;

            vm.IsGroupPileSettlementAnalysisDone = true;
            Assert.IsTrue(vm.HasSettlementResultsForTest(),
                "沈下解析済みなのに破棄対象と判定されない");

            vm.ClearSettlementResultsForTest();
            Assert.IsFalse(vm.IsGroupPileSettlementAnalysisDone, "フラグが落ちていない");
            Assert.IsFalse(vm.IsVerticalAnalysisDone, "フラグが落ちていない");
            Assert.IsFalse(vm.HasSettlementResultsForTest(), "破棄されていない");
        }
    }
}
