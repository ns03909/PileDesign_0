using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-09 の見直しで見つかった、ダイアログ・元に戻す・保存と読込の記録の取りこぼし。
    /// 解析の数値は変えない (記録・表示・入力の戻し方だけ)。
    /// </summary>
    [TestClass, DoNotParallelize]
    public class DialogUndoFixes20261009Tests
    {
        private bool _unattended;
        [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
        [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

        private static object? Field(object obj, string name)
            => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj);

        private static object? Invoke(object obj, string name, params object?[] args)
            => (obj.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"メソッド {name} が見つかりません")).Invoke(obj, args);

        private static void RunOnSta(Action action)
        {
            var ex = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
            Assert.IsFalse(timedOut, "時間切れになりました");
            if (ex != null) throw new AssertFailedException(ex.ToString(), ex);
        }

        /// <summary>
        /// 単杭沈下の結果を持つ状態を作る (杭 1 本・土層-杭セット 1 つに荷重-沈下曲線)。杭は要素分割済みにする。
        /// 解析結果の控えを取り、沈下の結果は最新とする。
        /// </summary>
        private static MainWindowViewModel CreateWithSettlementResult()
        {
            var vm = new MainWindowViewModel();
            var input = vm.CurrentInputModel!;
            input.PileLayoutItems ??= [];
            if (input.PileLayoutItems.Count == 0)
                input.PileLayoutItems.Add(new PileLayoutDataItem { No = 1, PileNo = 1, PileBodyNo = 1, GroundNo = 1 });
            foreach (var pile in input.PileLayoutItems)
            {
                pile.IsFrontPiles = new ObservableCollection<bool> { false, true, false, true };
                pile.IsFrontPilesLevel2 = new ObservableCollection<bool> { false, true, false, true };
                pile.SoilPileAltNo = 1;
            }
            var soilPile = new SoilPile();
            soilPile.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 0, D0s = 0 });
            soilPile.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 1000, D0s = 5 });
            input.ElementDivision ??= new ElementDivision();
            input.ElementDivision.SoilPiles = [soilPile];

            vm.IsElementSplit = true;
            vm.IsVerticalAnalysisDone = true;
            vm.CaptureAnalysisResultSet();
            vm.MarkSettlementResultsCurrent(SettlementAnalyses.All);
            vm.MarkProjectReplaced();
            Assert.IsNotNull(vm.CurrentResultSet, "(前提) 解析結果の控えが取れていません");
            Assert.IsFalse(vm.InputChangedSinceAnalysis, "(前提) 解析した直後なのに「再解析が必要」です");
            return vm;
        }

        /// <summary>自動保存が書く中身 (画面のスレッドで確定させるところまで) を取る。</summary>
        private static byte[] PrepareAutoSavePayload(MainWindowViewModel vm)
        {
            var auto = (AutoSaveService)Field(vm, "_autoSaveService")!;
            var prepared = Invoke(auto, "PrepareState");
            Assert.IsNotNull(prepared, "自動保存の中身が取れません");
            var save = (FileOperationService.PreparedSave)prepared!.GetType().GetProperty("Save")!.GetValue(prepared)!;
            Assert.IsNull(save.Error, save.Error?.ToString());
            return save.Payload!;
        }

        private static PileDesign.Models.ProjectData LoadPayload(MainWindowViewModel vm, byte[] payload)
        {
            string path = Path.Combine(Path.GetTempPath(), $"DialogUndoFixes_{Guid.NewGuid():N}.pdjson");
            File.WriteAllBytes(path, payload);
            try { return ((FileOperationService)Field(vm, "_fileOperationService")!).LoadProjectData(path); }
            finally { File.Delete(path); }
        }

        /// <summary>
        /// 自動保存も、手動保存と同じく解析時の入力の控え・「再解析が必要」・杭要素分割の有無を書くこと。
        /// 以前は書いておらず、単杭沈下のあとに入力を編集して自動保存から復元すると、曲線が編集後の入力に結び付き、
        /// 「再解析が必要」も杭要素分割済みの状態も消えていた。
        /// </summary>
        [TestMethod]
        public void AutoSave_WritesTheSameResultRecordsAsManualSave()
        {
            RunOnSta(() =>
            {
                using var vm = CreateWithSettlementResult();
                vm.CurrentInputModel!.PileLayoutItems[0].AxialForceVL0 += 100.0;
                vm.SaveUndoState("杭の軸力");
                Assert.IsTrue(vm.InputChangedSinceAnalysis, "(前提) 編集したのに「再解析が必要」が立ちません");

                var data = LoadPayload(vm, PrepareAutoSavePayload(vm));
                Assert.IsNotNull(data.ResultInputSnapshot, "自動保存に解析したときの入力の控えが書かれていません");
                Assert.AreEqual(true, data.InputChangedSinceAnalysis, "自動保存に「解析後に編集した」が書かれていません");
                Assert.AreEqual(true, data.IsElementSplit, "自動保存に杭要素分割済みが書かれていません");

                using var restored = new MainWindowViewModel();
                restored.ApplyLoadedProjectData(data, null, "復元しました。");
                Assert.IsTrue(restored.IsElementSplit, "復元すると杭要素分割済みが消えます");
                Assert.IsTrue(restored.InputChangedSinceAnalysis, "復元すると「再解析が必要」が消えます");
                Assert.IsTrue(restored.SettlementResultsAreStale, "復元すると沈下の結果が編集後の入力のものとして扱われます");
                Assert.AreNotSame(restored.CurrentInputModel, restored.ResultInputModel,
                    "復元した結果が、編集後の入力に結び付いています");
            });
        }

        /// <summary>
        /// 沈下の結果を持つファイルを開いただけで「沈下解析の結果は入力変更前のもの」と出ないこと。
        /// 以前は読込の仕上げの SaveUndoState が沈下の印を立て、水平解析の印しか戻していなかった
        /// (変更した項目に読込の処理の名前まで出ていた)。
        /// </summary>
        [TestMethod]
        public void OpeningAFileWithSettlementResults_DoesNotMarkThemStale()
        {
            RunOnSta(() =>
            {
                using var vm = CreateWithSettlementResult();
                var data = LoadPayload(vm, PrepareAutoSavePayload(vm));
                Assert.AreEqual(false, data.InputChangedSinceAnalysis, "(前提) 編集していないのに編集済みとして書かれています");

                using var opened = new MainWindowViewModel();
                opened.ApplyLoadedProjectData(data, null, "読込が完了しました。");
                Assert.IsTrue(opened.IsVerticalAnalysisDone, "(前提) 単杭沈下の結果が読み込まれていません");
                Assert.IsFalse(opened.SettlementResultsAreStale, "開いただけで沈下の結果が入力変更前のもの扱いになります");
                Assert.IsFalse(opened.InputChangedSinceAnalysis, "開いただけで「再解析が必要」が立ちます");
                Assert.IsFalse((opened.ResultSetStatusText ?? "").Contains("ApplyPostLoadProtocol"),
                    "状態表示に内部の処理名が出ています: " + opened.ResultSetStatusText);
                Assert.IsNull(opened.ResultStaleNotice, "開いただけで結果が古いという帯が出ます: " + opened.ResultStaleNotice);
                Assert.IsFalse(opened.HasUnsavedWork, "開いただけで未保存になっています");

                // 開いたあとに編集すれば、沈下の結果は古くなる (印を潰してしまっていない)
                opened.CurrentInputModel!.PileLayoutItems[0].AxialForceVL0 += 100.0;
                opened.SaveUndoState("杭の軸力");
                Assert.IsTrue(opened.SettlementResultsAreStale, "開いたあとの編集で沈下の印が立ちません");
            });
        }

        /// <summary>
        /// 解析したときの入力の控えにも、読込で今の版の形に整える処理をかけること。
        /// 以前は現在の入力にだけかけていて、レベル2 の前後判定を持たないファイルを開くと、
        /// 控え (計算書が読む) の杭は既定の「すべて前方杭」のままだった。
        /// </summary>
        [TestMethod]
        public void TheResultSnapshotIsNormalizedLikeTheCurrentInput()
        {
            RunOnSta(() =>
            {
                using var vm = CreateWithSettlementResult();
                vm.CurrentInputModel!.PileLayoutItems[0].AxialForceVL0 += 100.0;   // 控えを別の実体として書かせる
                vm.SaveUndoState("杭の軸力");
                var node = JsonNode.Parse(PrepareAutoSavePayload(vm))!;
                var snapshot = node["ResultInputSnapshot"];
                Assert.IsNotNull(snapshot, "(前提) 控えが書かれていません");
                int removed = RemoveKey(snapshot!, "IsFrontPilesLevel2");
                Assert.IsTrue(removed > 0, "(前提) 控えからレベル2 の前後判定を外せていません (旧ファイルの再現)");

                var data = LoadPayload(vm, System.Text.Encoding.UTF8.GetBytes(node.ToJsonString()));
                using var opened = new MainWindowViewModel();
                opened.ApplyLoadedProjectData(data, null, "読込が完了しました。");
                var resultInput = opened.ResultInputModel;
                Assert.AreNotSame(opened.CurrentInputModel, resultInput, "(前提) 控えが別の実体になっていません");
                var pile = resultInput.PileLayoutItems[0];
                CollectionAssert.AreEqual(pile.IsFrontPiles.ToArray(), pile.IsFrontPilesLevel2.ToArray(),
                    "控えのレベル2 の前後判定が、レベル1 の値で補われていません (計算書がすべて前方杭で出ます)");
                Assert.IsNotNull(resultInput.FoundationBeamInput, "控えに基礎梁の既定値が入っていません");
            });
        }

        private static int RemoveKey(JsonNode node, string key)
        {
            int count = 0;
            switch (node)
            {
                case JsonObject obj:
                    if (obj.Remove(key)) count++;
                    foreach (var (_, child) in obj.ToList())
                        if (child != null) count += RemoveKey(child, key);
                    break;
                case JsonArray arr:
                    foreach (var child in arr)
                        if (child != null) count += RemoveKey(child, key);
                    break;
            }
            return count;
        }

        /// <summary>
        /// 群杭沈下の入力だけの編集を元に戻す・やり直ししても、水平解析の結果に「再解析が必要」を立てないこと。
        /// 編集したときは沈下にだけ印が立つのに、元に戻す・やり直しのあとの決め直しが群杭沈下の入力も含む署名で
        /// 比べていたので、水平解析まで古い扱いになった。
        /// </summary>
        [TestMethod]
        public void UndoRedoOfASettlementOnlyEdit_DoesNotMarkHorizontalStale()
        {
            MainWindowViewModel? vm;
            try
            {
                vm = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 2, Level2Steps = 4, UseLineSearch = true, Parallelism = 1,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.None,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive("例題ファイルなし");
                return;
            }
            vm.CaptureAnalysisResultSet();
            Assert.IsTrue(vm.HasAnalysisResultSet, "(前提) 解析結果の控えがありません");
            vm.SaveUndoState("解析時");
            vm.RecheckInputAgainstAnalysis();
            Assert.IsFalse(vm.InputChangedSinceAnalysis, "(前提) 解析時の入力なのに「再解析が必要」です");

            vm.CurrentInputModel!.PileGroupSettlement ??= new PileGroupSettlement();
            double plane = vm.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;
            vm.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude = plane + 1.5;
            vm.SaveUndoState(MainWindowViewModel.AnalysisInputScope.Settlement, "荷重面");
            Assert.IsFalse((bool)Field(vm, "_horizontalInputChanged")!, "(前提) 沈下だけの編集で水平解析の印が立っています");

            vm.UndoCommand.Execute(null);
            vm.RedoCommand.Execute(null);
            Assert.AreEqual(plane + 1.5, vm.CurrentInputModel!.PileGroupSettlement.LoadingPlaneAltitude, 1e-9, "(前提) やり直しが効いていません");
            Assert.IsFalse((bool)Field(vm, "_horizontalInputChanged")!,
                "群杭沈下の入力だけの編集をやり直したら、水平解析の結果まで「再解析が必要」になりました");
            Assert.IsFalse(vm.IsHorizontalResultStale, vm.ResultSetStatusText);

            // 水平解析に効く編集は、これまでどおり水平解析の印を立てる (除きすぎていない)
            vm.CurrentInputModel.PileLayoutItems[0].AxialForceVL0 += 100.0;
            vm.SaveUndoState("杭の軸力");
            vm.UndoCommand.Execute(null);
            vm.RedoCommand.Execute(null);
            Assert.IsTrue((bool)Field(vm, "_horizontalInputChanged")!, "水平解析に効く編集のやり直しで印が立ちません");
        }

        /// <summary>
        /// 新規作成は前のプロジェクトの履歴を捨て、作った直後は「保存しますか」を聞かない状態にすること。
        /// </summary>
        [TestMethod]
        public void NewProject_ClearsHistoryAndIsNotUnsaved()
        {
            RunOnSta(() =>
            {
                using var vm = CreateWithSettlementResult();
                vm.CurrentInputModel!.PileLayoutItems[0].AxialForceVL0 += 100.0;
                vm.SaveUndoState("前のプロジェクトの編集");
                vm.MarkProjectReplaced();   // 確認を出さずに新規作成へ進ませる (保存済みの扱い)

                vm.NewInputModelFile().GetAwaiter().GetResult();

                Assert.IsFalse(vm.HasUnsavedWork, "作ったばかりの新規が未保存の扱いです (閉じると「保存しますか」が出ます)");
                Assert.IsFalse(vm.UndoManager.CanUndo, "新規作成のあとに「元に戻す」で前のプロジェクトへ戻れます");
                Assert.IsFalse(vm.InputChangedSinceAnalysis);
                Assert.IsFalse(vm.SettlementResultsAreStale);
            });
        }

        /// <summary>
        /// プロジェクト情報で「Z=0 の標高」を変えてからキャンセル (× も同じ) すると、地盤の Z も元へ戻ること。
        /// 以前は標高の値だけを戻し、その場でずらした地盤の Z はずれたまま残った。
        /// </summary>
        [TestMethod]
        public void ProjectInfoCancel_RestoresTheGroundZ()
        {
            using var main = new MainWindowViewModel();
            var input = main.CurrentInputModel!;
            Assert.IsTrue(input.GroundsInput?.Count > 0, "(前提) 既定の入力に地盤がありません");
            var ground = input.GroundsInput![0];
            double top = ground.GroundTopAltitude;
            double altitude = input.FundamentalInput.ReferenceAltitude;

            var vm = new ProjectInfoViewModel(main);
            vm.ReferenceAltitude = altitude + 2.5;
            vm.ReferenceAltitude = altitude + 4.0;
            Assert.AreEqual(top - 4.0, ground.GroundTopAltitude, 1e-9, "(前提) 標高を変えても地盤がずれていません");

            vm.CancelCommand.Execute(null);
            Assert.AreEqual(altitude, main.CurrentInputModel!.FundamentalInput.ReferenceAltitude, 1e-12, "標高が戻っていません");
            Assert.AreEqual(top, main.CurrentInputModel.GroundsInput[0].GroundTopAltitude, 1e-9,
                "キャンセルしたのに地盤の Z がずれたままです");

            // 元に戻してからキャンセルしても二重に戻さない
            var vm2 = new ProjectInfoViewModel(main);
            vm2.ReferenceAltitude = altitude + 1.0;
            vm2.UndoCommand.Execute(null);
            vm2.CancelCommand.Execute(null);
            Assert.AreEqual(top, main.CurrentInputModel.GroundsInput[0].GroundTopAltitude, 1e-9, "元に戻したあとのキャンセルで二重に戻しています");
        }

        /// <summary>
        /// 基本設定を開いた時点へ戻したら、入力欄も戻り、そのあとの変更が新しい設定に届くこと。
        /// 以前は設定の実体を差し替えるだけで、入力欄と変更の追随が前の実体のままだった
        /// (画面は ON、モデルは OFF のまま OK で閉じられた)。
        /// </summary>
        [TestMethod]
        public void FundamentalRestore_ReloadsTheFieldsAndFollowsTheNewSettings()
        {
            using var main = new MainWindowViewModel();
            var original = main.CurrentInputModel!.FundamentalInput.ShallowCopy();
            try
            {
                bool before = main.CurrentInputModel.FundamentalInput.UseFiberMPhi;
                var vm = new FundamentalViewModel(main);
                vm.UseFiberMPhi = !before;
                Assert.AreEqual(!before, main.CurrentInputModel.FundamentalInput.UseFiberMPhi, "(前提) 変更が設定に届いていません");

                Invoke(vm, "RestorePreviousSettings");
                Assert.AreEqual(before, main.CurrentInputModel.FundamentalInput.UseFiberMPhi, "(前提) 設定が戻っていません");
                Assert.AreEqual(before, vm.UseFiberMPhi, "設定は戻ったのに入力欄が戻っていません");

                vm.UseFiberMPhi = !before;
                Assert.AreEqual(!before, main.CurrentInputModel.FundamentalInput.UseFiberMPhi,
                    "戻したあとの変更が設定に届きません (OK で閉じると画面と違う設定で解析します)");

                string grade = main.CurrentInputModel.FundamentalInput.SeismicGrade == "A" ? "S" : "A";
                main.CurrentInputModel.FundamentalInput.SeismicGrade = grade;
                Assert.AreEqual(grade, vm.SeismicGrade, "戻したあとの設定の変更に入力欄が追随しません");
                Assert.IsFalse(vm.RedoCommand.CanExecute(null), "取り下げた変更をやり直しで戻せてしまいます");
            }
            finally
            {
                main.CurrentInputModel.FundamentalInput = original;
                main.ApplyConcreteModelOptions();
            }
        }

        /// <summary>
        /// 荷重のウィンドウで割増係数を変えて Ctrl+Z すると、係数と荷重組合せがそろって戻ること。
        /// 以前は控えを係数を変えたあと・組合せを作り直す前に取り、「新しい係数・古い組合せ」に戻っていた。
        /// </summary>
        [TestMethod]
        public void LoadCombinationFactorUndo_RestoresAConsistentPair()
        {
            using var main = new MainWindowViewModel();
            var vm = new LoadCaseViewModel(main);
            static string Describe(LoadCaseViewModel v)
                => string.Join(";", v.LoadCombinations.Select(c => $"{c.Alpha1:R},{c.Beta1:R},{c.Beta2:R}"))
                   + "|" + string.Join(";", v.LoadCombinationsPlus.Select(c => $"{c.Alpha1:R},{c.Beta1:R},{c.Beta2:R}"));

            double f0 = vm.LoadCombinationFactor;
            string c0 = Describe(vm);
            double f1 = f0 + 0.5;
            vm.LoadCombinationFactor = f1;
            string c1 = Describe(vm);
            Assert.AreNotEqual(c0, c1, "(前提) 係数を変えても荷重組合せが変わりません (揺らせていない)");

            vm.UndoCommand.Execute(null);
            Assert.AreEqual(f0, vm.LoadCombinationFactor, 1e-12, "係数が戻っていません");
            Assert.AreEqual(c0, Describe(vm), "係数は戻ったのに荷重組合せが戻っていません (どの時点にも無かった組み合わせです)");

            vm.RedoCommand.Execute(null);
            Assert.AreEqual(f1, vm.LoadCombinationFactor, 1e-12, "やり直しで係数が進みません");
            Assert.AreEqual(c1, Describe(vm), "やり直しで係数と荷重組合せがそろいません");
        }
    }
}
