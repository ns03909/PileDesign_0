using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// グラフウィンドウの「断面の基準」切替が出る条件の検証。
    ///
    /// 断面性状グラフ（N-M 相関 / M-φ など）を選び、かつ解析結果セットを保持しているときだけ出す。
    /// ファイルから復元した場合も同じように出る必要がある
    /// （解析 → 入力変更 → 保存 → 開き直しでラジオボタンが出ない、として報告された）。
    /// </summary>
    [TestClass]
    public class GraphBasisOptionTests
    {
        private static (MainWindowViewModel vm, InputModel input)? Build()
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (inputModel == null) return null;

            var vm = new MainWindowViewModel { CurrentInputModel = inputModel };
            inputModel.AttachViewModel(vm);

            var modelling = new AnalysisModelling(inputModel);
            vm.CurrentModel = new AnaModel(
                inputModel, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            vm.IsHorizontalAnalysisDone = true;

            return (vm, inputModel);
        }

        private static GraphViewModel MakeGraphVm(MainWindowViewModel vm)
        {
            var gvm = new GraphViewModel(vm) { IsHorizontalAnalysisDone = vm.IsHorizontalAnalysisDone };
            gvm.Initialize();
            return gvm;
        }

        [TestMethod]
        public void BasisOption_AppearsForSectionGraphs_AfterAnalysis()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;

            vm.CaptureAnalysisResultSet();
            var gvm = MakeGraphVm(vm);

            gvm.SelectedGraphOption = "慣性力作用点荷重変形関係";
            Assert.IsFalse(gvm.IsGraphBasisOptionVisible, "結果グラフで基準の切替が出ている");

            gvm.SelectedGraphOption = "杭体M-φ";
            Assert.IsTrue(gvm.IsGraphBasisOptionVisible, "M-φ で基準の切替が出ていない");

            gvm.SelectedGraphOption = "NMINT";
            Assert.IsTrue(gvm.IsGraphBasisOptionVisible, "N-M 相関で基準の切替が出ていない");
        }

        /// <summary>
        /// ファイルから復元した結果セットでも切替が出ること。
        /// </summary>
        [TestMethod]
        public void BasisOption_AppearsForSectionGraphs_AfterRestoreFromFile()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            // ファイル読込を模す: 復元経路で結果セットを設定する
            var restored = new AnalysisResultSet
            {
                InputSnapshot = input,
                AnaModel = vm.CurrentModel,
                CapturedAt = DateTime.Now,
                HasHorizontal = true,
            };
            vm.SetRestoredResultSet(restored, changedSinceAnalysis: true);
            Assert.IsTrue(vm.HasAnalysisResultSet, "復元で結果セットが設定されていない");

            var gvm = MakeGraphVm(vm);
            gvm.SelectedGraphOption = "杭体M-φ";
            Assert.IsTrue(gvm.IsGraphBasisOptionVisible,
                "ファイル復元後に基準の切替が出ていない");
        }

        /// <summary>
        /// 解析結果を保持していないときは、切替を出さない（比較対象が無いため）。
        /// </summary>
        [TestMethod]
        public void BasisOption_HiddenWhenNoResultSet()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;

            Assert.IsFalse(vm.HasAnalysisResultSet, "前提が崩れている: 結果セットが既にある");

            var gvm = MakeGraphVm(vm);
            gvm.SelectedGraphOption = "杭体M-φ";
            Assert.IsFalse(gvm.IsGraphBasisOptionVisible, "結果セットが無いのに切替が出ている");
        }

        /// <summary>
        /// 対象外のグラフへ移ったら「解析時の入力」へ戻すこと
        /// （結果グラフを現在の入力で描くと混在表示になるため）。
        /// </summary>
        [TestMethod]
        public void SwitchingToResultGraph_ResetsBasisToAnalyzed()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;

            vm.CaptureAnalysisResultSet();
            var gvm = MakeGraphVm(vm);

            gvm.SelectedGraphOption = "杭体M-φ";
            gvm.UseCurrentInputForCurves = true;
            Assert.IsFalse(gvm.ShowAnalysisOverlay, "現在の入力基準で結果を重ねようとしている");

            gvm.SelectedGraphOption = "慣性力作用点荷重変形関係";
            Assert.IsFalse(gvm.UseCurrentInputForCurves, "結果グラフへ移っても現在の入力基準のまま");
            Assert.IsTrue(gvm.ShowAnalysisOverlay);
        }
    }
}
