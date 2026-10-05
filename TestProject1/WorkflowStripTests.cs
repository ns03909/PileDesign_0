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
            Assert.AreEqual(WorkflowStepStatus.Done, vm.WorkflowSteps[2].Status, "前提: 解析済み");

            var raised = new List<string?>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            vm.MarkInputChangedSinceAnalysis(MainWindowViewModel.AnalysisInputScope.Model, "杭配置");
            CollectionAssert.Contains(raised, nameof(MainWindowViewModel.WorkflowSteps));
            Assert.AreEqual(WorkflowStepStatus.NeedsRedo, vm.WorkflowSteps[2].Status);

            raised.Clear();
            vm.IsElementSplit = !vm.IsElementSplit;
            CollectionAssert.Contains(raised, nameof(MainWindowViewModel.WorkflowSteps), "杭要素分割の切り替えで知らせていません");
        }
    }
}
