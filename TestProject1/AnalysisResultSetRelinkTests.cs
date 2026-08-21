using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 解析結果スナップショットで、杭 → FEM 要素の関連が保たれることの検証。
    ///
    /// PileLayoutDataItem の Beams / PileNodes / SoilNodes / HorizontalSoilSprings /
    /// PileTopRotationalSpring はいずれも [JsonIgnore]（解析ランタイム状態）で、
    /// スナップショットの JSON 往復では失われる。結果表示はここを辿って断面力や M-φ を引くため、
    /// 張り直さないと「解析時の入力」基準のグラフが軒並み空になる
    /// （実機で M-φ グラフが「解析時の入力」で何も表示されない、として報告された）。
    /// </summary>
    [TestClass]
    public class AnalysisResultSetRelinkTests
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

        [TestMethod]
        public void Snapshot_KeepsPileToFemAssociations()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            var livePile = input.PileLayoutItems[0];
            // 前提: 解析モデル構築後は杭に要素が結び付いている
            Assert.IsTrue(livePile.Beams.Count > 0, "前提が崩れている: live の杭に梁要素が無い");
            int liveBeamCount = livePile.Beams.Count;
            int livePileNodeCount = livePile.PileNodes.Count;

            vm.CaptureAnalysisResultSet();
            Assert.IsTrue(vm.HasAnalysisResultSet, "結果セットが作られていない");

            var snapPile = vm.ResultInputModel.PileLayoutItems[0];
            Assert.AreEqual(liveBeamCount, snapPile.Beams.Count,
                "スナップショットの杭に梁要素が張り直されていない（グラフが空になる原因）");
            Assert.AreEqual(livePileNodeCount, snapPile.PileNodes.Count,
                "スナップショットの杭に節点が張り直されていない");

            // 張り直し先はスナップショット側の要素であること（live を指していたら切り離せていない）
            var snapModel = vm.CurrentResultSet!.AnaModel!;
            Assert.IsTrue(snapModel.Beams.Contains(snapPile.Beams[0]),
                "杭の梁がスナップショットの AnaModel の要素ではない");
            Assert.IsFalse(vm.CurrentModel == null, "CurrentModel が失われている");

            foreach (var b in snapPile.Beams)
                Assert.IsFalse(livePile.Beams.Contains(b), "live の梁を指したままになっている");
        }

        [TestMethod]
        public void Snapshot_KeepsPileTopRotationalSpring()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            var livePile = input.PileLayoutItems.FirstOrDefault(p => p.PileTopRotationalSpring != null);
            if (livePile == null) { Assert.Inconclusive("杭頭回転ばねを持つ杭が無い例題"); return; }
            int idx = input.PileLayoutItems.IndexOf(livePile);

            vm.CaptureAnalysisResultSet();

            var snapPile = vm.ResultInputModel.PileLayoutItems[idx];
            Assert.IsNotNull(snapPile.PileTopRotationalSpring,
                "スナップショットの杭に杭頭回転ばねが張り直されていない");
            Assert.AreNotSame(livePile.PileTopRotationalSpring, snapPile.PileTopRotationalSpring,
                "live の杭頭回転ばねを指したままになっている");
            Assert.IsTrue(vm.CurrentResultSet!.AnaModel!.RotationalSprings
                    .Contains(snapPile.PileTopRotationalSpring!),
                "杭頭回転ばねがスナップショットの AnaModel の要素ではない");
        }

        /// <summary>
        /// 保存 → 読込でも杭 → FEM 要素の関連が復元されること。
        /// これらは [JsonIgnore] でファイルに残らず、FEM モデルを組むときにしか設定されないため、
        /// 対応表 (PileFemLinkTable) を別に保存して張り直す必要がある。
        /// </summary>
        [TestMethod]
        public void PileFemLinks_SurviveSaveAndLoad()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            vm.CaptureAnalysisResultSet();
            var snapshot = vm.ResultInputModel;
            var anaModel = vm.CurrentResultSet!.AnaModel!;
            int beamCount = snapshot.PileLayoutItems[0].Beams.Count;
            Assert.IsTrue(beamCount > 0, "前提が崩れている: スナップショットに梁が無い");

            // 保存側で対応表を作る
            var table = PileDesign.Models.PileFemLinkTable.Build(snapshot, anaModel);
            Assert.IsNotNull(table, "対応表が作られていない");

            // 読込直後を模す: 関連が落ちた状態にしてから張り直す
            foreach (var pile in snapshot.PileLayoutItems)
            {
                pile.Beams = [];
                pile.PileNodes = [];
                pile.SoilNodes = [];
                pile.HorizontalSoilSprings = [];
                pile.PileTopRotationalSpring = null;
            }
            Assert.AreEqual(0, snapshot.PileLayoutItems[0].Beams.Count);

            PileDesign.Models.PileFemLinkTable.Apply(table, snapshot, anaModel);

            Assert.AreEqual(beamCount, snapshot.PileLayoutItems[0].Beams.Count,
                "読込後に杭の梁が復元されていない（グラフが空になる原因）");
            Assert.IsTrue(anaModel.Beams.Contains(snapshot.PileLayoutItems[0].Beams[0]),
                "復元された梁がモデルの要素ではない");
        }

        [TestMethod]
        public void Snapshot_KeepsHorizontalSoilSprings()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            var livePile = input.PileLayoutItems.FirstOrDefault(p => p.HorizontalSoilSprings.Count > 0);
            if (livePile == null) { Assert.Inconclusive("水平地盤ばねを持つ杭が無い例題"); return; }
            int idx = input.PileLayoutItems.IndexOf(livePile);
            int count = livePile.HorizontalSoilSprings.Count;

            vm.CaptureAnalysisResultSet();

            var snapPile = vm.ResultInputModel.PileLayoutItems[idx];
            Assert.AreEqual(count, snapPile.HorizontalSoilSprings.Count,
                "スナップショットの杭に水平地盤ばねが張り直されていない");
            Assert.IsTrue(vm.CurrentResultSet!.AnaModel!.HorizontalSoilSprings
                    .Contains(snapPile.HorizontalSoilSprings[0]),
                "水平地盤ばねがスナップショットの AnaModel の要素ではない");
        }
    }
}
