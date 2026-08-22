using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 読込時に杭要素分割の状態が正しく反映されることの検証。
    ///
    /// メイン画面の杭の色はこの状態だけで決まる（黄＝分割前 / 青＝分割後）。
    /// 以前は「AnaModel に節点があるか」で推定しており、解析結果を保持したまま
    /// 分割だけ取り消した状態が復元できなかった。
    /// </summary>
    [TestClass]
    public class ElementSplitRestoreTests
    {
        private static (MainWindowViewModel vm, ProjectData data)? Build(bool? savedSplit)
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) return null;

            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);

            var modelling = new AnalysisModelling(input);
            var ana = new AnaModel(
                input, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            Assert.IsTrue(ana.Nodes.Count > 0, "前提: 解析モデルに節点がある");

            vm.CurrentModel = ana;
            return (vm, new ProjectData
            {
                FormatVersion = 2,
                InputModel = input,
                AnaModel = ana,
                IsElementSplit = savedSplit,
            });
        }

        [TestMethod]
        public void SavedSplitFalse_IsHonoured_EvenWhenResultsExist()
        {
            var built = Build(savedSplit: false);
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, data) = built.Value;

            vm.RestoreAnalysisState(data);

            Assert.IsFalse(vm.IsElementSplit,
                "保存された「分割なし」が反映されていない（杭が分割後の色で描かれる）");
        }

        [TestMethod]
        public void SavedSplitTrue_IsHonoured()
        {
            var built = Build(savedSplit: true);
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, data) = built.Value;

            vm.RestoreAnalysisState(data);

            Assert.IsTrue(vm.IsElementSplit, "保存された「分割あり」が反映されていない");
        }

        /// <summary>旧ファイル（保存値なし）は従来どおり AnaModel から推定する。</summary>
        [TestMethod]
        public void LegacyFileWithoutValue_FallsBackToInference()
        {
            var built = Build(savedSplit: null);
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, data) = built.Value;

            vm.RestoreAnalysisState(data);

            Assert.IsTrue(vm.IsElementSplit, "旧ファイルの推定（節点があれば分割済み）が効いていない");
        }
    }
}
