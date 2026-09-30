using PileDesign.Output;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace TestProject1
{
    /// <summary>
    /// 計算書の作成は途中で止められる。以前は画面のスレッドで一気に作っていたので、大規模なモデルでは
    /// 数十秒〜数分のあいだ画面が止まり、止める手段も無かった。
    /// </summary>
    [TestClass]
    public class ReportCancellationTests
    {
        /// <summary>計算書は STA スレッドで作る (WPF の描画を使う)。</summary>
        private static void OnSta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure is AssertInconclusiveException inconclusive) throw inconclusive;
            if (failure != null) Assert.Fail("STA スレッド内で例外:\n" + failure);
        }

        private static (PileDesign.Models.InputData.InputModel Input, MainWindowViewModel Vm) Example()
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("Example3_1", "PileExample3_1");
            if (input == null) Assert.Inconclusive($"例題ロード失敗: {error}");
            input!.FundamentalInput ??= new PileDesign.Models.InputData.FundamentalInput();
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            vm.DocxOutput.SelectAllDocxSectionsCommand.Execute(null);
            return (input, vm);
        }

        /// <summary>
        /// <b>本題。</b> 途中で止めると <see cref="OperationCanceledException"/> で抜け、作りかけの計算書は残さない。
        /// 同じ名前で前に出力した計算書はそのまま (一時ファイルに書いてから差し替えるので、差し替えずに捨てる)。
        /// 図表 1 つの失敗を省いて続ける作り (<c>NoteOmitted</c>) が、止められたことまで「失敗」として飲み込まないこと。
        /// </summary>
        [TestMethod]
        [Timeout(300000)]
        public void CancellingMidway_LeavesThePreviousReportAsItWas()
        {
            OnSta(() =>
            {
                var (input, vm) = Example();
                string dir = Path.Combine(Path.GetTempPath(), $"pd_report_cancel_{Guid.NewGuid():N}");
                Directory.CreateDirectory(dir);
                try
                {
                    string path = Path.Combine(dir, "計算書.docx");
                    File.WriteAllText(path, "前に出力した計算書");

                    using var cancellation = new CancellationTokenSource();
                    var seen = new List<ReportProgress>();
                    int pumps = 0;
                    var run = new ReportRunControl
                    {
                        Token = cancellation.Token,
                        PumpInterval = TimeSpan.Zero,
                        PumpMessages = () => pumps++,
                        Report = p =>
                        {
                            seen.Add(p);
                            // 入力データの章の途中で止める
                            if (p.StepNumber >= 2 && p.Items >= 5) cancellation.Cancel();
                        },
                    };

                    var doc = new WordDocument(input, null!, vm);
                    Assert.ThrowsException<OperationCanceledException>(() => doc.CreateWordDocument(input, path, run));

                    Assert.AreEqual("前に出力した計算書", File.ReadAllText(path), "止めたのに前の計算書が書き換わっています");
                    CollectionAssert.AreEqual(new[] { "計算書.docx" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray(),
                        "作りかけのファイルが残っています");
                    Assert.IsTrue(pumps > 0, "作成中に画面のメッセージを回していません (中止のボタンが押せない)");
                    Assert.IsFalse(seen.Any(p => p.StepNumber == WordDocument.ReportStepCount - 1),
                        "止めたあとも書き出しの段階まで進んでいます");
                }
                finally { Directory.Delete(dir, recursive: true); }
            });
        }

        /// <summary>
        /// 止めなければ最後まで作る。区切りは章の中まで細かく入っている (段階の境目だけだと、いちばん長い章のあいだ止められない)。
        /// 受け口を渡さなければ従来どおり (テスト・ほかの呼び出し元)。
        /// </summary>
        [TestMethod]
        [Timeout(300000)]
        public void WithoutCancelling_TheReportIsWritten_AndCheckpointsAreFineGrained()
        {
            OnSta(() =>
            {
                var (input, vm) = Example();
                string path = Path.Combine(Path.GetTempPath(), $"pd_report_{Guid.NewGuid():N}.docx");
                try
                {
                    var seen = new List<ReportProgress>();
                    var run = new ReportRunControl { PumpInterval = TimeSpan.Zero, Report = seen.Add };
                    new WordDocument(input, null!, vm).CreateWordDocument(input, path, run);

                    Assert.IsTrue(new FileInfo(path).Length > 10_000, "計算書が作られていません");
                    CollectionAssert.AreEqual(Enumerable.Range(0, WordDocument.ReportStepCount).ToArray(),
                        seen.Select(p => p.StepNumber).Distinct().ToArray(), "段階を順に通っていません");
                    int inInputChapter = seen.Count(p => p.StepNumber == 2);
                    Assert.IsTrue(inInputChapter >= 10, $"入力データの章の中の区切りが少なすぎます ({inInputChapter} 回)");

                    // 受け口なし
                    new WordDocument(input, null!, vm).CreateWordDocument(input, path);
                    Assert.IsTrue(new FileInfo(path).Length > 10_000);
                }
                finally { File.Delete(path); }
            });
        }

        /// <summary>画面の計算書の出力は、進み具合と中止の窓を通して作る (砂時計で画面を止めたまま作らない)。</summary>
        [TestMethod]
        public void TheScreenBuildsTheReportThroughTheCancellableRunner()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs");
            StringAssert.Contains(src, "RunCancellableReport(run => doc.CreateWordDocument(inputForReport, saveFileDialog.FileName, run))");
            Assert.IsFalse(src.Contains("Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait"), "砂時計で画面を止めたまま作っています");

            // 試験中 (ダイアログを出さない) は窓を出さずにそのまま作る
            var vm = new MainWindowViewModel();
            bool built = false;
            Assert.IsTrue(vm.RunCancellableReport(_ => built = true));
            Assert.IsTrue(built);
        }
    }
}
