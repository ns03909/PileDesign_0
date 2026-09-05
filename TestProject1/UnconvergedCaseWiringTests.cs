using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.ViewModels;
using System;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 収束状態が、解析結果から検定の行まで<b>実際に届いている</b>こと。
    ///
    /// 型と集計は <see cref="UnconvergedCaseEvaluationTests"/> で固定しているが、
    /// それだけでは「結果と検定行を突き合わせる鍵が合っているか」が分からない。
    /// 鍵 (荷重ケース名 / 荷重組合せ名 / 液状化) がずれていると、
    /// <b>印は一件も付かず、テストは全部通る</b>。まさに今回直した種類の欠陥なので、
    /// 例題を実際に解いた結果に未収束を差し込んで、行に届くところまで確かめる。
    /// </summary>
    [TestClass]
    public class UnconvergedCaseWiringTests
    {
        /// <summary>例題 9 (場所打ちRC + 18 杭)。golden テストと同じ条件で解く。</summary>
        private static MainWindowViewModel? RunExample9()
        {
            var options = new HeadlessHorizontalRunner.RunOptions
            {
                Level1Steps = 4,
                Level2Steps = 8,
                LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                UseLineSearch = true,
                Parallelism = 1,
            };

            try
            {
                var vm = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", options);
                vm?.ApplyConcreteModelOptions();
                return vm;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                return null;
            }
        }

        [TestMethod]
        [TestCategory("Slow")]
        public void ConvergenceStatus_ReachesTheEvaluationRows()
        {
            var vm = RunExample9();
            if (vm?.CurrentModel == null)
            {
                Assert.Inconclusive("例題 9 を読み込めませんでした");
                return;
            }

            var model = vm.CurrentModel;

            // 前提: 例題は公表された計算例なので、そのままなら全ケース収束している
            Assert.IsFalse(model.HasUnconvergedSteps(), "例題 9 が未収束のステップを含んでいます");
            var before = EvaluationService.BuildEvaluationResult(vm, factored: false);
            Assert.AreEqual(0, before.UnconvergedCount);
            Assert.IsTrue(before.Items.Count > 0, "検定項目が 0 件です");

            // レベル2 のケースを 1 つ選び、そのステップを未収束にする
            var target = model.AnalysisStepResults.First(r => r.LoadCase?.Level == 2);
            string caseName = target.LoadCase!.LoadName;
            string comboName = target.LoadCombination!.Name;
            bool liq = target.IsLiquefaction;

            var affected = model.AnalysisStepResults
                .Where(r => r.LoadCase?.LoadName == caseName
                         && r.LoadCombination?.Name == comboName
                         && r.IsLiquefaction == liq)
                .ToList();
            foreach (var r in affected) r.Status = StepStatus.Unconverged;

            // 検定をやり直すと、そのケースの行だけが「未収束」になる
            var after = EvaluationService.BuildEvaluationResult(vm, factored: false);

            Assert.IsTrue(after.UnconvergedCount > 0,
                "解析結果に未収束を記録したのに、検定の行に届いていません。"
                + "結果と検定行を突き合わせる鍵 (荷重ケース名 / 荷重組合せ名 / 液状化) がずれています。");

            Assert.AreEqual(after.Items.Count, before.Items.Count, "検定項目の件数が変わっています");

            // 印が付いたのは、そのケースの行だけであること
            foreach (var item in after.Items.Where(i => i.IsFromUnconvergedCase))
            {
                Assert.AreEqual(caseName, item.LoadCaseName);
                Assert.AreEqual(comboName, item.LoadCombinationName);
                Assert.AreEqual(liq, item.IsLiquefaction);
                Assert.AreEqual("未収束", item.StatusLabel);
            }

            // OK / NG の合計から、未収束の分だけ抜けていること
            Assert.AreEqual(before.OkCount + before.NgCount,
                after.OkCount + after.NgCount + after.UnconvergedCount,
                "未収束の行が OK / NG のどちらかに数えられています");

            // 検定テキストにも出ること
            string text = EvaluationService.BuildEvaluationText(vm, factored: false, displayFilter: 2);
            StringAssert.Contains(text, "未収束のケースの検定:");
            StringAssert.Contains(text, "[未収束]");

            // 後片付け: 解析済み VM は他のテストと共有しないが、念のため戻す
            foreach (var r in affected) r.Status = StepStatus.Converged;
        }
    }
}
