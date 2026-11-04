using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 計算書を作る前の確認 (2026-10-17 のレビュー)。未実施の解析・入力変更前の結果・NG・判定できない項目を
    /// 作る前に 1 つの画面にまとめ、確認先へ移れるようにする。以前は出来上がった計算書を読んで初めて分かった。
    /// </summary>
    [TestClass]
    public class ReportPreflightTests
    {
        private static readonly RequiredAnalysisState[] AllDone =
            [new("水平", true, false), new("単杭沈下", true, false), new("群杭沈下", true, false)];

        [TestMethod]
        public void NothingToCheck_GivesNoItems()
        {
            var items = MainWindowViewModel.BuildReportPreflight(AllDone, null, false, new ReportEvaluationCounts(12, 0, 0, 0, 0));
            Assert.AreEqual(0, items.Count, "確かめることが無いのに確認を出します (画面が毎回出ると読まれなくなる)");
        }

        [TestMethod]
        public void UnrunAnalyses_StaleResults_AndEvaluationProblems_AreListed_WithWhereToCheck()
        {
            var items = MainWindowViewModel.BuildReportPreflight(
                [new("水平", false, false), new("単杭沈下", false, false), new("群杭沈下", true, true)],
                "表示中の沈下の結果は、そのあとに変更された入力の前のものです。群杭沈下解析（基礎梁なし）をやり直してください。",
                inputEditedSinceAnalysis: true,
                new ReportEvaluationCounts(12, 2, 1, 0, 3),
                ["支持力・沈下量"]);

            var horizontal = items.Single(i => i.Text.StartsWith("水平解析が未実施"));
            Assert.AreEqual(ReportPreflightTarget.HorizontalAnalysis, horizontal.Target, "未実施の水平解析から、その画面へ移れません");
            Assert.AreEqual(ReportPreflightSeverity.Warning, horizontal.Severity);
            Assert.AreEqual(ReportPreflightTarget.None, items.Single(i => i.Text.StartsWith("単杭沈下解析が未実施")).Target);
            Assert.IsTrue(items.Any(i => i.Text.Contains("群杭沈下解析（基礎梁なし）をやり直してください")), "古い結果の帯の文が出ていません");
            Assert.IsTrue(items.Any(i => i.Severity == ReportPreflightSeverity.Info && i.Text.Contains("解析を実行した時点の入力で作成します")));

            var ng = items.Single(i => i.Text.StartsWith("NG の杭が 2 本"));
            Assert.AreEqual(ReportPreflightSeverity.Caution, ng.Severity, "NG は計算書としては正しいので、欠けている結果より軽く扱う");
            Assert.AreEqual(ReportPreflightTarget.Dashboard, ng.Target);
            Assert.IsTrue(items.Any(i => i.Text.Contains("収束しなかったケースがある杭が 1 本")));
            Assert.IsTrue(items.Any(i => i.Text.Contains("検定できなかった項目がある杭が 3 本")));
            Assert.IsFalse(items.Any(i => i.Text.Contains("適用範囲の外")), "0 本の項目まで出しています");
            Assert.IsTrue(items.Any(i => i.Text.Contains("「支持力・沈下量」の検定を組み立てられませんでした")));
        }

        /// <summary>
        /// 検定のまとめそのものが取れなかった (例外) ときは、それを確認の項目にする (2026-10-18 のレビュー)。
        /// 以前はログに残すだけで、ほかに項目が無ければ確認の画面も出さず、「確かめることが無い」と同じに作っていた。
        /// </summary>
        [TestMethod]
        public void AnUnavailableEvaluation_IsAnItem_NotSilence()
        {
            var items = MainWindowViewModel.BuildReportPreflight(AllDone, null, false, counts: null, evaluationUnavailable: true);
            Assert.AreEqual(1, items.Count, "検定の状態が分からないのに確認を出しません");
            StringAssert.Contains(items[0].Text, "検定の状態を確認できませんでした");
            Assert.AreEqual(ReportPreflightSeverity.Warning, items[0].Severity);
            Assert.AreEqual(ReportPreflightTarget.Dashboard, items[0].Target);

            string code = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.ReportPreflight.cs");
            string confirm = TestSource.MethodBody(code, "internal ReportPreflightChoice ConfirmReportPreflight(");
            StringAssert.Contains(confirm, "unavailable = true;", "例外のときに印を立てていません");
            StringAssert.Contains(confirm, "counts, failures, unavailable);", "印を確認の項目に渡していません");
        }

        /// <summary>確認の画面で選んだことに従う。画面の無い実行 (テスト・起動の確認) では出さずに作る。</summary>
        [TestMethod]
        public void TheChoice_DecidesWhetherToCreate_AndUnattendedRunsDoNotAsk()
        {
            var built = AnalysisInputScopeTests.BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var vm = built.Value.vm;   // 水平解析だけ済み: 沈下の解析が未実施なので確認がある
            IReadOnlyList<ReportPreflightItem>? asked = null;
            bool unattended = MessageService.IsUnattended;
            try
            {
                MessageService.IsUnattended = false;
                vm.AskReportPreflight = items => { asked = items; return (ReportPreflightChoice.Cancel, ReportPreflightTarget.None); };
                Assert.AreEqual(ReportPreflightChoice.Cancel, vm.ConfirmReportPreflight(), "キャンセルしたのに作ります");
                Assert.IsNotNull(asked, "確かめることがあるのに確認を出していません");
                Assert.IsTrue(asked!.Any(i => i.Text.StartsWith("単杭沈下解析が未実施")));

                vm.AskReportPreflight = _ => (ReportPreflightChoice.Proceed, ReportPreflightTarget.None);
                Assert.AreEqual(ReportPreflightChoice.Proceed, vm.ConfirmReportPreflight(), "「このまま作成」なのに作りません");

                vm.AskReportPreflight = _ => (ReportPreflightChoice.Navigate, ReportPreflightTarget.Dashboard);
                Assert.AreEqual(ReportPreflightChoice.Navigate, vm.ConfirmReportPreflight(), "確認先へ移ると選んだのに作ります");

                MessageService.IsUnattended = true;
                asked = null;
                vm.AskReportPreflight = items => { asked = items; return (ReportPreflightChoice.Cancel, ReportPreflightTarget.None); };
                Assert.AreEqual(ReportPreflightChoice.Proceed, vm.ConfirmReportPreflight(), "画面の無い実行で止まりました");
                Assert.IsNull(asked, "画面の無い実行で確認を出しています");
            }
            finally { MessageService.IsUnattended = unattended; }

            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "var preflight = ConfirmReportPreflight();", "計算書を作る前に確かめていません");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml.cs"),
                "viewModel.AskReportPreflight = ", "確認の画面がメイン画面につながっていません");
        }

        /// <summary>
        /// 出力設定の画面は、出力の結末を見て閉じる (2026-10-18 のレビュー)。以前は結末によらず閉じたので、
        /// 保存先の選択をキャンセルしても、保存に失敗しても、設定をし直す必要があった。
        /// </summary>
        [TestMethod]
        public void TheOptionsWindow_StaysOpen_OnCancelAndFailure()
        {
            Assert.IsTrue(MainWindowViewModel.ShouldCloseReportOptions(ReportOutputResult.Completed));
            Assert.IsFalse(MainWindowViewModel.ShouldCloseReportOptions(ReportOutputResult.Cancelled), "キャンセルで設定画面を閉じています");
            Assert.IsFalse(MainWindowViewModel.ShouldCloseReportOptions(ReportOutputResult.Failed), "失敗で設定画面を閉じています (やり直せない)");
            Assert.IsTrue(MainWindowViewModel.ShouldCloseReportOptions(ReportOutputResult.Navigated), "モーダルのままでは移った先を操作できません");
            Assert.IsTrue(MainWindowViewModel.ShouldCloseReportOptions(ReportOutputResult.Refused), "再解析が要るのに設定画面を残しています");

            string window = TestSource.Read("Graphics_r1", "Views", "DocxOutputWindow.xaml.cs");
            StringAssert.Contains(window, "ShouldCloseReportOptions(vm.OutputWordFile())", "設定画面が出力の結末を見ていません");
            string output = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "public ReportOutputResult OutputWordFile()");
            StringAssert.Contains(output, "if (preflight == ReportPreflightChoice.Navigate) return ReportOutputResult.Navigated;");
            StringAssert.Contains(output, "return ReportOutputResult.Failed;");
        }

        /// <summary>
        /// 作成した計算書を開く処理は、作成の失敗とは別に扱う (2026-10-18 のレビュー)。以前は同じ try の中にあり、
        /// 関連付けが無いなどで開けないと、作成済みなのに「保存失敗」と出た。完了後のステータスバーの案内も
        /// finally で消していた。
        /// </summary>
        [TestMethod]
        public void OpeningTheReport_IsNotPartOfCreatingIt()
        {
            string output = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "public ReportOutputResult OutputWordFile()");
            int saveFailed = output.IndexOf("MessageService.ShowSaveFailed(", System.StringComparison.Ordinal);
            int open = output.IndexOf("System.Diagnostics.Process.Start(", System.StringComparison.Ordinal);
            Assert.IsTrue(saveFailed >= 0 && open >= 0, "作成の失敗の知らせか、開く処理が見つかりません");
            Assert.IsTrue(open > saveFailed, "開く処理が作成の try の中にあります (開けないと「保存失敗」と出る)");
            StringAssert.Contains(output, "DescribeReportOpenFailed(saveFileDialog.FileName)", "開けなかったことを保存先とともに知らせていません");
            StringAssert.Contains(output, "if (!created) StatusMessage = \"準備完了\";", "完了後のステータスバーの案内を消しています");

            string message = MainWindowViewModel.DescribeReportOpenFailed(@"C:\work\計算書.docx");
            StringAssert.Contains(message, "作成済み");
            StringAssert.Contains(message, @"C:\work\計算書.docx", "保存先を示していません");
        }

        /// <summary>確認の画面の XAML (StaticResource のキー誤りはビルドを通る)。</summary>
        [TestMethod]
        public void TheDialog_XamlParses()
        {
            int rows = -1;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var dialog = new PileDesign.Views.ReportPreflightDialog(
                [
                    new ReportPreflightItem("水平解析が未実施です。", ReportPreflightSeverity.Warning, ReportPreflightTarget.HorizontalAnalysis),
                    new ReportPreflightItem("NG の杭が 2 本あります。", ReportPreflightSeverity.Caution, ReportPreflightTarget.Dashboard),
                ]);
                try { rows = dialog.ItemList.Items.Count; }
                finally { dialog.Close(); }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("STA スレッドが時間内に終わりませんでした"); return; }
            if (captured != null) throw new AssertFailedException(captured.ToString());
            Assert.AreEqual(2, rows);
        }

        /// <summary>
        /// 2 本の杭の間の検定 (杭頭変形角) は、両方の杭のまとめに入ること (2026-11-04 外部レビュー)。
        /// 以前は片方の杭にしか数えず、変形角が NG でももう一方の杭の色や杭ごとの一覧に出なかった。
        /// </summary>
        [TestMethod]
        public void ADeformationAngleBetweenTwoPilesCountsForBoth()
        {
            var angle = new PileDesign.Models.Results.EvaluationItem
            {
                Kind = PileDesign.Models.Results.EvaluationKind.PileHeadDeformationAngle,
                PileNo = 1, OtherPileNo = 2, Response = 3, Limit = 2, IsOk = false,
            };
            var summary = PileEvaluationSummary.FromResults(new PileDesign.Models.Results.EvaluationResult([angle]), null,
                new PileDesign.Models.Results.EvaluationResult([]), "A");
            Assert.IsTrue(summary.ByPile.TryGetValue(2, out var other) && other.HasNg,
                "変形角が NG なのに、もう一方の杭 (No.2) のまとめに出ていません");
            Assert.IsTrue(summary.ByPile[1].HasNg);
            Assert.IsTrue(summary.ByPileHead.ContainsKey(2), "もう一方の杭の杭頭の印に出ていません");
            Assert.AreEqual(1, summary.Horizontal!.NgCount, "項目の数は 1 件のまま (二重に数えない)");
        }

        /// <summary>
        /// 杭に属さない項目 (基礎梁の傾斜角など) の NG・未収束・適用範囲外も、作る前の確認に出ること (2026-11-04 外部レビュー)。
        /// 確認は杭の本数で数えるので、杭番号の無い項目は出なかった。
        /// </summary>
        [TestMethod]
        public void NgAndUnconvergedItemsWithoutAPile_AreListed()
        {
            var beam = new PileDesign.Models.Results.EvaluationItem
            {
                Kind = PileDesign.Models.Results.EvaluationKind.FoundationBeamInclination, Response = 3, Limit = 2, IsOk = false,
            };
            var unconverged = beam with { Response = 1, IsOk = true, CaseConvergence = PileDesign.FEM.StepStatus.Unconverged };
            var outOfScope = beam with { Response = 1, IsOk = true, OutOfScopeReason = "適用範囲外" };
            var summary = PileEvaluationSummary.FromResults(new PileDesign.Models.Results.EvaluationResult([]), null,
                new PileDesign.Models.Results.EvaluationResult([beam, unconverged, outOfScope]), "A");

            var items = MainWindowViewModel.BuildReportPreflight(AllDone, null, false, ReportEvaluationCounts.From(summary));
            Assert.IsTrue(items.Any(i => i.Text.Contains("杭に属さない項目") && i.Text.Contains("NG が 1 件")),
                "杭に属さない NG が確認に出ていません: " + string.Join(" / ", items.Select(i => i.Text)));
            Assert.IsTrue(items.Any(i => i.Text.Contains("杭に属さない項目") && i.Text.Contains("収束しなかった") && i.Text.Contains("1 件")));
            Assert.IsTrue(items.Any(i => i.Text.Contains("杭に属さない項目") && i.Text.Contains("適用範囲の外") && i.Text.Contains("1 件")));
        }

        /// <summary>
        /// どの杭の結果か分からない検定不能の項目 (杭番号なし) も、作る前の確認に出ること (2026-11-04 外部レビュー)。
        /// 杭ごとのまとめは杭番号のある項目だけを畳むので、杭の本数だけを数えると 0 に見えて確認に出なかった。
        /// </summary>
        [TestMethod]
        public void UnavailableItemsWithoutAPile_AreListed()
        {
            var ok = new PileDesign.Models.Results.EvaluationItem
            {
                Kind = PileDesign.Models.Results.EvaluationKind.PileSectionMoment, PileNo = 1, Response = 1, Limit = 2, IsOk = true,
            };
            var unlinked = ok with { PileNo = null, Response = double.NaN, UnavailableReason = EvaluationService.UnlinkedPileReason };
            var noPile = ok with { PileNo = null, Response = double.NaN, UnavailableReason = "データが無い" };
            var summary = PileEvaluationSummary.FromResults(
                new PileDesign.Models.Results.EvaluationResult([ok, unlinked, unlinked, noPile]), null,
                new PileDesign.Models.Results.EvaluationResult([]), "A");

            Assert.AreEqual(3, summary.UnavailableWithoutPileCount);
            Assert.AreEqual(2, summary.UnlinkedPileCount);
            var counts = ReportEvaluationCounts.From(summary);
            Assert.AreEqual(0, counts.Unavailable, "(前提) 杭ごとの件数には入らない");

            var items = MainWindowViewModel.BuildReportPreflight(AllDone, null, false, counts);
            var unlinkedItem = items.SingleOrDefault(i => i.Text.Contains("どの杭の結果か分からず検定できなかった項目が 2 件"));
            Assert.IsNotNull(unlinkedItem, "どの杭か分からない検定不能の項目が確認に出ていません: " + string.Join(" / ", items.Select(i => i.Text)));
            Assert.AreEqual(ReportPreflightTarget.HorizontalAnalysis, unlinkedItem!.Target, "水平解析のやり直しへ移れません");
            Assert.IsTrue(items.Any(i => i.Text.Contains("杭に属さない項目") && i.Text.Contains("1 件")));
        }
    }
}
