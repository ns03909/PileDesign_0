using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestProject1
{
    /// <summary>
    /// 単杭沈下の画面で、入力と結果 (曲線・各杭の単杭沈下量) の組が食い違わないこと (2026-10-11 外部レビュー 3〜5)。
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class SettlementDialogConsistency20261011Tests
    {
        private static VerticalLoadTransferMethod Solve(InputModel model, SoilPile sp) => new(model, sp);

        /// <summary>
        /// 例題 3.4 の 1 本の杭の杭頭を下げ、同じ杭体を使う土層-杭セットを 2 つにする (例題には無いため)。
        /// </summary>
        private static InputModel? ModelWithSharedBody()
        {
            var (model, _) = IntegrationTests.BuildExampleInputModel("Example3_4", "PileExample3_4");
            if (model == null) return null;
            var group = model.PileLayoutItems.GroupBy(p => p.PileBodyNo).FirstOrDefault(g => g.Count() >= 2);
            if (group == null) return null;
            group.Last().Z -= 0.5;
            model.GenerateSoilPiles();
            var sets = model.ElementDivision.SoilPiles;
            if (sets.Count(s => s.PileBodyNo == group.Key) < 2) return null;
            if (sets.Any(s => s.PileToeNValue == 0)) return null;   // 警告ダイアログが出る
            return model;
        }

        private static void OnSta(Func<InputModel?> build, Action<InputModel> check)
        {
            var model = build();
            if (model == null) { Assert.Inconclusive("例題を組めません"); return; }
            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
            try
            {
                var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
                {
                    SynchronizationContext.SetSynchronizationContext(null);
                    check(model);
                }, out bool timedOut);
                if (timedOut) { Assert.Inconclusive("STA スレッドで時間内に終わりませんでした"); return; }
                if (captured is AssertFailedException || captured is AssertInconclusiveException) throw captured;
                if (captured != null) Assert.Fail(captured.ToString());
            }
            finally { MessageService.IsUnattended = unattended; }
        }

        private static InputModel? Example() => IntegrationTests.BuildExampleInputModel("Example3_4", "PileExample3_4").Item1;

        /// <summary>
        /// 3. 解析中・解析に失敗したあとに OK を押しても確定しない。以前は解析の完了を待たずに今の (古い) 曲線を写して閉じ、
        /// あとから終わった解析の結果は捨て、そのうえ結果を「最新」と記録していた。
        /// </summary>
        [TestMethod]
        public void Ok_WaitsForTheAnalysis_AndRefusesAfterAFailure()
        {
            OnSta(Example, model =>
            {
                var original = model.ElementDivision.SoilPiles;
                var vm = new SettlementViewModel(new MainWindowViewModel { CurrentInputModel = model });
                vm.AnalysisRunnerForTesting = (m, sp) => Task.FromResult(Solve(m, sp));
                bool closed = false;
                vm.RequestClose += (_, _) => closed = true;

                // 解析中の OK
                var pending = new TaskCompletionSource<VerticalLoadTransferMethod>();
                var run = vm.ExecuteAnalysisCore((_, _) => pending.Task);
                vm.OkCommand.Execute(null);
                Assert.IsFalse(closed, "解析中なのに OK で閉じました");
                Assert.AreSame(original, model.ElementDivision.SoilPiles, "解析中なのに OK で確定しました");

                // 解析に失敗したあとの OK
                pending.SetException(new InvalidOperationException("検査用の失敗"));
                Assert.ThrowsException<AggregateException>(() => run.Wait());
                vm.OkCommand.Execute(null);
                Assert.IsFalse(closed, "解析に失敗したのに OK で閉じました");
                Assert.AreSame(original, model.ElementDivision.SoilPiles, "解析に失敗したのに OK で確定しました");

                // 解き直して成功すれば確定できる
                vm.ExecuteAnalysisCore((m, sp) => Task.FromResult(Solve(m, sp))).Wait();
                vm.OkCommand.Execute(null);
                Assert.IsTrue(closed, "解析が成功したのに OK で閉じません");
                Assert.AreNotSame(original, model.ElementDivision.SoilPiles);
            });
        }

        /// <summary>
        /// 4. 編集 → 解き直し → 元に戻すで、入力・曲線と一緒に各杭の単杭沈下量も戻る。やり直すと編集後の値に戻る。
        /// 以前は入力と曲線だけが戻り、各杭の沈下量は編集後の値が残った。
        /// </summary>
        [TestMethod]
        public void Undo_RestoresEachPilesSettlementWithTheInput()
        {
            OnSta(Example, model =>
            {
                var vm = new SettlementViewModel(new MainWindowViewModel { CurrentInputModel = model });
                vm.AnalysisRunnerForTesting = (m, sp) => Task.FromResult(Solve(m, sp));
                vm.AnalyzeAllSoilPiles();
                string Settlements() => string.Join("|", model.PileLayoutItems.Select(p => $"{p.SinglePileSettlementVL:R}"));
                var body = vm.PileBody;
                double alpha = body.SettleAlpha;
                string before = Settlements();

                // 画面と同じ順: 欄を離れる直前 (PreviewLostKeyboardFocus) に控えを取り、そのあと値を書く
                vm.SaveUndoSnapshot();
                body.SettleAlpha = alpha + 0.3;
                vm.ExecuteAnalysisCore((m, sp) => Task.FromResult(Solve(m, sp))).Wait();
                string edited = Settlements();
                Assert.AreNotEqual(before, edited, "(前提) α を変えて解き直すと単杭沈下量が変わること");

                vm.UndoCommand.Execute(null);
                Assert.AreEqual(alpha, body.SettleAlpha, 1e-12, "α が戻っていません");
                Assert.AreEqual(before, Settlements(), "元に戻しても、各杭の単杭沈下量が編集後の値のままです");

                vm.RedoCommand.Execute(null);
                Assert.AreEqual(alpha + 0.3, body.SettleAlpha, 1e-12);
                Assert.AreEqual(edited, Settlements(), "やり直しで、各杭の単杭沈下量が編集後の値に戻りません");

                vm.CancelCommand.Execute(null);
            });
        }

        /// <summary>
        /// 5. 杭体の α・n を変えると、同じ杭体を使うセットすべて (表示していないセットも) を解き直す。
        /// 以前は表示中のセットだけを解き直し、ほかのセットには変更前の曲線と沈下量が残った (杭先端径だけはほかも解き直していた)。
        /// </summary>
        [TestMethod]
        public void ChangingAlpha_ReanalyzesEverySetOfTheSameBody()
        {
            OnSta(ModelWithSharedBody, model =>
            {
                var vm = new SettlementViewModel(new MainWindowViewModel { CurrentInputModel = model });
                vm.AnalysisRunnerForTesting = (m, sp) => Task.FromResult(Solve(m, sp));
                vm.AnalyzeAllSoilPiles();

                int bodyNo = vm.SoilPiles.GroupBy(s => s.PileBodyNo).First(g => g.Count() >= 2).Key;
                int shown = vm.SoilPiles.ToList().FindIndex(s => s.PileBodyNo == bodyNo);
                vm.SelectSoilPileIndex(shown);
                var other = vm.SoilPiles.Where(s => s.PileBodyNo == bodyNo && !ReferenceEquals(s, vm.SoilPile)).First();
                int otherNo = vm.SoilPiles.IndexOf(other) + 1;
                var otherCurve = other.LoadDisplacements;
                var otherPiles = model.PileLayoutItems.Where(p => p.SoilPileAltNo == otherNo).ToList();
                Assert.IsTrue(otherPiles.Count > 0, "(前提) 表示していないセットを使う杭があること");
                var otherBefore = otherPiles.Select(p => p.SinglePileSettlementVL).ToList();

                vm.ComboBoxPresetSettlementParameters = new System.Windows.Controls.ComboBox();   // 画面が持つ欄 (テストには画面が無い)
                var box = new System.Windows.Controls.TextBox { Name = "TextBoxSettleAlpha", Text = $"{vm.PileBody.SettleAlpha + 0.3:R}" };
                vm.OnPileBodyLostFocus(box);

                Assert.AreNotSame(otherCurve, other.LoadDisplacements, "同じ杭体の、表示していないセットの曲線を解き直していません");
                CollectionAssert.AreNotEqual(otherBefore, otherPiles.Select(p => p.SinglePileSettlementVL).ToList(),
                    "同じ杭体の、表示していないセットを使う杭の単杭沈下量が変更前のままです");
                vm.CancelCommand.Execute(null);
            });
        }
    }
}
