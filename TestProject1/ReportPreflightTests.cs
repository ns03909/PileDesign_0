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
                Assert.IsFalse(vm.ConfirmReportPreflight(), "キャンセルしたのに作ります");
                Assert.IsNotNull(asked, "確かめることがあるのに確認を出していません");
                Assert.IsTrue(asked!.Any(i => i.Text.StartsWith("単杭沈下解析が未実施")));

                vm.AskReportPreflight = _ => (ReportPreflightChoice.Proceed, ReportPreflightTarget.None);
                Assert.IsTrue(vm.ConfirmReportPreflight(), "「このまま作成」なのに作りません");

                vm.AskReportPreflight = _ => (ReportPreflightChoice.Navigate, ReportPreflightTarget.Dashboard);
                Assert.IsFalse(vm.ConfirmReportPreflight(), "確認先へ移ると選んだのに作ります");

                MessageService.IsUnattended = true;
                asked = null;
                vm.AskReportPreflight = items => { asked = items; return (ReportPreflightChoice.Cancel, ReportPreflightTarget.None); };
                Assert.IsTrue(vm.ConfirmReportPreflight(), "画面の無い実行で止まりました");
                Assert.IsNull(asked, "画面の無い実行で確認を出しています");
            }
            finally { MessageService.IsUnattended = unattended; }

            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "if (!ConfirmReportPreflight()) return;", "計算書を作る前に確かめていません");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml.cs"),
                "viewModel.AskReportPreflight = ", "確認の画面がメイン画面につながっていません");
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
    }
}
