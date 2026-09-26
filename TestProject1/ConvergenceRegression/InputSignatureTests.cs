using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Diagnostics;

namespace TestProject1.ConvergenceRegression
{
    /// <summary>
    /// 「再解析が必要」を、解析したときの入力と今の入力の中身で決め直すこと (2026-09-27)。
    ///
    /// 以前は編集のたびに印を立てるだけで、元に戻すで解析時と同じ入力へ戻しても「再解析が必要」が残り、
    /// 逆に解析より前の入力へ戻しても印が立たなかった。
    /// </summary>
    [TestClass]
    public class InputSignatureTests
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
        public void TheSignatureFollowsTheContentNotTheInstance()
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            Assert.IsNotNull(input, error);

            var sw = Stopwatch.StartNew();
            string? original = MainWindowViewModel.InputSignature(input);
            sw.Stop();
            Assert.IsNotNull(original);
            Console.WriteLine($"署名の計算: {sw.ElapsedMilliseconds} ms (計算例 10、初回)");
            sw.Restart();
            MainWindowViewModel.InputSignature(input);
            Console.WriteLine($"署名の計算: {sw.ElapsedMilliseconds} ms (2 回目)");
            var json = System.Text.Json.JsonSerializer.Serialize(input, new System.Text.Json.JsonSerializerOptions
            {
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
            });
            Console.WriteLine($"署名の元の大きさ: {json.Length / 1024} KB");

            Assert.AreEqual(original, MainWindowViewModel.InputSignature(input.DeepCopy()),
                "中身が同じ複製 (元に戻すが作る入力) で署名が変わります");

            input.PileLayoutItems[0].IsSelected = !input.PileLayoutItems[0].IsSelected;
            Assert.AreEqual(original, MainWindowViewModel.InputSignature(input), "選択の状態で署名が変わります (画面だけの状態)");

            input.PileLayoutItems[0].AxialForceVL0 += 1.0;
            Assert.AreNotEqual(original, MainWindowViewModel.InputSignature(input), "軸力を変えても署名が変わりません");
        }

        [TestMethod]
        public void UndoingBackToTheAnalysedInputClearsTheWarningAndRedoRaisesIt()
        {
            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
            try
            {
                var vm = Run();
                if (vm == null) return;
                vm.CaptureAnalysisResultSet();   // 画面の解析の終わりと同じく、解析結果の控えを取る
                Assert.IsTrue(vm.HasAnalysisResultSet, "(前提) 解析結果の控えがありません");

                // 解析したときの入力を履歴に積み、1 つ編集する
                vm.SaveUndoState("解析時");
                vm.CurrentInputModel!.PileLayoutItems[0].AxialForceVL0 += 100.0;
                vm.SaveUndoState("軸力の編集");
                Assert.IsTrue(vm.InputChangedSinceAnalysis, "(前提) 編集で「再解析が必要」が立っていません");

                vm.UndoCommand.Execute(null);
                Assert.IsFalse(vm.InputChangedSinceAnalysis, "解析したときと同じ入力へ戻したのに「再解析が必要」が残っています");
                Assert.IsFalse(vm.ResultSetStatusText.Contains("再解析が必要"), vm.ResultSetStatusText);

                vm.RedoCommand.Execute(null);
                Assert.IsTrue(vm.InputChangedSinceAnalysis, "やり直しで編集後の入力へ移ったのに「再解析が必要」が立ちません");
                StringAssert.Contains(vm.ResultSetStatusText, "再解析が必要");
            }
            finally
            {
                MessageService.IsUnattended = unattended;
            }
        }

        [TestMethod]
        public void UndoingPastTheAnalysisRaisesTheWarning()
        {
            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
            try
            {
                var vm = Run();
                if (vm == null) return;
                vm.CaptureAnalysisResultSet();

                // 解析より前の入力 (軸力が違う) → 解析時の入力、の順に履歴を積む
                vm.CurrentInputModel!.PileLayoutItems[0].AxialForceVL0 -= 100.0;
                vm.SaveUndoState("解析前");
                vm.CurrentInputModel.PileLayoutItems[0].AxialForceVL0 += 100.0;
                vm.SaveUndoState("解析時");
                vm.RecheckInputAgainstAnalysis();
                Assert.IsFalse(vm.InputChangedSinceAnalysis, "(前提) 解析時の入力なのに「再解析が必要」になっています");

                vm.UndoCommand.Execute(null);
                Assert.IsTrue(vm.InputChangedSinceAnalysis, "解析より前の入力へ戻したのに「再解析が必要」が立ちません");
            }
            finally
            {
                MessageService.IsUnattended = unattended;
            }
        }
    }
}
