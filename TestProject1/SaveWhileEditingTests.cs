using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TestProject1
{
    /// <summary>
    /// 保存と「未保存」の印の食い違い。
    ///
    /// - 保存は待ち合わせ (await) のあいだも画面を動かすので、保存を始めたあとに入力を編集したり
    ///   解析が終わったりすることがある。以前は保存の完了で無条件に「保存済み」にしていたので、
    ///   ファイルに入っていない変更があるのに、閉じるときの確認が出なかった。
    /// - 「名前を付けて保存」は書き込みの前に保存先を切り替え、失敗しても戻していなかった。
    ///   次の上書き保存・自動保存が、書けなかった保存先を「いまのファイル」として扱った。
    /// </summary>
    [TestClass]
    public class SaveWhileEditingTests
    {
        [TestMethod]
        public void EditDuringSaveKeepsTheUnsavedMark()
        {
            var vm = new MainWindowViewModel();
            vm.SaveUndoState("保存前の編集");
            Assert.IsTrue(vm.HasUnsavedWork);

            int atSaveStart = vm.UnsavedWorkGeneration;   // 保存を始めたとき
            vm.SaveUndoState("保存中の編集");               // 保存のあいだに編集した
            vm.MarkWorkSavedAsOf(atSaveStart);             // 保存が終わった

            Assert.IsTrue(vm.HasUnsavedWork,
                "保存を始めたあとの編集はファイルに入っていないのに、「保存済み」になりました");
        }

        [TestMethod]
        public void AnalysisFinishedDuringSaveKeepsTheUnsavedMark()
        {
            var vm = new MainWindowViewModel();
            int atSaveStart = vm.UnsavedWorkGeneration;
            vm.IsHorizontalAnalysisDone = true;            // 保存のあいだに解析が終わった (setter から完了が記録される)
            vm.MarkWorkSavedAsOf(atSaveStart);

            Assert.IsTrue(vm.HasUnsavedWork,
                "保存を始めたあとに終わった解析の結果はファイルに入っていないのに、「保存済み」になりました");
        }

        [TestMethod]
        public void NoChangeDuringSaveMarksItSaved()
        {
            var vm = new MainWindowViewModel();
            vm.SaveUndoState("保存前の編集");
            int atSaveStart = vm.UnsavedWorkGeneration;
            vm.MarkWorkSavedAsOf(atSaveStart);

            Assert.IsFalse(vm.HasUnsavedWork, "保存中に何も変わっていないのに、未保存のままです");
        }

        /// <summary>
        /// 保存の途中で別のプロジェクトを開いたら、保存の完了処理がいまのプロジェクトに作用しないこと。
        ///
        /// 以前は上書き保存の完了で未保存の印を消していた (名前を付けて保存では保存先も書き換えた)。
        /// 開いたばかりのプロジェクトの編集が「保存済み」になり、閉じるときの確認なしに消える。
        /// 保存先への書き込みを排他で止めておき、そのあいだにプロジェクトを取り替えてから完了させる。
        /// </summary>
        [TestMethod]
        public void OpeningAnotherProjectDuringSave_LeavesTheNewProjectAlone()
        {
            string dir = Path.Combine(Path.GetTempPath(), "PileDesignSaveTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string pathA = Path.Combine(dir, "A.pdj");
            string pathB = Path.Combine(dir, "B.pdj");
            try
            {
                var error = XamlSmokeTestSupport.RunOnStaThread(() =>
                {
                    var vm = new MainWindowViewModel { CurrentFilePath = pathA };
                    vm.SaveUndoState("A の編集");

                    var gate = FileOperationService.GateFor(pathA);
                    gate.Wait();                                  // A の書き込みをここで止める
                    Task<bool> save;
                    try
                    {
                        save = vm.SaveInputModelFileCoreAsync();
                        Assert.IsFalse(save.IsCompleted, "書き込みを止めたのに保存が終わっています (この検査が成立していない)");

                        // 保存の途中で B を開いて編集した
                        vm.MarkProjectReplaced();
                        vm.CurrentFilePath = pathB;
                        vm.SaveUndoState("B の編集");
                    }
                    finally
                    {
                        gate.Release();
                    }
                    WaitWithDispatcher(save);

                    Assert.IsFalse(save.Result, "保存中に開いたプロジェクトまで保存できたことになっています");
                    Assert.AreEqual(pathB, vm.CurrentFilePath, "保存中に開いたプロジェクトの保存先が書き換わりました");
                    Assert.IsTrue(vm.HasUnsavedWork, "保存中に開いたプロジェクトの編集が「保存済み」になりました");
                    Assert.IsTrue(File.Exists(pathA), "保存を始めたプロジェクトのファイルが書けていません");
                }, out bool timedOut);
                Assert.IsFalse(timedOut, "時間内に終わりませんでした");
                if (error != null) throw error;
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            }
        }

        /// <summary>
        /// 保存を始めたあとに要素の値を書き換えても、ファイルには保存を始めたときの値が入ること。
        ///
        /// 保存の写しが守るのはコレクションの入れ物だけで、要素の実体は画面と共有している。
        /// 以前はバックグラウンドで直列化していたので、保存の途中の編集が、ファイルの一部にだけ
        /// 入り得た (どの時点にも無かった入力が 1 つのファイルになる)。
        /// </summary>
        [TestMethod]
        public void EditingAValueDuringSave_DoesNotReachTheFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "PileDesignSaveTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "model.pdj");
            try
            {
                var service = new FileOperationService(new JsonSerializerOptions
                {
                    ReferenceHandler = ReferenceHandler.Preserve,
                });
                var model = new InputModel { FundamentalInput = new FundamentalInput() };
                model.FundamentalInput.ProjectName = "保存を始めたときの名前";

                var gate = FileOperationService.GateFor(path);
                gate.Wait();                                      // 書き込みをここで止める
                Task save;
                try
                {
                    save = service.SaveProjectDataAsync(path, model, null);
                    model.FundamentalInput.ProjectName = "保存の途中で書き換えた名前";
                }
                finally
                {
                    gate.Release();
                }
                save.GetAwaiter().GetResult();

                var loaded = service.LoadProjectData(path);
                Assert.AreEqual("保存を始めたときの名前", loaded.InputModel.FundamentalInput.ProjectName,
                    "保存の途中の編集がファイルに入りました");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            }
        }

        /// <summary>
        /// 自動保存も、中身を確定させた (画面のスレッドの Tick 前半) あとの編集をファイルに入れないこと。
        ///
        /// 以前は写した器をバックグラウンドへ渡してそこで直列化していたので、メイン画面の
        /// プロパティパネルなどで要素の値を書き換えると、先に書いた要素は編集前・後の要素は
        /// 編集後という自動保存ファイルができ得た。復元の元になるファイルなので、混ざると
        /// どの時点にも無かった入力を「前回の作業」として戻すことになる。
        /// </summary>
        [TestMethod]
        public void EditingAValueDuringAutoSave_DoesNotReachTheFile()
        {
            var service = new FileOperationService(new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
            });
            var auto = new AutoSaveService(service);
            var model = new InputModel { FundamentalInput = new FundamentalInput() };
            model.FundamentalInput.ProjectName = "確定したときの名前";
            string source = Path.Combine(Path.GetTempPath(), $"AutoSaveFix_{Guid.NewGuid():N}.pdj");
            string? written = null;
            try
            {
                auto.LiveStateProvider = () => (model, source, null, null);
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var prepared = typeof(AutoSaveService).GetMethod("PrepareState", flags)!.Invoke(auto, null);
                Assert.IsNotNull(prepared, "保存する状態が取れていません");

                model.FundamentalInput.ProjectName = "書き出しの前に書き換えた名前";   // Tick の前半と後半のあいだの編集

                auto.AutoSaveCompleted += (_, e) => written = e.FilePath;
                typeof(AutoSaveService).GetMethod("PerformAutoSave", flags)!.Invoke(auto, [prepared]);

                Assert.IsNotNull(written, "自動保存が書かれていません");
                Assert.AreEqual("確定したときの名前", service.LoadProjectData(written).InputModel.FundamentalInput.ProjectName,
                    "中身を確定させたあとの編集が自動保存ファイルに入りました");
            }
            finally
            {
                auto.Stop();
                if (written != null)
                    try { File.Delete(written); } catch (IOException) { }
            }
        }

        /// <summary>画面のスレッドのまま、ディスパッチャを回して待つ (await の続きを同じスレッドで動かすため)。</summary>
        private static void WaitWithDispatcher(Task task)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            if (!task.IsCompleted) Dispatcher.PushFrame(frame);
        }

        /// <summary>
        /// 保存の 2 つの経路 (上書き保存・名前を付けて保存) が、待つ前に番号を控え、終わったら照合していること。
        /// 保存ダイアログと待ち合わせのタイミングに頼らずに確かめるため、形で見張る。
        /// </summary>
        [TestMethod]
        public void BothSavePathsCompareTheGenerationAndCommitThePathAfterWriting()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs");
            foreach (string signature in new[]
                     {
                         "internal async Task<bool> SaveInputModelFileAsCoreAsync()",
                         "internal async Task<bool> SaveInputModelFileCoreAsync()",
                     })
            {
                string body = TestSource.MethodBody(src, signature);
                int captured = body.IndexOf("int generationAtSaveStart = UnsavedWorkGeneration;", StringComparison.Ordinal);
                int saved = body.IndexOf("await _fileOperationService.SaveProjectDataAsync(", StringComparison.Ordinal);
                int compared = body.IndexOf("MarkWorkSavedAsOf(generationAtSaveStart)", StringComparison.Ordinal);
                Assert.IsTrue(captured >= 0 && saved > captured && compared > saved,
                    $"{signature}: 保存の前に番号を控え、保存のあとで照合していません");
                int projectChecked = body.IndexOf("ProjectReplacedDuringSave(projectAtSaveStart", StringComparison.Ordinal);
                Assert.IsTrue(projectChecked > saved && projectChecked < compared,
                    $"{signature}: 保存の完了処理が、保存を始めたときのプロジェクトかどうかを確かめる前に、いまの作業に作用しています");
                Assert.IsFalse(body.Contains("MarkProjectReplaced();", StringComparison.Ordinal),
                    $"{signature}: 保存の完了で無条件に「保存済み」にしています");
            }

            string saveAs = TestSource.MethodBody(src, "internal async Task<bool> SaveInputModelFileAsCoreAsync()");
            int write = saveAs.IndexOf("SaveProjectDataAsync(newPath", StringComparison.Ordinal);
            int commit = saveAs.IndexOf("CurrentFilePath = newPath;", StringComparison.Ordinal);
            Assert.IsTrue(write >= 0 && commit > write,
                "名前を付けて保存が、書き込みの前に保存先を切り替えています (失敗しても切り替わったままになる)");
        }
    }
}
