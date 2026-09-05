using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 沈下の結果は <see cref="GroupSettlementResult"/> が持ち、<b>実体は 1 つ</b>。
    /// 現在の入力と解析結果セットのスナップショットは同じインスタンスを指す。
    ///
    /// 以前は結果が入力モデルの中にあり、スナップショットへ<b>写して</b>いた。
    /// 写す経路を 1 つ忘れると
    /// <list type="bullet">
    /// <item>水平解析のあとに入力を編集して沈下を実行すると、結果表示が読む
    ///   スナップショットに沈下の結果が無く、沈下のテーブルが出ない</item>
    /// <item>入力側でケースを切り替えても結果表示が古いケースのまま</item>
    /// </list>
    /// になる。共有にすると写し忘れという失敗自体が無くなる。ここではそれを固定する。
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
                "沈下の記録がスナップショットから見えない（結果テーブルが沈下を出せない）");
            Assert.AreEqual(12.5,
                snapPgs.CaseRecords![0].PileSettlements_mm[input.PileLayoutItems[0].PileNo], 1e-9);
            Assert.AreEqual(12.5, snapPgs.SettlementOf(input.PileLayoutItems[0].PileNo), 1e-9,
                "各杭の沈下量が結果から引けない");

            Assert.AreSame(capturedAnaModel, vm.CurrentResultSet.AnaModel,
                "水平解析の結果まで取り直している（編集後の入力と解析時の結果が 1 組になる）");
            Assert.IsTrue(vm.InputChangedSinceAnalysis,
                "水平解析が陳腐化している記録が消えている");
        }

        /// <summary>
        /// 現在の入力とスナップショットは<b>同じ結果</b>を指すこと。
        ///
        /// 複製にすると、入力側でケースを切り替えたときに結果表示が追随せず、
        /// 「沈下だけ再実行したのに結果テーブルが古い」が起きる。
        /// 結果の実体は 1 つ、というのがこの設計の要。
        /// </summary>
        [TestMethod]
        public void SnapshotSettlement_IsTheSameResultInstance()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            CaptureHorizontal(vm, input);
            vm.MarkInputChangedSinceAnalysis();
            var liveRecord = AddSettlementResult(input, settlement_mm: 20.0);
            vm.CaptureAnalysisResultSet();

            var snapPgs = vm.CurrentResultSet!.InputSnapshot.PileGroupSettlement;
            Assert.AreSame(input.PileGroupSettlement.Result, snapPgs.Result,
                "結果のインスタンスが分かれている (写し忘れが起きる形に戻っている)");
            Assert.AreSame(liveRecord, snapPgs.CaseRecords![0]);
            Assert.AreSame(vm.CurrentResultSet.GroupSettlement, snapPgs.Result);

            // 入力側でケースを切り替えたら結果表示も同じケースを見る
            input.PileGroupSettlement.ActiveCaseIndex = -1;
            Assert.IsNull(snapPgs.ActiveRecord, "ケースの切り替えが結果表示に届いていない");
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
