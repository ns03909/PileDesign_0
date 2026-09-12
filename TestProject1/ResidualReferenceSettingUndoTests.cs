using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 基本設定で「収束判定の基準値」を変えたあとの<b>戻し方</b>。
    ///
    /// <para>この画面は実体 (<c>InputModel.FundamentalInput</c>) を直接編集し、Ctrl+Z と
    /// キャンセルの 2 通りで戻せる。どちらかを配線し忘れると、選び直したつもりが
    /// <b>戻っていない</b>まま解析へ行く。材料オプションで実際に踏んだ形なので、
    /// 新しい項目もここで固定する (2026-09-12)。</para>
    ///
    /// <para>確認ダイアログは出ない経路である。保存すると水平解析の結果は消さずに
    /// 「再解析が必要」の印が立つだけなので、戻し忘れても画面上は静かに進む。</para>
    /// </summary>
    [TestClass]
    public class ResidualReferenceSettingUndoTests
    {
        /// <summary>選んだ値が入力の実体へ入ること。</summary>
        [TestMethod]
        public void ChoosingAModeWritesItIntoTheInput()
        {
            var mainVm = new MainWindowViewModel();
            var fundamental = mainVm.CurrentInputModel!.FundamentalInput;
            var vm = new FundamentalViewModel(mainVm);

            Assert.AreEqual(ResidualReferenceModes.Default, vm.ResidualReference,
                "画面の初期値が入力の既定と違います");

            vm.ResidualReference = ResidualReferenceMode.InternalForce;

            Assert.AreEqual(ResidualReferenceMode.InternalForce, fundamental.ResidualReference,
                "選んだ基準値が入力の実体に入っていません");
        }

        /// <summary>Ctrl+Z (UndoCommand) で前の選択に戻り、Redo でやり直せること。</summary>
        [TestMethod]
        public void UndoGoesBackToThePreviousMode()
        {
            var mainVm = new MainWindowViewModel();
            var fundamental = mainVm.CurrentInputModel!.FundamentalInput;
            var vm = new FundamentalViewModel(mainVm);

            var before = fundamental.ResidualReference;
            vm.ResidualReference = ResidualReferenceMode.ExternalForce;
            Assert.AreEqual(ResidualReferenceMode.ExternalForce, fundamental.ResidualReference);

            Assert.IsTrue(vm.UndoCommand.CanExecute(null),
                "基準値を変えたのに戻せません (Undo に積んでいない)");
            vm.UndoCommand.Execute(null);

            Assert.AreEqual(before, fundamental.ResidualReference,
                "Ctrl+Z で基準値が前の選択に戻っていません");
            Assert.AreEqual(before, vm.ResidualReference,
                "入力は戻ったのに画面の表示が戻っていません (選び直せなくなる)");

            Assert.IsTrue(vm.RedoCommand.CanExecute(null), "やり直せません");
            vm.RedoCommand.Execute(null);
            Assert.AreEqual(ResidualReferenceMode.ExternalForce, fundamental.ResidualReference,
                "Redo で基準値が戻っていません");
        }

        /// <summary>キャンセルで入力ごと元に戻ること (× で閉じても同じ経路を通る)。</summary>
        [TestMethod]
        public void CancelRestoresTheMode()
        {
            var mainVm = new MainWindowViewModel();
            var vm = new FundamentalViewModel(mainVm);

            var before = mainVm.CurrentInputModel!.FundamentalInput.ResidualReference;
            vm.ResidualReference = ResidualReferenceMode.InternalForce;

            vm.CancelCommand.Execute(null);

            Assert.AreEqual(before, mainVm.CurrentInputModel!.FundamentalInput.ResidualReference,
                "キャンセルしたのに基準値が変わったままです");
            Assert.IsFalse(vm.AppliedChanges, "キャンセルなのに適用済みになっています");
        }
    }
}
