using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;

namespace TestProject1
{
    /// <summary>
    /// 一連の操作の回帰 (2026-10-17 のレビュー): セル編集 → 確定 → Undo → 保存・再読込 → 結果の確認。
    /// 一つひとつの段は個別のテストがあるが、つないだ流れで「画面 (今の入力) と解析時の入力の控え・再解析の印・
    /// 工程の帯・古い結果の帯」が食い違う不具合が繰り返し起きたので、流れごと固定する。
    /// 解析の結果の数値は解かない (控えの入力と印が流れの中で保たれるかを見る)。
    /// </summary>
    [TestClass]
    public class EditUndoSaveReloadFlowTests
    {
        private static object? Field(object obj, string name)
            => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj);

        /// <summary>杭配置の表のセルに打って確定する (画面と同じ: 確定の処理のあと、取り消されなければ値を書き込む)。</summary>
        private static void CommitPileCell(MainWindowViewModel vm, PileLayoutDataItem pile, string path, string typed)
        {
            var box = new TextBox { DataContext = pile };
            box.SetBinding(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
            box.Text = typed;
            var args = new DataGridCellEditEndingEventArgs(new DataGridTextColumn(), new DataGridRow { Item = pile }, box, DataGridEditAction.Commit);
            vm.DataGridPileLayout_OnCellEditEndingCommand.Execute(args);
            Assert.IsFalse(args.Cancel, "(前提) 確定が取り消されました");
            box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        }

        /// <summary>
        /// 手動保存と同じ中身を作り (<see cref="MainWindowViewModel.SaveProjectToAsync"/> が渡すのと同じ引数。解析結果も含める)、
        /// 読み込んで新しい画面に開く。保存の非同期の待ちは画面のスレッドで詰まるので、中身を作る段を直接呼ぶ。
        /// </summary>
        private static MainWindowViewModel SaveAndReopen(MainWindowViewModel vm)
        {
            var fileService = (FileOperationService)Field(vm, "_fileOperationService")!;
            var set = vm.CurrentResultSet;
            var save = fileService.PrepareSave(vm.CurrentInputModel, vm.CurrentModel, vm.VerticalBeamCaseResults,
                set?.InputSnapshot, set?.CapturedAt,
                PileDesign.Models.PileFemLinkTable.Build(set?.InputSnapshot, set?.AnaModel),
                vm.IsElementSplit, vm.InputChangedSinceAnalysis,
                analysisRunRecords: set?.RunRecords,
                staleSettlementResults: MainWindowViewModel.SettlementAnalysisKeys(vm.StaleSettlementAnalyses));
            Assert.IsNull(save.Error, save.Error?.ToString());
            string path = Path.Combine(Path.GetTempPath(), $"EditUndoSaveReload_{Guid.NewGuid():N}.pdjson");
            File.WriteAllBytes(path, save.Payload!);
            try
            {
                var data = fileService.LoadProjectData(path);
                var opened = new MainWindowViewModel();
                opened.ApplyLoadedProjectData(data, null, "読込が完了しました。");
                return opened;
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void EditCommitUndoSaveReload_KeepsTheScreenAndTheAnalyzedInputConsistent()
        {
            var built = AnalysisInputScopeTests.BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var vm = built.Value.vm;
            // 解いた結果の代わりに収束した 1 ステップを持たせる (読込は、ステップの結果があれば水平解析済みとみなす)。
            // 結果の数値は見ない。保存・再読込で結果が残るかと、控え・印のそろい方を見る
            vm.CurrentModel!.AnalysisStepResults.Add(new PileDesign.FEM.AnalysisStepResult
            {
                LoadCase = new LoadCase { LoadName = "L1", Level = 1, No = 1 },
                Step = 1,
                Status = PileDesign.FEM.StepStatus.Converged,
            });
            vm.MarkProjectReplaced();
            double x0 = vm.CurrentInputModel!.PileLayoutItems[0].X;
            double edited = x0 + 1.0;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                bool unattended = MessageService.IsUnattended;
                MessageService.IsUnattended = true;
                try
                {
                    // 1. セル編集 → 確定: 今の入力は変わり、控え (解析時の入力) は変わらない。再解析の印・帯・工程の帯がそろって立つ
                    CommitPileCell(vm, vm.CurrentInputModel!.PileLayoutItems[0], nameof(PileLayoutDataItem.X),
                        edited.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Assert.AreEqual(edited, vm.CurrentInputModel!.PileLayoutItems[0].X, 1e-9, "確定した値が入力に入っていません");
                    Assert.AreEqual(x0, vm.ResultInputModel!.PileLayoutItems[0].X, 1e-9, "解析時の入力の控えまで書き換わっています");
                    Assert.IsTrue(vm.InputChangedSinceAnalysis, "確定しても「再解析が必要」が立ちません");
                    Assert.IsTrue(vm.IsHorizontalResultStale, "水平解析の結果が古い扱いになっていません");
                    Assert.AreEqual(WorkflowStepStatus.NeedsRedo, vm.WorkflowSteps[2].Status, "工程の帯の「解析」がやり直しになっていません");
                    StringAssert.Contains(vm.ResultStaleNotice, "水平解析をやり直してください", "古い結果の帯が出ていません");

                    // 2. Undo: 入力は元に戻り、結果は残る (Undo で解析結果を捨てない)
                    vm.UndoCommand.Execute(null);
                    Assert.AreEqual(x0, vm.CurrentInputModel!.PileLayoutItems[0].X, 1e-9, "Undo で値が戻っていません");
                    Assert.IsTrue(vm.IsHorizontalAnalysisDone, "Undo で水平解析の結果が消えました");
                    Assert.IsNotNull(vm.CurrentResultSet, "Undo で解析時の入力の控えが消えました");
                    bool staleAfterUndo = vm.InputChangedSinceAnalysis;

                    // 3. 保存 → 再読込: 値・結果・控え・再解析の印が、保存前と同じ
                    var reopened = SaveAndReopen(vm);
                    Assert.AreEqual(x0, reopened.CurrentInputModel!.PileLayoutItems[0].X, 1e-9, "再読込で値が変わりました");
                    Assert.IsTrue(reopened.IsHorizontalAnalysisDone, "再読込で水平解析の結果が消えました");
                    Assert.AreEqual(1, reopened.CurrentModel?.AnalysisStepResults.Count, "再読込で解析のステップの結果が変わりました");
                    Assert.AreEqual(PileDesign.FEM.StepStatus.Converged, reopened.CurrentModel!.AnalysisStepResults[0].Status);
                    Assert.AreEqual(x0, reopened.ResultInputModel!.PileLayoutItems[0].X, 1e-9, "再読込で解析時の入力の控えが変わりました");
                    Assert.AreEqual(staleAfterUndo, reopened.InputChangedSinceAnalysis, "再読込で「再解析が必要」が保存前と変わりました");
                    Assert.IsFalse(reopened.HasUnsavedWork, "開いただけで未保存になっています");

                    // 4. 再読込のあとに同じ編集をして保存・再読込: 古い結果の印と控えが、ファイルを通っても保たれる
                    CommitPileCell(reopened, reopened.CurrentInputModel!.PileLayoutItems[0], nameof(PileLayoutDataItem.X),
                        edited.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Assert.IsTrue(reopened.InputChangedSinceAnalysis, "再読込のあとの確定で「再解析が必要」が立ちません");
                    var again = SaveAndReopen(reopened);
                    Assert.AreEqual(edited, again.CurrentInputModel!.PileLayoutItems[0].X, 1e-9, "編集した値が保存されていません");
                    Assert.AreEqual(x0, again.ResultInputModel!.PileLayoutItems[0].X, 1e-9,
                        "保存・再読込で、解析時の入力の控えが編集後の値になりました (結果と入力が食い違います)");
                    Assert.IsTrue(again.InputChangedSinceAnalysis, "保存・再読込で「再解析が必要」が消えました");
                    Assert.IsTrue(again.IsHorizontalResultStale, "保存・再読込で、水平解析の結果が古い扱いでなくなりました");
                    Assert.AreEqual(WorkflowStepStatus.NeedsRedo, again.WorkflowSteps[2].Status, "再読込のあと、工程の帯がやり直しを示していません");
                }
                finally { MessageService.IsUnattended = unattended; }
            }, out bool timedOut, timeoutSeconds: 120);
            if (timedOut) { Assert.Inconclusive("STA スレッドが時間内に終わりませんでした"); return; }
            if (captured != null) throw new AssertFailedException(captured.ToString());
        }
    }
}
