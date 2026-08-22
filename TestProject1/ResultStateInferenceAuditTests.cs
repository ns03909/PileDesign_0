using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 「解析結果の有無から状態を推定している」箇所の監査で見つかった問題の回帰検査。
    ///
    /// 解析結果を入力編集で破棄しなくなったため、
    /// 「結果があるなら入力は解析時のまま」「結果を消したら痕跡も消える」という
    /// 従来の暗黙の前提が成り立たなくなった箇所がある。
    /// </summary>
    [TestClass]
    public class ResultStateInferenceAuditTests
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

        /// <summary>
        /// 解析結果を消す経路では、結果セット (解析時の入力スナップショット) も必ず消えること。
        /// 残すと ResultInputModel が解析時の入力を返し続け、消したはずの結果の痕跡
        /// (ステータスバー・グラフの基準切替) が残る。
        /// </summary>
        [TestMethod]
        public void ClearingResults_AlsoClearsResultSet()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            vm.CaptureAnalysisResultSet();
            Assert.IsTrue(vm.HasAnalysisResultSet, "前提: 結果セットがある");
            Assert.AreNotSame(input, vm.ResultInputModel, "前提: スナップショットは別実体");

            vm.ClearAnalysisResultSetState();

            Assert.IsFalse(vm.HasAnalysisResultSet, "結果セットが残っている");
            Assert.AreSame(input, vm.ResultInputModel,
                "結果を消したのに ResultInputModel が解析時の入力を返し続けている");
            Assert.IsFalse(vm.InputChangedSinceAnalysis, "陳腐化の記録が残っている");
            Assert.AreEqual(string.Empty, vm.ResultSetStatusText,
                "結果を消したのにステータス表示が残っている");
        }

        /// <summary>
        /// 破棄コマンドでも同じこと。
        /// </summary>
        [TestMethod]
        public void DiscardCommand_ClearsResultSet()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;

            vm.CaptureAnalysisResultSet();
            vm.MarkInputChangedSinceAnalysis();
            Assert.IsTrue(vm.InputChangedSinceAnalysis);

            vm.DiscardAnalysisResults();

            Assert.IsFalse(vm.HasAnalysisResultSet);
            Assert.IsFalse(vm.InputChangedSinceAnalysis);
            Assert.IsNull(vm.CurrentModel);
        }

        /// <summary>
        /// 陳腐化の記録は「結果セットがあるときだけ」立つこと。
        /// 結果が無い状態で立つと、以降の判定 (古い結果を転写しない等) が誤作動する。
        /// </summary>
        [TestMethod]
        public void StaleFlag_OnlySetWhileResultSetExists()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;

            vm.MarkInputChangedSinceAnalysis();
            Assert.IsFalse(vm.InputChangedSinceAnalysis,
                "結果セットが無いのに陳腐化が記録されている");

            vm.CaptureAnalysisResultSet();
            vm.MarkInputChangedSinceAnalysis();
            Assert.IsTrue(vm.InputChangedSinceAnalysis);
        }
    }
}
