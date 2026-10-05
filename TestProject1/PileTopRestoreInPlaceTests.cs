using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 杭頭部ウィンドウの「元に戻す」と「キャンセル」は、画面が編集している実体へ中身を写すこと。
    ///
    /// この画面は杭体ウィンドウの作業用の写しの杭頭部を受け取って直接書き換える。以前は戻した状態を
    /// 別の実体として<b>確定済みの入力の</b>杭頭部へ差し替えていたので、
    ///   ・編集している実体には編集が残り、杭体ウィンドウの OK でキャンセルしたはずの編集が確定した
    ///   ・確定済みの入力の杭頭部は、杭体ウィンドウをキャンセルしても差し替わったまま残った
    /// </summary>
    [TestClass]
    public class PileTopRestoreInPlaceTests
    {
        private static (PileTopViewModel vm, PileTop editing, PileTop committed, MainWindowViewModel main) Open()
        {
            // 既定の入力 (杭体 1 つ) から始める
            var main = new MainWindowViewModel();
            var committed = main.CurrentInputModel!.PileBodies[0].PileTop;
            committed.PileCapFc = 24;
            // 杭体ウィンドウの作業用の写し
            var editing = committed.DeepCopy();
            var section = new PileSection();
            var vm = new PileTopViewModel(main, editing, 1, "場所打ち鉄筋コンクリート杭", "定着筋方式", "場所打ちコンクリート杭", section);
            return (vm, editing, committed, main);
        }

        [TestMethod]
        public void Cancel_RestoresTheInstanceBeingEdited_AndLeavesTheCommittedInputAlone()
        {
            var (vm, editing, committed, main) = Open();
            editing.PileCapFc = 30;
            vm.CancelCommand.Execute(null);

            Assert.AreSame(editing, vm.PileTop, "画面の杭頭部が別の実体に差し替わっています");
            Assert.AreEqual(24.0, editing.PileCapFc, "キャンセルしても編集している実体に編集が残っています (杭体ウィンドウの OK で確定してしまう)");
            Assert.AreSame(committed, main.CurrentInputModel!.PileBodies[0].PileTop, "確定済みの入力の杭頭部を差し替えています");
        }

        [TestMethod]
        public void Undo_RestoresTheInstanceBeingEdited()
        {
            var (vm, editing, _, _) = Open();
            vm._undoManager.SaveState(editing.DeepCopy());
            editing.PileCapFc = 30;
            vm.UndoCommand.Execute(null);
            Assert.AreSame(editing, vm.PileTop);
            Assert.AreEqual(24.0, editing.PileCapFc, "Ctrl+Z で編集している実体が戻っていません");
        }
    }
}
