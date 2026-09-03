using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 沈下の結果は入力モデルの中に格納されているため、そのままでは
    /// 解析結果セット（解析時の入力ごと切り離した複製）に乗らない。
    ///
    /// 乗らないと次が起きる。
    /// <list type="bullet">
    /// <item>水平解析のあとに入力を編集して沈下を実行すると、結果表示が読む
    ///   スナップショットに沈下の結果が無く、沈下のテーブルが出ない</item>
    /// <item>結果テーブルが現在の入力を読むと、解析後に杭を足した状態で結果の行を組む</item>
    /// </list>
    /// 解析完了時に沈下の結果だけをスナップショットへ写し、破棄は両方から行う。
    /// ここではその往復を固定する。
    /// </summary>
    [TestClass]
    public class SettlementResultSnapshotTests
    {
        private static (MainWindowViewModel vm, InputModel input)? Build()
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (inputModel == null) return null;

            var vm = new MainWindowViewModel { CurrentInputModel = inputModel };
            inputModel.AttachViewModel(vm);
            return (vm, inputModel);
        }

        /// <summary>水平解析を終えた状態（結果セットを 1 つ持っている状態）を作る。</summary>
        private static void CaptureHorizontal(MainWindowViewModel vm, InputModel input)
        {
            var modelling = new AnalysisModelling(input);
            vm.CurrentModel = new AnaModel(
                input, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            vm.IsHorizontalAnalysisDone = true;
            vm.CaptureAnalysisResultSet();
        }

        /// <summary>沈下解析が結果を書き込んだ状態を、現在の入力に対して作る。</summary>
        private static GroupSettlementCaseRecord AddSettlementResult(InputModel input, double settlement_mm)
        {
            // 例題由来のモデルは沈下の入れ物を持たないことがある
            input.PileGroupSettlement ??= new PileGroupSettlement();
            var pgs = input.PileGroupSettlement;
            var pile = input.PileLayoutItems[0];

            var record = new GroupSettlementCaseRecord
            {
                LoadCaseName = "VL",
                LoadingType = "任意矩形",
                IsBeamAware = false,
                IsConverged = true,
                PileSettlements_mm = new Dictionary<int, double> { [pile.PileNo] = settlement_mm },
            };
            pgs.CaseRecords ??= [];
            pgs.CaseRecords.Add(record);
            pgs.ActiveCaseIndex = pgs.CaseRecords.Count - 1;
            pile.GroupPileSettlement = settlement_mm;
            return record;
        }

        // ── 沈下の結果がスナップショットに乗ること ──────────────────

        /// <summary>
        /// 水平解析 → 入力編集 → 沈下解析 の順でも、沈下の結果がスナップショットに乗ること。
        /// 乗らないと結果テーブルが沈下を出せない（この経路がまさに抜けていた）。
        /// </summary>
        [TestMethod]
        public void SettlementAfterInputEdit_LandsInSnapshot()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            CaptureHorizontal(vm, input);
            var capturedAnaModel = vm.CurrentResultSet!.AnaModel;

            // 解析のあとに入力を編集し、そのあとで沈下解析が終わった状況
            vm.MarkInputChangedSinceAnalysis();
            AddSettlementResult(input, settlement_mm: 12.5);
            vm.IsGroupPileSettlementAnalysisDone = true;
            vm.CaptureAnalysisResultSet();

            var snapPgs = vm.CurrentResultSet!.InputSnapshot.PileGroupSettlement;
            Assert.AreEqual(1, snapPgs.CaseRecords?.Count ?? 0,
                "沈下の記録がスナップショットへ写っていない（結果テーブルが沈下を出せない）");
            Assert.AreEqual(12.5,
                snapPgs.CaseRecords![0].PileSettlements_mm[input.PileLayoutItems[0].PileNo], 1e-9);
            Assert.AreEqual(12.5,
                vm.CurrentResultSet.InputSnapshot.PileLayoutItems[0].GroupPileSettlement, 1e-9,
                "各杭の沈下量がスナップショットへ写っていない");

            Assert.AreSame(capturedAnaModel, vm.CurrentResultSet.AnaModel,
                "水平解析の結果まで取り直している（編集後の入力と解析時の結果が 1 組になる）");
            Assert.IsTrue(vm.InputChangedSinceAnalysis,
                "水平解析が陳腐化している記録が消えている");
        }

        /// <summary>
        /// スナップショットへ写した記録は複製であること。
        /// 同じインスタンスを共有すると、以降の入力側の操作が結果表示に漏れる。
        /// </summary>
        [TestMethod]
        public void SnapshotSettlement_IsACopy()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            CaptureHorizontal(vm, input);
            vm.MarkInputChangedSinceAnalysis();
            var liveRecord = AddSettlementResult(input, settlement_mm: 20.0);
            vm.CaptureAnalysisResultSet();

            var snapRecord = vm.CurrentResultSet!.InputSnapshot.PileGroupSettlement.CaseRecords![0];
            Assert.AreNotSame(liveRecord, snapRecord, "記録のインスタンスを共有している");

            // 入力側を書き換えても結果側は動かない
            liveRecord.PileSettlements_mm[input.PileLayoutItems[0].PileNo] = 99.0;
            Assert.AreEqual(20.0,
                snapRecord.PileSettlements_mm[input.PileLayoutItems[0].PileNo], 1e-9,
                "入力側の書き換えが結果側に漏れている");
        }

        // ── 破棄は両方から ──────────────────────────────

        /// <summary>
        /// 沈下結果の破棄はスナップショットからも消すこと。
        /// 現在の入力だけ消すと、「削除されます」と言いながら結果テーブルに残る。
        /// </summary>
        [TestMethod]
        public void ClearSettlementResults_ClearsSnapshotToo()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            CaptureHorizontal(vm, input);
            vm.MarkInputChangedSinceAnalysis();
            AddSettlementResult(input, settlement_mm: 8.0);
            vm.IsGroupPileSettlementAnalysisDone = true;
            vm.CaptureAnalysisResultSet();

            Assert.AreEqual(1,
                vm.CurrentResultSet!.InputSnapshot.PileGroupSettlement.CaseRecords?.Count ?? 0,
                "前提: スナップショットに沈下の記録がある");

            vm.ClearSettlementResultsForTest();

            Assert.AreEqual(0, input.PileGroupSettlement.CaseRecords?.Count ?? 0,
                "現在の入力から消えていない");
            Assert.AreEqual(0,
                vm.CurrentResultSet.InputSnapshot.PileGroupSettlement.CaseRecords?.Count ?? 0,
                "スナップショットから消えていない（結果テーブルに残る）");
            Assert.AreEqual(0.0,
                vm.CurrentResultSet.InputSnapshot.PileLayoutItems[0].GroupPileSettlement, 1e-9,
                "スナップショット側の各杭の沈下量が残っている");
            Assert.IsFalse(vm.IsGroupPileSettlementAnalysisDone);
        }

        /// <summary>
        /// 結果セットが無い（水平解析をしていない）状態でも、沈下の結果は現在の入力に残ること。
        /// このとき結果表示は <c>ResultInputModel</c> のフォールバックで現在の入力を読む。
        /// </summary>
        [TestMethod]
        public void WithoutResultSet_SettlementStaysInCurrentInput()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            AddSettlementResult(input, settlement_mm: 5.0);
            vm.IsGroupPileSettlementAnalysisDone = true;

            Assert.IsNull(vm.CurrentResultSet, "前提: 結果セットは無い");
            Assert.AreSame(input, vm.ResultInputModel,
                "結果セットが無いときは現在の入力を返すこと");
            Assert.AreEqual(1, vm.ResultInputModel.PileGroupSettlement.CaseRecords.Count);
        }
    }
}
