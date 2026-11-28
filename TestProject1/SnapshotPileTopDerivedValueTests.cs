using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace TestProject1
{
    /// <summary>
    /// 解析結果の控え (JSON で複製する) と読み込んだ入力でも、半剛接合の杭頭モデルが杭体から受け取る値
    /// (キャプテンの杭体 Fc・Ec、キャプリングの Ep・EI。保存しない) が配られていること (2026-11-28)。
    /// <para>検定・グラフは控えを読む。配られていないと、キャプテンの Ma が σmax の Fc にパイルキャップの Fc だけを使い
    /// (杭体の Fc のほうが小さいとき危険側。例題の Fc を 36 にして 14,005.7 に対し解析と同じ値は 12,779.2 kN·m)、
    /// M-θ のグラフは Ep = 40,000 の Ke で描いた。検定の途中で M-θ を作ると配られるので、結果が計算の順に依った。</para>
    /// </summary>
    [TestClass]
    public class SnapshotPileTopDerivedValueTests
    {
        [TestMethod]
        [Timeout(600000)]
        public void TheSnapshotAndTheReopenedFile_UseThePileBodyValuesLikeTheAnalysis()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { Run(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure is AssertInconclusiveException inconclusive) throw inconclusive;
            if (failure != null) Assert.Fail("STA スレッド内で例外:\n" + failure);

            static void Run()
            {
                MessageService.IsUnattended = true;
                var (input, error) = IntegrationTests.BuildExampleInputModel("ExampleCPT2018", "PileExampleCPT2018");
                if (input == null) Assert.Inconclusive($"例題ロード失敗: {error}");
                // パイルキャップの Fc を杭体 (30) より大きくする: σmax の Fc は小さい方 (杭体) になるはず
                foreach (var pb in input!.PileBodies) pb.PileTop.PileCapFc = 36;

                var vm = new MainWindowViewModel { CurrentInputModel = input };
                var hcvm = new HorizontalCalculationViewModel(vm) { BypassUiPromptsForTesting = true, MaxCaseDegreeOfParallelism = 4 };
                hcvm.ExecuteAnalysisCommand.Execute(null);
                var deadline = DateTime.UtcNow.AddMinutes(8);
                while (hcvm.IsAnalysisRunning && DateTime.UtcNow < deadline) Thread.Sleep(100);
                Assert.IsFalse(hcvm.IsAnalysisRunning, "水平解析がタイムアウトしました");
                vm.CurrentModel = hcvm.CurrentModel;
                vm.IsHorizontalAnalysisDone = hcvm.CurrentModel != null;
                vm.CaptureAnalysisResultSet(hcvm.SettingsAtLastRunForTesting ?? hcvm.CaptureRunSettings());
                Assert.IsFalse(ReferenceEquals(vm.ResultInputModel, vm.CurrentInputModel), "(前提) 検定は控えを読む");

                AssertMatchesTheAnalyzedInput(vm, vm.CurrentInputModel!, "解析した直後");

                var reopened = SaveAndReopen(vm);
                Assert.IsFalse(reopened.InputChangedSinceAnalysis,
                    "開き直しただけで「解析のあとに入力を変えた」になっています (配り直しが変更として数えられた)");
                AssertMatchesTheAnalyzedInput(reopened, vm.CurrentInputModel!, "保存して開き直したあと");
            }
        }

        /// <summary>検定の Ma と控えの Ke・Mu が、解析した入力 (解析の入口で配った値) と同じであること。</summary>
        private static void AssertMatchesTheAnalyzedInput(MainWindowViewModel vm, PileDesign.Models.InputData.InputModel analyzed, string when)
        {
            // 検定より先に控えを見る (検定の途中で配られて、配っていなくても合格してしまわないように)
            var snapshot = vm.ResultInputModel!;
            for (int i = 0; i < snapshot.PileBodies.Count; i++)
            {
                var cp = snapshot.PileBodies[i].PileTop.CaptainPile!;
                var live = analyzed.PileBodies[i].PileTop.CaptainPile!;
                Assert.AreEqual(30.0, cp.DesignFc, 1e-9, $"{when}: 杭体 {i + 1} の σmax の Fc が杭体とパイルキャップの小さい方 (30) になっていません");
                Assert.AreEqual(live.Ke, cp.Ke, Math.Abs(live.Ke) * 1e-12, $"{when}: 杭体 {i + 1} の控えの Ke が解析と違います (M-θ のグラフが違う曲線になる)");
                var (_, mSnap) = cp.GetMThetaRelationship(3000e3);
                var (_, mLive) = live.GetMThetaRelationship(3000e3);
                Assert.AreEqual(mLive.Max(), mSnap.Max(), Math.Abs(mLive.Max()) * 1e-12, $"{when}: 杭体 {i + 1} の Mu が解析と違います");
            }

            var result = EvaluationService.BuildEvaluationResult(vm, factored: true);
            var ma = result.Items.Where(x => x.Kind == EvaluationKind.PileHeadJointMoment && !x.IsUnavailable).ToList();
            TestSource.AssertScanned(ma.Count, 5, $"{when}: キャプテンの Ma の検定");
            foreach (var item in ma)
            {
                var live = analyzed.PileBodies[item.PileBodyNo - 1].PileTop.CaptainPile!;
                double expected = live.GetShortTermAllowableMoment(item.AxialForce!.Value * 1000.0) * 1e-6;
                Assert.AreEqual(expected, item.Limit, Math.Abs(expected) * 1e-9,
                    $"{when}: 杭 No.{item.PileNo} ({item.LoadCaseName}) の検定の Ma が解析した入力の値と違います");
            }
        }

        private static MainWindowViewModel SaveAndReopen(MainWindowViewModel vm)
        {
            var files = (FileOperationService)typeof(MainWindowViewModel).GetField("_fileOperationService",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
            var prepared = files.PrepareSave(vm.CurrentInputModel, vm.CurrentModel, vm.VerticalBeamCaseResults?.ToList(),
                vm.CurrentResultSet?.InputSnapshot, vm.CurrentResultSet?.CapturedAt,
                PileDesign.Models.PileFemLinkTable.Build(vm.CurrentResultSet?.InputSnapshot, vm.CurrentResultSet?.AnaModel),
                vm.IsElementSplit, vm.InputChangedSinceAnalysis, analysisRunRecords: vm.CurrentResultSet?.RunRecords);
            Assert.IsNull(prepared.Error, prepared.Error?.ToString());
            string path = Path.Combine(Path.GetTempPath(), $"pd_snapshot_derived_{Guid.NewGuid():N}.pdj");
            files.WritePrepared(path, prepared);
            try
            {
                var data = files.LoadProjectData(path);
                var opened = new MainWindowViewModel();
                opened.ApplyLoadedProjectData(data, null, "読込が完了しました。");
                return opened;
            }
            finally { File.Delete(path); }
        }
    }
}
