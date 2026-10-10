using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestProject1
{
    /// <summary>
    /// 外部レビュー (2026-10-10) の指摘への対応の再発防止。指摘に書かれた操作の順を再現して確かめる。
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class ReviewFindings20261010Tests
    {
        // ── 単杭沈下の画面 (指摘 1〜3) ─────────────────

        /// <summary>杭体の違う土層-杭セットが 2 つ以上ある例題 (例題 3.4)。</summary>
        private static InputModel? ModelWithTwoBodies()
        {
            var (model, _) = IntegrationTests.BuildExampleInputModel("Example3_4", "PileExample3_4");
            if (model == null) return null;
            var sets = model.ElementDivision.SoilPiles;
            if (sets.Count < 2 || sets.Select(s => s.PileBodyNo).Distinct().Count() < 2) return null;
            if (sets.Any(s => s.PileToeNValue == 0)) return null;   // 警告ダイアログが出る
            return model;
        }

        private static void OnSta(Action<InputModel> check)
        {
            var model = ModelWithTwoBodies();
            if (model == null) { Assert.Inconclusive("杭体の違う 2 セットの例題を組めません"); return; }
            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
            try
            {
                var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
                {
                    // 継続を同じスレッドで同期に走らせる (完了の順をテストが決める)
                    SynchronizationContext.SetSynchronizationContext(null);
                    check(model);
                }, out bool timedOut);
                if (timedOut) { Assert.Inconclusive("STA スレッドで時間内に終わりませんでした"); return; }
                if (captured is AssertFailedException || captured is AssertInconclusiveException) throw captured;
                if (captured != null) Assert.Fail(captured.ToString());
            }
            finally { MessageService.IsUnattended = unattended; }
        }

        private static VerticalLoadTransferMethod Solve(InputModel model, SoilPile sp) => new(model, sp);

        /// <summary>
        /// 1. 解析は開始時のセットへ結果を書く。A の解析中に B へ切り替えても、A の結果は A へ (B へは書かない)。
        /// 同じセットで後から始めた解析があれば古い結果を捨てる (完了の順が逆転しても新しい結果が残る)。閉じたら反映しない。
        /// </summary>
        [TestMethod]
        public void SettlementAnalysis_WritesToTheSetItStartedOn_AndDropsStaleResults()
        {
            OnSta(model =>
            {
                var vm = new SettlementViewModel(new MainWindowViewModel { CurrentInputModel = model });
                // 選択の切り替えから走る解析も、同期の代わりで解く (画面スレッドの外から状態を触らない)
                vm.AnalysisRunnerForTesting = (m, sp) => Task.FromResult(Solve(m, sp));
                var a = vm.SoilPiles[0];
                var b = vm.SoilPiles.First(s => s.PileBodyNo != a.PileBodyNo);
                var resultA = Solve(model, a);

                // A の解析を始め、終わる前に B へ切り替える
                var pending = new TaskCompletionSource<VerticalLoadTransferMethod>();
                var run = vm.ExecuteAnalysisCore((_, _) => pending.Task);
                vm.SoilPileNo = vm.SoilPiles.IndexOf(b) + 1;
                vm.ComboBoxSoilPileNo_SelectionChanged(1);
                Assert.AreSame(b, vm.SoilPile, "前提: B へ切り替えた");
                var bBefore = b.LoadDisplacements;
                pending.SetResult(resultA);
                run.Wait();
                Assert.AreSame(resultA.LoadDisplacements, a.LoadDisplacements, "A の結果が A に書かれていません");
                Assert.AreNotSame(resultA.LoadDisplacements, b.LoadDisplacements, "A の結果を B へ書いています");

                // 同じセット (B) で 2 回続けて解き、完了の順を逆にする: 後から始めた方が残る
                var first = new TaskCompletionSource<VerticalLoadTransferMethod>();
                var second = new TaskCompletionSource<VerticalLoadTransferMethod>();
                var r1 = Solve(model, b);
                var r2 = Solve(model, b);
                var t1 = vm.ExecuteAnalysisCore((_, _) => first.Task);
                var t2 = vm.ExecuteAnalysisCore((_, _) => second.Task);
                second.SetResult(r2);
                first.SetResult(r1);
                Task.WaitAll(t1, t2);
                Assert.AreSame(r2.LoadDisplacements, b.LoadDisplacements, "古い解析の結果が新しい結果を上書きしました");

                // 解析中に閉じたら反映しない
                var late = new TaskCompletionSource<VerticalLoadTransferMethod>();
                var r3 = Solve(model, b);
                var t3 = vm.ExecuteAnalysisCore((_, _) => late.Task);
                vm.CancelCommand.Execute(null);
                late.SetResult(r3);
                t3.Wait();
                Assert.AreNotSame(r3.LoadDisplacements, b.LoadDisplacements, "閉じたあとに解析の結果を反映しました");
            });
        }

        /// <summary>
        /// 2. 編集・解析・キャンセルで、各杭の単杭沈下の結果と元の土層-杭セットの結果が、開いたときのまま。
        /// 以前は元のセットへも直接書き、キャンセルしても変更後の入力で解いた結果が残った。
        /// </summary>
        [TestMethod]
        public void SettlementCancel_LeavesResultsAsTheyWere()
        {
            OnSta(model =>
            {
                var origSets = model.ElementDivision.SoilPiles.ToList();
                var origCurves = origSets.Select(s => s.LoadDisplacements).ToList();
                var origNodes = origSets.Select(s => s.NodeDisplacements).ToList();
                string Settlements() => string.Join("|", model.PileLayoutItems.Select(p =>
                    $"{p.SinglePileSettlementVL:R};{p.SinglePileSettlementVLBeyondCurve};"
                    + string.Join(",", p.SinglePileSettlementLevel1s ?? []) + ";" + string.Join(",", p.SinglePileSettlementLevel2s ?? [])
                    + ";" + string.Join(",", p.SinglePileSettlementLevel1sBeyondCurve ?? []) + ";" + string.Join(",", p.SinglePileSettlementLevel2sBeyondCurve ?? [])));
                string before = Settlements();
                bool doneBefore = false;

                var mainVm = new MainWindowViewModel { CurrentInputModel = model };
                doneBefore = mainVm.IsVerticalAnalysisDone;
                var vm = new SettlementViewModel(mainVm);
                vm.AnalysisRunnerForTesting = (m, sp) => Task.FromResult(Solve(m, sp));
                vm.AnalyzeAllSoilPiles();                          // 開いたときの一括解析
                vm.PileBody.SettleAlpha += 0.3;                    // 編集して
                var run = vm.ExecuteAnalysisCore((m, sp) => Task.FromResult(Solve(m, sp)));   // 解き直す
                run.Wait();
                Assert.AreNotEqual(before, Settlements(), "前提: 解析で単杭沈下量が変わった");

                vm.CancelCommand.Execute(null);

                Assert.AreEqual(before, Settlements(), "キャンセルしても、各杭の単杭沈下の結果が変更後のまま残っています");
                CollectionAssert.AreEqual(origSets, model.ElementDivision.SoilPiles.ToList(), "元の土層-杭セットが差し替わっています");
                for (int i = 0; i < origSets.Count; i++)
                {
                    Assert.AreSame(origCurves[i], origSets[i].LoadDisplacements, $"セット {i + 1} の荷重-沈下曲線が変わっています");
                    Assert.AreSame(origNodes[i], origSets[i].NodeDisplacements, $"セット {i + 1} の節点の結果が変わっています");
                }
                Assert.AreEqual(doneBefore, mainVm.IsVerticalAnalysisDone, "キャンセルで解析済みの状態が変わっています");
            });
        }

        /// <summary>
        /// 3. コンボボックスで杭体の違うセットへ切り替えると、杭体 (α・n・杭体記号などの入力欄の対象) も切り替わり、
        /// 編集は選択先の杭体だけに入る。以前はセットだけが切り替わり、前の杭体を編集していた。
        /// </summary>
        [TestMethod]
        public void SettlementComboSwitch_SwitchesThePileBodyToo()
        {
            OnSta(model =>
            {
                var vm = new SettlementViewModel(new MainWindowViewModel { CurrentInputModel = model });
                vm.AnalysisRunnerForTesting = (m, sp) => Task.FromResult(Solve(m, sp));
                var a = vm.SoilPile;
                var b = vm.SoilPiles.First(s => s.PileBodyNo != a.PileBodyNo);
                var bodyA = model.PileBodyAt(a.PileBodyNo)!;
                var bodyB = model.PileBodyAt(b.PileBodyNo)!;
                double alphaA = bodyA.SettleAlpha;

                vm.SoilPileNo = vm.SoilPiles.IndexOf(b) + 1;
                vm.ComboBoxSoilPileNo_SelectionChanged(1);

                Assert.AreSame(b, vm.SoilPile);
                Assert.AreSame(bodyB, vm.PileBody, "コンボボックスで切り替えても、杭体が前のセットのままです");
                vm.PileBody.SettleAlpha = bodyB.SettleAlpha + 0.2;
                Assert.AreEqual(alphaA, bodyA.SettleAlpha, 1e-12, "選択していない杭体を編集しています");
                vm.CancelCommand.Execute(null);
            });
        }

        // ── 入力の複製 (指摘 4・5) ─────────────────

        /// <summary>4. DeepCopy は元を書き換えない。並行に走らせても、元の土層-杭セットの参照と件数が変わらない。</summary>
        [TestMethod]
        public void DeepCopy_DoesNotTouchTheSourceWhileCopying()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }
            var sets = model.ElementDivision.SoilPiles;
            int count = sets.Count;
            Assert.IsTrue(count > 0, "前提: 土層-杭セットがある");

            using var stop = new CancellationTokenSource();
            var copier = Task.Run(() => { while (!stop.IsCancellationRequested) model.CopyForHistory(); });
            var seen = new List<string>();
            var until = DateTime.UtcNow.AddSeconds(2);
            int reads = 0;
            while (DateTime.UtcNow < until)
            {
                var now = model.ElementDivision.SoilPiles;
                if (!ReferenceEquals(now, sets) || now.Count != count) seen.Add($"参照 {(ReferenceEquals(now, sets) ? "同じ" : "違う")}・件数 {now.Count}");
                reads++;
            }
            stop.Cancel();
            copier.Wait();
            Assert.IsTrue(reads > 100, "前提: 読み取りを繰り返した");
            Assert.AreEqual(0, seen.Count, "複製の最中に、元の土層-杭セットが空・別の一覧に見えました: " + string.Join(", ", seen.Distinct().Take(5)));
        }

        private sealed class Unserializable
        {
            public double Value => throw new InvalidOperationException("複製できない値");
        }

        /// <summary>
        /// 5. 複製に失敗したら、欠けた写しを成功として返さない (例外で知らせる)。InputModel.CopyForHistory はこの形だけを使い、
        /// 履歴は写しを作れなければ記録しない (欠けた写しを積まない)。
        /// </summary>
        [TestMethod]
        public void DeepCopy_FailureIsReportedNotDropped()
        {
            Assert.ThrowsException<InvalidOperationException>(() =>
                PileDesign.Common.DeepCopyUtil.CloneCollectionViaJsonOrThrow(new[] { new Unserializable() }));
            Assert.AreEqual(0, PileDesign.Common.DeepCopyUtil.CloneCollectionViaJson(new[] { new Unserializable() }).Count,
                "前提: 欠けてよい用途の複製は黙って除く");

            string src = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Models", "InputData", "InputModel.cs"), "public InputModel CopyForHistory()");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(src, @"DeepCopyUtil\.CloneJson\(|CloneCollectionViaJson\("),
                "InputModel.CopyForHistory が、失敗を黙って null・欠けで返す複製を使っています");
            Assert.IsFalse(src.Contains("ElementDivision.SetSoilPilesSilently([])", StringComparison.Ordinal), "元の土層-杭セットを一時的に空にしています");

            string undo = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.Undo.cs"), "public void SaveUndoState(AnalysisInputScope scope,");
            StringAssert.Contains(undo, "TryCopyForHistory(", "履歴の記録が、写しの失敗を受けていません");
        }

        // ── ソルバーの診断情報 (指摘 6) ─────────────────

        /// <summary>6. 求解の診断情報 (解法・時間・再利用回数) はモデルごと。2 つのモデルの値が混ざらない。</summary>
        [TestMethod]
        public void SolverDiagnostics_ArePerModel()
        {
            var K = MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.DenseOfArray(new double[,] { { 4, 1 }, { 1, 3 } });
            var bvec = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray([1.0, 2.0]);
            var cacheA = new CholeskySolverCache();
            var cacheB = new CholeskySolverCache();

            CsparseLinearSolver.Solve(K, bvec, isSpd: true, cache: cacheA);
            CsparseLinearSolver.Solve(K, bvec, isSpd: true, cache: cacheA);   // 2 回目は因子を再利用
            Assert.AreEqual(CsparseLinearSolver.SolverKind.Cholesky, cacheA.Diagnostics.LastSolver);
            Assert.AreEqual(1, cacheA.Diagnostics.CholeskyReuseCount, "因子の再利用がモデルの診断に数えられていません");
            Assert.AreEqual(CsparseLinearSolver.SolverKind.None, cacheB.Diagnostics.LastSolver, "解いていないモデルに別のモデルの解法が入っています");
            Assert.AreEqual(0, cacheB.Diagnostics.CholeskyReuseCount);

            cacheB.Diagnostics.Reset();
            Assert.AreEqual(1, cacheA.Diagnostics.CholeskyReuseCount, "別のモデルのリセットで値が消えました");

            string run = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(run, @"CsparseLinearSolver\.(ResetInternalTimers|CscBuildTicks|FactorizeTicks|SolveBackSubTicks|CholeskyReuseCount|LastSuccessfulSolver)"),
                "水平解析のログが、ケースをまたいで共有する診断情報を読んでいます");
        }

        // ── CI (指摘 7) ─────────────────

        /// <summary>7. CI もローカルと同じスクリプトを通し、実行件数の下限・意図しないスキップ・画面スレッドの例外まで見る。</summary>
        [TestMethod]
        public void Ci_UsesTheSameChecksAsLocalRuns()
        {
            string ci = TestSource.Read(".github", "workflows", "build-test.yml");
            StringAssert.Contains(ci, "tools/run-tests.ps1", "CI がローカルの検査スクリプトを通っていません");
            StringAssert.Contains(ci, "-EnforceMinimum", "CI が実行件数の下限を見ていません");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(ci, @"^\s*dotnet test ", System.Text.RegularExpressions.RegexOptions.Multiline),
                "CI が dotnet test を直接呼んでいます (件数の検査を通らない)");

            string script = TestSource.Read("tools", "run-tests.ps1");
            StringAssert.Contains(script, "[int]$MaxSkipped");
            StringAssert.Contains(script, "dispatcher-exceptions.log");
        }

        /// <summary>
        /// 解析結果の表示内容の候補は、並べ替えと同じロックを通して足し引きする (WPF にもこのロックを渡している)。
        /// 以前はセッターがロックを通らず、別のスレッドの並べ替えとすれ違って添字外れの例外が出た (2026-10-10 の全体テストで発生)。
        /// </summary>
        [TestMethod]
        public void AnalysisContentOptions_AreChangedUnderTheLock()
        {
            int scanned = 0;
            foreach (var file in System.IO.Directory.GetFiles(TestSource.Dir("Graphics_r1", "ViewModels"), "*.cs"))
            {
                string src = System.IO.File.ReadAllText(file);
                scanned++;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, @"AnalysisResultContentOption\.(Add|Remove)\("))
                {
                    // 許すのは、ロックを通す 2 つの関数の中だけ
                    int at = m.Index;
                    string before = src.Substring(Math.Max(0, at - 400), Math.Min(400, at));
                    Assert.IsTrue(before.Contains("lock (_analysisResultContentOptionLock)", StringComparison.Ordinal),
                        $"{System.IO.Path.GetFileName(file)}: 解析結果の表示内容の候補をロックの外で足し引きしています");
                }
            }
            TestSource.AssertScanned(scanned, 10, "ViewModels のファイル");
        }
    }
}
