using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;

namespace TestProject1.ConvergenceRegression
{
    /// <summary>
    /// 解析結果の控え (解析したときの入力の写し) を作れなかったときに、利用者が気づけること (2026-09-27)。
    ///
    /// 以前は複製の失敗をログに残して null を返すだけで、呼び出し側はそのまま続けた。結果表示は編集中の入力を
    /// 解析時の入力の代わりに見るので、入力を編集した時点で結果と入力が混ざり、それを示すものが無かった。
    /// </summary>
    [TestClass]
    public class ResultSnapshotFailureTests
    {
        private static MainWindowViewModel? Run()
        {
            try
            {
                return HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 2, Level2Steps = 4, UseLineSearch = true, Parallelism = 1,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.None,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive("例題ファイルなし");
                return null;
            }
        }

        [TestMethod]
        public void AFailedSnapshotIsReportedAndEditingMarksTheResultsAsMixed()
        {
            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
            try
            {
                var vm = Run();
                if (vm == null) return;

                // 控えが無い状態で失敗が起きたことにする
                vm.ClearAnalysisResultSetState();
                vm.OnResultSnapshotFailed();

                Assert.IsTrue(vm.ResultSnapshotFailed);
                Assert.IsFalse(vm.ResultsMixedWithEditedInput, "解いた直後は入力と結果が合っています");
                StringAssert.Contains(vm.ResultSetStatusText, "控えを作れませんでした", "状態表示で知らせていません");

                vm.MarkInputChangedSinceAnalysis();

                Assert.IsTrue(vm.InputChangedSinceAnalysis, "控えが無いまま編集しても、編集された印が立ちません (結果セットが無いと黙っていた)");
                Assert.IsTrue(vm.ResultsMixedWithEditedInput);
                StringAssert.Contains(vm.ResultSetStatusText, "混ざっています");

                // 控えを作り直せたら (再解析) 印は降りる
                vm.CaptureAnalysisResultSet();
                Assert.IsFalse(vm.ResultSnapshotFailed, "控えを作れたのに失敗の印が残っています");
                Assert.IsFalse(vm.ResultsMixedWithEditedInput);
            }
            finally
            {
                MessageService.IsUnattended = unattended;
            }
        }

        [TestMethod]
        public void TheCaptureReportsTheFailureInsteadOfContinuingSilently()
        {
            string capture = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.ResultSet.cs"),
                "public void CaptureAnalysisResultSet()");
            StringAssert.Contains(capture, "OnResultSnapshotFailed();", "控えを作れなかったときに黙って続けています");

            string failure = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.ResultSet.cs"),
                "internal void OnResultSnapshotFailed()");
            StringAssert.Contains(failure, "MessageService.Show(", "控えを作れなかったことを画面で知らせていません");
            StringAssert.Contains(failure, "CurrentResultSet = null", "前の解析の控えを新しい結果と組にしたまま残しています");
        }

        /// <summary>控えが無いまま入力を編集したら、計算書は出さない (解析時の入力で作り直す手段が無い)。</summary>
        [TestMethod]
        public void TheReportIsNotProducedFromMixedResults()
        {
            string output = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "public void OutputWordFile()");
            int gate = output.IndexOf("if (ResultsMixedWithEditedInput)", StringComparison.Ordinal);
            int dialog = output.IndexOf("saveFileDialog.ShowDialog()", StringComparison.Ordinal);
            Assert.IsTrue(gate >= 0, "計算書の出力が、結果と入力の混ざった状態を止めていません");
            Assert.IsTrue(gate < dialog, "保存先を訊いてから止めています (先に止める)");
        }

        [TestMethod]
        public void TheMessagesSayWhatToDo()
        {
            StringAssert.Contains(MainWindowViewModel.DescribeResultSnapshotFailure(), "入力を編集する前に");
            StringAssert.Contains(MainWindowViewModel.DescribeResultSnapshotStatus(inputEdited: true), "再解析が必要です");
            StringAssert.Contains(MainWindowViewModel.DescribeResultSnapshotStatus(inputEdited: false), "控えを作れませんでした");
        }
    }
}
