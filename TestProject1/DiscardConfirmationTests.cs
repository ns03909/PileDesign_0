using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 「現状の入力内容は削除されます」の確認を出す条件。
    ///
    /// 起動直後や計算例を読み込んだ直後にも出していると、
    /// 内容を確かめずに「はい」を押すだけの確認になり、
    /// 本当に失うものがあるときにも読まれなくなる。
    /// </summary>
    [TestClass]
    public class DiscardConfirmationTests
    {
        [TestMethod]
        public void FreshlyStarted_NeedsNoConfirmation()
        {
            var vm = new MainWindowViewModel();

            Assert.IsTrue(vm.CanDiscardInputWithoutLoss,
                "起動直後で何も編集していないのに、破棄の確認が出る判定になっている");
        }

        [TestMethod]
        public void AfterEditing_NeedsConfirmation()
        {
            var vm = new MainWindowViewModel();
            Assert.IsTrue(vm.CanDiscardInputWithoutLoss);

            // SaveUndoState は DataGrid のセル確定も含む全編集の集約点
            vm.SaveUndoState("テスト編集");

            Assert.IsFalse(vm.CanDiscardInputWithoutLoss,
                "編集したのに、破棄の確認が出ない判定になっている");
        }

        [TestMethod]
        public void AfterMarkingUntouched_NeedsNoConfirmationAgain()
        {
            var vm = new MainWindowViewModel();
            vm.SaveUndoState("テスト編集");
            Assert.IsFalse(vm.CanDiscardInputWithoutLoss);

            // 読み込み・新規作成・計算例ロードの直後に呼ばれる
            vm.MarkProjectUntouched();

            Assert.IsTrue(vm.CanDiscardInputWithoutLoss,
                "読み込み直後なのに、破棄の確認が出る判定のままになっている");
        }

        /// <summary>
        /// 解析結果を持っているときは、編集していなくても確認する。
        /// 解析には時間がかかるので、黙って捨ててはいけない。
        /// </summary>
        [TestMethod]
        public void WithAnalysisResults_NeedsConfirmationEvenIfUnedited()
        {
            var vm = new MainWindowViewModel();
            Assert.IsTrue(vm.CanDiscardInputWithoutLoss);

            vm.IsHorizontalAnalysisDone = true;

            Assert.IsFalse(vm.CanDiscardInputWithoutLoss,
                "解析結果があるのに、破棄の確認が出ない判定になっている");
        }
    }
}
