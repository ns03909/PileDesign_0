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
    /// 解析結果セット（入力スナップショット + 結果）の検証。
    ///
    /// 実務では解析結果を横目に見ながら入力を変えていく。それを成立させるには、
    /// 結果が「解析を実行した時点の入力」と一緒に切り離されている必要がある。
    /// 切り離せていないと、入力を編集した瞬間に結果側の断面や配置まで書き換わり、
    /// 「変位は解析時・断面は編集後」という混在表示になる。
    /// </summary>
    [TestClass]
    public class AnalysisResultSetTests
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
        public void Capture_DetachesSnapshotFromLiveInput()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            vm.CaptureAnalysisResultSet();
            Assert.IsTrue(vm.HasAnalysisResultSet, "結果セットが作られていない");
            Assert.IsNotNull(vm.CurrentResultSet!.AnaModel, "結果 (AnaModel) が複製されていない");

            var livePile = input.PileLayoutItems[0];
            var snapPile = vm.ResultInputModel.PileLayoutItems[0];

            Assert.AreNotSame(input, vm.ResultInputModel, "スナップショットが live と同一インスタンス");
            Assert.AreNotSame(livePile, snapPile, "杭がコピーされていない");
            Assert.AreEqual(livePile.No, snapPile.No, "杭の対応が崩れている");

            // 入力を編集してもスナップショットは動かない
            double before = snapPile.AxialForceVL0;
            livePile.AxialForceVL0 = before + 1000.0;

            Assert.AreEqual(before, snapPile.AxialForceVL0, 1e-9,
                "入力を編集したらスナップショット側の軸力まで変わった");
            Assert.AreEqual(before, vm.ResultInputModel.PileLayoutItems[0].AxialForceVL0, 1e-9);
        }

        /// <summary>
        /// スナップショット側の杭が VM 経由で「現在の入力」を見に行かないこと。
        /// PileLayoutDataItem.InputModel は VM の CurrentInputModel を返していたため、
        /// 親を固定しないとコピーした杭が live の断面を拾ってしまう。
        /// </summary>
        [TestMethod]
        public void Snapshot_ResolvesItsOwnInputModel_NotTheLiveOne()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            vm.CaptureAnalysisResultSet();

            var snapPile = vm.ResultInputModel.PileLayoutItems[0];
            Assert.AreSame(vm.ResultInputModel, snapPile.InputModel,
                "スナップショットの杭が自分の親モデルを返していない");

            var livePile = input.PileLayoutItems[0];
            Assert.AreSame(input, livePile.InputModel,
                "live の杭が live の親モデルを返していない");
        }

        /// <summary>
        /// 結果 (AnaModel) がスナップショット側の入力と結び付いていること。
        /// ReferenceHandler.Preserve での往復なので、参照はコピー内で閉じているはず。
        /// </summary>
        [TestMethod]
        public void Capture_ResultReferencesSnapshotNotLiveInput()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            var liveModel = vm.CurrentModel;
            vm.CaptureAnalysisResultSet();

            Assert.AreNotSame(liveModel, vm.CurrentModel,
                "CurrentModel が複製に差し替わっていない");
            Assert.AreSame(vm.CurrentResultSet!.AnaModel, vm.CurrentModel);

            // 複製された結果の節点が、live の節点と別インスタンスであること
            if (liveModel!.Nodes.Count > 0 && vm.CurrentModel!.Nodes.Count > 0)
            {
                Assert.AreNotSame(liveModel.Nodes[0], vm.CurrentModel.Nodes[0],
                    "結果の節点が複製されていない");
                Assert.AreEqual(liveModel.Nodes.Count, vm.CurrentModel.Nodes.Count,
                    "節点数が往復で変わっている");
            }
        }

        [TestMethod]
        public void InputChangedFlag_TracksEditsAfterAnalysis()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;

            vm.CaptureAnalysisResultSet();
            Assert.IsFalse(vm.InputChangedSinceAnalysis, "解析直後に変更済みになっている");

            // 実機では SaveUndoState (全編集の集約点、DataGrid のセル確定も通る) から呼ばれる。
            // テストハーネスの VM では Undo 一式が揃わないので、ここでは記録側だけを検証する。
            vm.MarkInputChangedSinceAnalysis();
            Assert.IsTrue(vm.InputChangedSinceAnalysis, "編集しても陳腐化が記録されていない");
            StringAssert.Contains(vm.ResultSetStatusText, "再解析",
                "変更後の状態表示に再解析の案内が無い");

            // 再解析 = 新しい AnaModel が入ってから取り直す。
            // (Capture は CurrentModel が前回と同じインスタンスかどうかで
            //  「本当に解析し直したのか」を見分ける)
            vm.CurrentModel = NewAnaModel(vm);
            vm.CaptureAnalysisResultSet();
            Assert.IsFalse(vm.InputChangedSinceAnalysis, "再解析でフラグが戻っていない");
        }

        /// <summary>
        /// 入力を編集したあと<b>沈下だけ</b>再実行しても、スナップショットを取り直さないこと。
        ///
        /// 取り直すと「編集後の入力」と「解析時の水平解析結果」が 1 組に組み直され、
        /// 変位は解析時・断面は編集後という混在に戻る。この仕組みが防ぐはずのものそのもの。
        /// 併せて「入力が変更された」の記録も消え、陳腐化した水平解析結果の警告が出なくなっていた。
        ///
        /// 沈下の結果は入力モデルの中にあり、表示も現在の入力を見るので、
        /// 取り直さなくても沈下の結果は出る。
        /// </summary>
        [TestMethod]
        public void SettlementOnlyRun_DoesNotRepairTheSnapshotWithEditedInput()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            vm.CaptureAnalysisResultSet();
            var snapshotAfterAnalysis = vm.ResultInputModel;

            // 解析後に入力を編集
            double before = vm.ResultInputModel.PileLayoutItems[0].AxialForceVL0;
            input.PileLayoutItems[0].AxialForceVL0 = before + 1000.0;
            vm.MarkInputChangedSinceAnalysis();

            // 沈下だけ再実行 (水平解析は走っていないので CurrentModel は同じインスタンスのまま)
            vm.CaptureAnalysisResultSet();

            Assert.AreSame(snapshotAfterAnalysis, vm.ResultInputModel,
                "編集後の入力でスナップショットが組み直されている");
            Assert.AreEqual(before, vm.ResultInputModel.PileLayoutItems[0].AxialForceVL0, 1e-9,
                "スナップショットに編集後の値が入っている");
            Assert.IsTrue(vm.InputChangedSinceAnalysis,
                "陳腐化した水平解析結果の警告が消えている");
        }

        /// <summary>解析し直したときと同じく、別インスタンスの AnaModel を作る。</summary>
        private static AnaModel NewAnaModel(MainWindowViewModel vm)
        {
            var input = vm.CurrentInputModel;
            var modelling = new AnalysisModelling(input);
            return new AnaModel(
                input, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
        }

        [TestMethod]
        public void Discard_ClearsResultSet()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;

            vm.CaptureAnalysisResultSet();
            Assert.IsTrue(vm.HasAnalysisResultSet);

            vm.DiscardAnalysisResults();
            Assert.IsFalse(vm.HasAnalysisResultSet, "破棄されていない");
            Assert.IsNull(vm.CurrentModel);
            Assert.IsFalse(vm.IsHorizontalAnalysisDone);
        }
    }
}
