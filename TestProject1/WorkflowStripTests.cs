using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// メイン画面の工程の帯 (入力 → 杭要素分割 → 解析 → 検定 → 計算書)。
    /// 初めて使うときに次に何をすればよいかが分からなかったので、工程と状態を並べ、押すとその画面を開く。
    /// 状態の決め方と、帯が押されたときに開く画面・状態が変わったときの通知を固定する。
    /// </summary>
    [TestClass]
    public class WorkflowStripTests
    {
        private static IReadOnlyList<WorkflowStepStatus> Statuses(int piles, bool split, bool analyzed, bool stale) =>
            MainWindowViewModel.BuildWorkflowSteps(piles, split, analyzed, stale, null, null, null, null, null)
                .Select(s => s.Status).ToList();

        [TestMethod]
        public void Steps_AreInTheOrderOfTheWork()
        {
            var names = MainWindowViewModel.BuildWorkflowSteps(0, false, false, false, null, null, null, null, null).Select(s => s.Name);
            CollectionAssert.AreEqual(new[] { "入力", "杭要素分割", "解析", "検定", "計算書" }, names.ToArray());
        }

        /// <summary>次にすることは、まだ済んでいない最初の工程。後ろの工程は「まだ」で押せても目立たせない。</summary>
        [TestMethod]
        public void TheNextStep_IsTheFirstOneNotDone()
        {
            const WorkflowStepStatus D = WorkflowStepStatus.Done, N = WorkflowStepStatus.Next, Y = WorkflowStepStatus.NotYet, R = WorkflowStepStatus.NeedsRedo;
            CollectionAssert.AreEqual(new[] { N, Y, Y, Y, Y }, Statuses(0, false, false, false).ToArray(), "杭が無いうちは入力が次");
            CollectionAssert.AreEqual(new[] { D, N, Y, Y, Y }, Statuses(4, false, false, false).ToArray(), "入力のあとは杭要素分割");
            CollectionAssert.AreEqual(new[] { D, D, N, Y, Y }, Statuses(4, true, false, false).ToArray(), "分割のあとは解析");
            CollectionAssert.AreEqual(new[] { D, D, D, D, N }, Statuses(4, true, true, false).ToArray(), "解析のあとは計算書");
            CollectionAssert.AreEqual(new[] { D, D, R, R, R }, Statuses(4, true, true, true).ToArray(),
                "解析のあとに入力を変えたら、解析とその先はやり直しが要る");
        }

        [TestMethod]
        public void StateTexts_SayWhatIsDone()
        {
            var steps = MainWindowViewModel.BuildWorkflowSteps(4, true, true, true, null, null, null, null, null);
            Assert.AreEqual("杭 4 本", steps[0].StateText);
            Assert.AreEqual("やり直しが必要", steps[2].StateText);
            Assert.AreEqual("入力変更前の結果", steps[3].StateText);
            StringAssert.Contains(steps[2].ToolTip, "入力が変更されています");
            Assert.AreEqual("!", steps[2].Mark);

            // 帯は狭いので短く書く (「まだ」→「未」、「解析のあと」→「解析後」)
            var fresh = MainWindowViewModel.BuildWorkflowSteps(4, false, false, false, null, null, null, null, null);
            Assert.AreEqual("未", fresh[1].StateText);
            Assert.AreEqual("未", fresh[2].StateText);
            Assert.AreEqual("解析後", fresh[3].StateText);
            Assert.AreEqual("解析後", fresh[4].StateText);
        }

        /// <summary>
        /// 「入力」は、解析を止める入力 (「入力の診断」と同じ検査) が残っていれば済みにせず「!」で件数を示す。
        /// 注意だけなら済みにして件数を添える。以前は杭が 1 本でもあれば済みにしていた (2026-10-15 のレビュー)。
        /// </summary>
        [TestMethod]
        public void InputStep_ReflectsTheInputCheck()
        {
            var blocked = MainWindowViewModel.BuildWorkflowSteps(12, false, false, false, null, null, null, null, null, new InputCheckCounts(2, 3));
            Assert.AreEqual(WorkflowStepStatus.NeedsRedo, blocked[0].Status, "解析を止める入力があるのに済みにしています");
            Assert.AreEqual("杭 12 本・解析を止める 2 件", blocked[0].StateText);
            StringAssert.Contains(blocked[0].ToolTip, "解析を止める入力が 2 件");
            Assert.AreEqual(WorkflowStepStatus.NotYet, blocked[1].Status, "入力が済んでいないのに杭要素分割を次にしています");

            var warned = MainWindowViewModel.BuildWorkflowSteps(12, false, false, false, null, null, null, null, null, new InputCheckCounts(0, 3));
            Assert.AreEqual(WorkflowStepStatus.Done, warned[0].Status);
            Assert.AreEqual("杭 12 本・注意 3 件", warned[0].StateText);
            Assert.AreEqual(WorkflowStepStatus.Next, warned[1].Status);

            var clean = MainWindowViewModel.BuildWorkflowSteps(12, false, false, false, null, null, null, null, null, new InputCheckCounts(0, 0));
            Assert.AreEqual("杭 12 本", clean[0].StateText);
            // 杭が無ければ件数は出さない (杭が無いことが先)
            var none = MainWindowViewModel.BuildWorkflowSteps(0, false, false, false, null, null, null, null, null, new InputCheckCounts(5, 1));
            Assert.AreEqual("杭が未入力", none[0].StateText);
            Assert.AreEqual(WorkflowStepStatus.Next, none[0].Status);
        }

        /// <summary>実際の入力で数え直す: 杭間隔比 R/B を 1 未満にすると解析を止める 1 件が出て、戻すと消える。数え直しは速い。</summary>
        [TestMethod]
        public void InputCheck_CountsTheRealInput()
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) { Assert.Inconclusive(error); return; }
            using var vm = new MainWindowViewModel { CurrentInputModel = input };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            vm.RunWorkflowInputCheck();
            sw.Stop();
            Assert.AreEqual(WorkflowStepStatus.Done, vm.WorkflowSteps[0].Status, $"(前提) 計算例10 は解析を止める入力が無い: {vm.WorkflowSteps[0].StateText}");
            Assert.IsTrue(sw.ElapsedMilliseconds < 2000, $"入力の検査の数え直しに {sw.ElapsedMilliseconds} ms かかっています (編集のたびに走る)");

            var raised = new List<string?>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            double original = input.PileLayoutItems[0].PileSpacingFactor;
            input.PileLayoutItems[0].PileSpacingFactor = 0.5;
            vm.RunWorkflowInputCheck();
            Assert.AreEqual(WorkflowStepStatus.NeedsRedo, vm.WorkflowSteps[0].Status, vm.WorkflowSteps[0].StateText);
            StringAssert.Contains(vm.WorkflowSteps[0].StateText, "解析を止める 1 件");
            CollectionAssert.Contains(raised, nameof(MainWindowViewModel.WorkflowSteps), "件数が変わったのに帯へ知らせていません");

            input.PileLayoutItems[0].PileSpacingFactor = original;
            vm.RunWorkflowInputCheck();
            Assert.AreEqual(WorkflowStepStatus.Done, vm.WorkflowSteps[0].Status);
            // 編集の番号が進むたびに数え直しを頼む (待ってから 1 回)
            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.EvaluationColoring.cs"), "RequestWorkflowInputCheck();");
        }

        /// <summary>各工程を押すと、その工程の画面を開くコマンドにつながっている。</summary>
        [TestMethod]
        public void EachStep_OpensItsWindow()
        {
            var vm = new MainWindowViewModel();
            var steps = vm.WorkflowSteps;
            Assert.AreSame(vm.OpenDiagnosticListCommand, steps[0].Command);
            Assert.AreSame(vm.OpenElementDivisionWindowCommand, steps[1].Command);
            Assert.AreSame(vm.OpenLateralLoadAnalysisWindowCommand, steps[2].Command);
            Assert.AreSame(vm.OpenResultDashboardCommand, steps[3].Command);
            Assert.AreSame(vm.OpenDocxOutputWindowCommand, steps[4].Command);

            bool opened = false;
            vm.OpenResultDashboardAction = () => opened = true;
            steps[3].Command!.Execute(null);
            Assert.IsTrue(opened, "検定を押してもダッシュボードが開きません");

            string main = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml.cs");
            StringAssert.Contains(main, "viewModel.OpenResultDashboardAction = ");
            string xaml = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");
            StringAssert.Contains(xaml, "<ctrl:WorkflowStrip Grid.Row=\"1\"/>");
        }

        /// <summary>入力を変えた・杭要素分割した、で帯が描き直される (通知が無いと古い状態のまま残る)。</summary>
        [TestMethod]
        public void TheStrip_IsRenotified_WhenItsInputsChange()
        {
            var built = AnalysisInputScopeTests.BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var vm = built.Value.vm;
            Assert.IsTrue(vm.RequiredAnalyses[0] is { Name: "水平", Done: true, Stale: false }, "前提: 水平解析済み");

            var raised = new List<string?>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            vm.MarkInputChangedSinceAnalysis(MainWindowViewModel.AnalysisInputScope.Model, "杭配置");
            CollectionAssert.Contains(raised, nameof(MainWindowViewModel.WorkflowSteps));
            Assert.AreEqual(WorkflowStepStatus.NeedsRedo, vm.WorkflowSteps[2].Status);

            raised.Clear();
            vm.IsElementSplit = !vm.IsElementSplit;
            CollectionAssert.Contains(raised, nameof(MainWindowViewModel.WorkflowSteps), "杭要素分割の切り替えで知らせていません");
        }

        // ── 入力の検査の状態 (2026-10-17 のレビュー) ──────────────────────────────

        private static WorkflowStep InputStep(InputCheckStatus status) =>
            MainWindowViewModel.BuildWorkflowSteps(12, false, false, false, null, null, null, null, null, status)[0];

        /// <summary>
        /// まだ数えていない・失敗した・数え直し中を、数えて不備 0 件と区別する。
        /// 以前は件数が無いことを「不備 0 件」と同じに扱い、検査に失敗しても開いた直後でも「入力」が ✓ に見えた。
        /// </summary>
        [TestMethod]
        public void InputStep_DistinguishesNotCheckedFailedAndRefreshing()
        {
            var notChecked = InputStep(new InputCheckStatus(InputCheckState.NotChecked));
            Assert.AreEqual("杭 12 本・点検待ち", notChecked.StateText);
            Assert.AreEqual(WorkflowStepStatus.Next, notChecked.Status, "数えていないのに済みにしています");

            var failed = InputStep(new InputCheckStatus(InputCheckState.Failed));
            Assert.AreEqual("杭 12 本・点検できませんでした", failed.StateText);
            Assert.AreEqual(WorkflowStepStatus.NeedsRedo, failed.Status, "検査に失敗したのに済みにしています");
            StringAssert.Contains(failed.ToolTip, "「入力の診断」で確かめてください");

            var refreshingClean = InputStep(new InputCheckStatus(InputCheckState.Checked, new InputCheckCounts(0, 1), Refreshing: true));
            Assert.AreEqual("杭 12 本・注意 1 件（点検中）", refreshingClean.StateText);
            Assert.AreEqual(WorkflowStepStatus.Done, refreshingClean.Status, "数え直し中に色を変えています (打つたびにちらつく)");
            var refreshingBlocked = InputStep(new InputCheckStatus(InputCheckState.Checked, new InputCheckCounts(2, 0), Refreshing: true));
            Assert.AreEqual(WorkflowStepStatus.NeedsRedo, refreshingBlocked.Status);
            StringAssert.Contains(refreshingBlocked.StateText, "（点検中）");

            // 杭要素分割は、止める入力が無いと分かったときと、まだ分からない (点検待ち) ときに次へ進める
            var steps = MainWindowViewModel.BuildWorkflowSteps(12, false, false, false, null, null, null, null, null, new InputCheckStatus(InputCheckState.Failed));
            Assert.AreEqual(WorkflowStepStatus.NotYet, steps[1].Status, "検査に失敗したのに杭要素分割を次にしています");
        }

        /// <summary>モデルを差し替えたら前のモデルの件数を捨てて「点検待ち」、編集なら件数を残して「点検中」。数え終われば件数。</summary>
        [TestMethod]
        public void InputCheckStatus_FollowsModelReplacementAndEdits()
        {
            var built = AnalysisInputScopeTests.BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var vm = built.Value.vm;
            vm.RunWorkflowInputCheck();
            Assert.AreEqual(InputCheckState.Checked, vm.WorkflowInputCheckStatus.State);
            Assert.IsFalse(vm.WorkflowInputCheckStatus.Refreshing);

            vm.SaveUndoState("杭配置");   // 編集 (InputEditVersion が進む)
            Assert.IsTrue(vm.WorkflowInputCheckStatus.Refreshing, "編集のあと、数え直し中であることを示していません");
            Assert.IsNotNull(vm.WorkflowInputCheckStatus.Counts, "編集のたびに件数を捨てています (帯がちらつく)");
            StringAssert.Contains(vm.WorkflowSteps[0].StateText, "（点検中）");
            vm.RunWorkflowInputCheck();
            Assert.IsFalse(vm.WorkflowInputCheckStatus.Refreshing);

            var other = AnalysisInputScopeTests.BuildAnalyzed()!.Value.input;
            vm.CurrentInputModel = other;
            Assert.AreEqual(InputCheckState.NotChecked, vm.WorkflowInputCheckStatus.State, "別のモデルを開いたのに前のモデルの件数を出しています");
            StringAssert.Contains(vm.WorkflowSteps[0].StateText, "点検待ち");

            // 検査の例外は「点検できませんでした」にする (件数を捨てて不備 0 件に見せない)
            string code = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.Workflow.cs");
            string run = TestSource.MethodBody(code, "internal void RunWorkflowInputCheck(");
            StringAssert.Contains(run, "status = new InputCheckStatus(InputCheckState.Failed);");
        }

        // ── 沈下の解析も帯に出す (2026-10-17 のレビュー) ──────────────────────────────

        private static IReadOnlyList<WorkflowStep> Steps(params RequiredAnalysisState[] analyses) =>
            MainWindowViewModel.BuildWorkflowSteps(12, true, analyses[0].Done, analyses[0].Stale, null, null, null, null, null,
                new InputCheckCounts(0, 0), analyses, "単杭＋群杭沈下");

        /// <summary>
        /// 「解析」は検討に要る解析ごとの状態を出し、色は一番悪いもの。検定・計算書も同じ材料で決める。
        /// 以前は水平解析だけを見ており、沈下の解析が未実施でも「解析 済み」「計算書 出力できます」と出ていた。
        /// </summary>
        [TestMethod]
        public void AnalysisStep_ShowsEachRequiredAnalysis()
        {
            var all = Steps(new("水平", true, false), new("単杭沈下", true, false), new("群杭沈下", true, false));
            Assert.AreEqual("水平 済み・単杭沈下 済み・群杭沈下 済み", all[2].StateText);
            Assert.AreEqual(WorkflowStepStatus.Done, all[2].Status);
            Assert.AreEqual("結果あり", all[3].StateText);
            Assert.AreEqual(WorkflowStepStatus.Done, all[3].Status);
            Assert.AreEqual("出力できます", all[4].StateText);
            StringAssert.Contains(all[2].ToolTip, "沈下検討の対象は「単杭＋群杭沈下」", "検討対象を説明していません");

            var partial = Steps(new("水平", true, false), new("単杭沈下", false, false), new("群杭沈下", false, false));
            Assert.AreEqual("水平 済み・単杭沈下 未・群杭沈下 未", partial[2].StateText);
            Assert.AreEqual(WorkflowStepStatus.Next, partial[2].Status, "沈下の解析が未実施なのに解析を済みにしています");
            Assert.AreEqual("一部の解析のみ", partial[3].StateText);
            Assert.AreEqual("未実施の解析あり", partial[4].StateText);

            var stale = Steps(new("水平", true, false), new("単杭沈下", true, true), new("群杭沈下", true, false));
            Assert.AreEqual("水平 済み・単杭沈下 やり直し・群杭沈下 済み", stale[2].StateText);
            Assert.AreEqual(WorkflowStepStatus.NeedsRedo, stale[2].Status, "沈下の結果が古いのにやり直しにしていません");
            Assert.AreEqual(WorkflowStepStatus.NeedsRedo, stale[3].Status);
            Assert.AreEqual(WorkflowStepStatus.NeedsRedo, stale[4].Status);

            var none = Steps(new("水平", false, false), new("単杭沈下", false, false));
            Assert.AreEqual(WorkflowStepStatus.NotYet, none[3].Status);
            Assert.AreEqual("解析後", none[4].StateText);
        }

        /// <summary>群杭沈下は「沈下検討の対象」が「単杭＋群杭沈下」のときだけ要る。単杭沈下は基礎梁考慮の解析でも済み。</summary>
        [TestMethod]
        public void RequiredAnalyses_FollowTheSettlementBasis()
        {
            var vm = new MainWindowViewModel();
            vm.CurrentInputModel!.FundamentalInput.SettlementDesignIncludesGroup = true;
            CollectionAssert.AreEqual(new[] { "水平", "単杭沈下", "群杭沈下" }, vm.RequiredAnalyses.Select(a => a.Name).ToArray());
            vm.CurrentInputModel.FundamentalInput.SettlementDesignIncludesGroup = false;
            CollectionAssert.AreEqual(new[] { "水平", "単杭沈下" }, vm.RequiredAnalyses.Select(a => a.Name).ToArray(),
                "「単杭沈下」だけを検討するのに群杭沈下を求めています");

            Assert.IsFalse(vm.RequiredAnalyses[1].Done);
            var raised = new List<string?>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            vm.IsVerticalBeamAnalysisDone = true;
            CollectionAssert.Contains(raised, nameof(MainWindowViewModel.WorkflowSteps), "沈下の解析が済んでも帯を描き直していません");
            // 沈下量の検定が使うのは単杭沈下 (通常) の結果だけ。基礎梁考慮の結果だけでは単杭沈下を済みにしない (2026-10-18)
            Assert.IsFalse(vm.RequiredAnalyses[1].Done, "基礎梁考慮の結果だけで、検定が使う単杭沈下を済みにしています");
            Assert.IsTrue(vm.RequiredAnalyses.Any(a => a is { Name: "単杭沈下（基礎梁考慮）", Done: true }),
                "基礎梁考慮の結果があるのに、別に並べていません");
        }

        /// <summary>
        /// 済み・古さは検定が実際に使う結果で決める (2026-10-18 のレビュー)。通常の単杭沈下を解き直したあと、残した基礎梁考慮の
        /// 結果だけが古いときは、基礎梁考慮だけを「やり直し」にし、単杭沈下は済みのまま。基礎梁考慮の結果が無ければ並べない。
        /// 群杭沈下は、基礎梁なし・ありの両方を検定に使うので、どちらかが古ければやり直しのまま。
        /// </summary>
        [TestMethod]
        public void SettlementStates_FollowTheResultsTheEvaluationUses()
        {
            var vm = new MainWindowViewModel();
            vm.CurrentInputModel!.FundamentalInput.SettlementDesignIncludesGroup = false;
            vm.IsVerticalAnalysisDone = true;
            Assert.IsFalse(vm.RequiredAnalyses.Any(a => a.Name.Contains("基礎梁考慮")), "基礎梁考慮の結果が無いのに「未」として並べています");

            vm.IsVerticalBeamAnalysisDone = true;
            vm.RestoreSettlementStale(SettlementAnalyses.VerticalBeam);
            var single = vm.RequiredAnalyses.Single(a => a.Name == "単杭沈下");
            var beam = vm.RequiredAnalyses.Single(a => a.Name == "単杭沈下（基礎梁考慮）");
            Assert.IsFalse(single.Stale, "検定が使わない基礎梁考慮の古い結果で、単杭沈下をやり直しにしています");
            Assert.IsTrue(beam.Stale, "古い基礎梁考慮の結果を、やり直しとして示していません");
            StringAssert.Contains(vm.WorkflowSteps[2].StateText, "単杭沈下 済み・単杭沈下（基礎梁考慮） やり直し");

            // 群杭沈下: 基礎梁なしの結果が古ければ (ありが新しくても) やり直し
            vm.CurrentInputModel.FundamentalInput.SettlementDesignIncludesGroup = true;
            vm.IsGroupPileSettlementAnalysisDone = true;
            vm.RestoreSettlementStale(SettlementAnalyses.Group);
            Assert.IsTrue(vm.RequiredAnalyses.Single(a => a.Name == "群杭沈下").Stale);
        }
    }
}
