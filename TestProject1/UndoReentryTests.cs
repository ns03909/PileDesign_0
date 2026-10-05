using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 戻し・やり直しの最中に起きた変更を、新しい手として積まないこと。
    ///
    /// プロジェクト情報・基本設定では、戻す処理がモデルの値を書き換えると、画面側の変更通知
    /// (On…Changed) がそれを新しい編集として積み直していた。積み直された手が戻した手の上に乗るので
    /// 次の Ctrl+Z は空振りし、やり直しは「新しい手を積んだので捨てた」やり直しの手を取り出して例外になった。
    /// </summary>
    [TestClass]
    public class UndoReentryTests
    {
        [TestMethod]
        public void ChangesMadeWhileUndoing_AreNotRecordedAsNewEdits()
        {
            var m = new UndoManager();
            int value = 0;
            void Set(int v)
            {
                int old = value;
                value = v;
                // 画面の変更通知が積み直す形
                m.PushAction(() => Set(old), () => Set(v), "値");
            }
            Set(1);
            Set(2);
            Assert.AreEqual(2, m.UndoCount);

            m.Undo();
            Assert.AreEqual(1, value);
            Assert.AreEqual(1, m.UndoCount, "戻す最中の変更が新しい手として積まれています");
            Assert.AreEqual(1, m.RedoCount, "やり直しの手が捨てられています");

            m.Redo();   // 以前は InvalidOperationException (空のスタックから取り出す)
            Assert.AreEqual(2, value);

            m.Undo(); m.Undo();
            Assert.AreEqual(0, value, "2 回目の Ctrl+Z が空振りしています");
        }

        [TestMethod]
        public void ProjectInfo_UndoAndRedoTheProjectName()
        {
            var input = new InputModel { FundamentalInput = new FundamentalInput { ProjectName = "元の名前" } };
            var main = new MainWindowViewModel { CurrentInputModel = input };
            var vm = new ProjectInfoViewModel(main);
            Assert.IsFalse(vm.UndoCommand.CanExecute(null), "開いただけで「元に戻す」の手が積まれています (値の変わらない手)");

            vm.ProjectName = "新しい名前";
            vm.UndoCommand.Execute(null);
            Assert.AreEqual("元の名前", input.FundamentalInput.ProjectName);
            Assert.AreEqual("元の名前", vm.ProjectName);

            vm.RedoCommand.Execute(null);
            Assert.AreEqual("新しい名前", input.FundamentalInput.ProjectName);

            vm.UndoCommand.Execute(null);
            Assert.AreEqual("元の名前", input.FundamentalInput.ProjectName, "やり直したあとの Ctrl+Z が効きません");
        }
    }
}
