using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common.Undo;
using PileDesign.ViewModels;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// メイン画面の Undo / Redo が成り立っていること。
    ///
    /// <para>ここは入力モデルを<b>まるごと</b>控えて差し替える方式で、
    /// 戻したあとに <c>AttachViewModel</c> と <c>UpdatePropertyPanel</c> で
    /// 配線を張り直す。各ウィンドウのように「実体の一部だけを掴む」形ではないので、
    /// 掴んだ先が外れる類の問題は起きない。</para>
    ///
    /// <para><b>見張るのは 2 つ。</b>
    /// 往復して値が戻ること、そして<b>戻す最中に控えが積まれないこと</b>。
    /// <c>UndoManager.SaveState</c> は現在位置より後ろを切り捨てるので、
    /// 戻したあとの再描画や通知が控えを積むと、その場で Redo が消える
    /// （荷重条件・杭要素分割の各ウィンドウで実際に起きていた形）。</para>
    ///
    /// <para><see cref="UndoManagerTests"/> は仕組みそのものを見る。こちらは
    /// <b>メイン画面がその仕組みを正しく使えているか</b>を見る。</para>
    /// </summary>
    [TestClass]
    public class MainWindowUndoTests
    {
        [TestMethod]
        public void Undo_ThenRedo_RoundTripsTheInput()
        {
            var vm = new MainWindowViewModel();
            var fundamental = vm.CurrentInputModel?.FundamentalInput;
            Assert.IsNotNull(fundamental, "既定モデルに基本条件がありません");

            double before = fundamental!.ReferenceAltitude;
            double edited = before + 3.5;

            vm.SaveUndoState("初期");
            fundamental.ReferenceAltitude = edited;
            vm.SaveUndoState("編集");

            vm.UndoCommand.Execute(null);
            Assert.AreEqual(before, vm.CurrentInputModel!.FundamentalInput!.ReferenceAltitude, 1e-9,
                "Undo で基準標高が戻っていません");

            vm.RedoCommand.Execute(null);
            Assert.AreEqual(edited, vm.CurrentInputModel!.FundamentalInput!.ReferenceAltitude, 1e-9,
                "Redo で基準標高が戻ってきません");
        }

        /// <summary>
        /// 戻す最中に控えが積まれないこと。積むと履歴の後ろが切り捨てられ、
        /// Redo が消えて 2 回目以降の Ctrl+Z も効かなくなる。
        /// </summary>
        [TestMethod]
        public void Undo_DoesNotGrowTheHistory()
        {
            var vm = new MainWindowViewModel();
            var fundamental = vm.CurrentInputModel?.FundamentalInput;
            Assert.IsNotNull(fundamental, "既定モデルに基本条件がありません");

            vm.SaveUndoState("初期");
            fundamental!.ReferenceAltitude += 3.5;
            vm.SaveUndoState("編集");

            var manager = Manager(vm);
            int before = manager.History.Count;

            vm.UndoCommand.Execute(null);

            Assert.AreEqual(before, manager.History.Count,
                $"Undo で履歴が {before} → {manager.History.Count} 段に増えました。"
                + "戻す最中に控えを積んでいます (SaveState は現在位置より後ろを切り捨てるので "
                + "Redo が消えます)");

            Assert.IsTrue(manager.CanRedo, "Undo した直後に Redo が消えています");
        }

        private static UndoManager Manager(MainWindowViewModel vm)
        {
            var f = typeof(MainWindowViewModel).GetField("_undoManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "_undoManager が見つかりません (名前が変わった?)");
            var m = f!.GetValue(vm) as UndoManager;
            Assert.IsNotNull(m, "UndoManager を取り出せません");
            return m!;
        }
    }
}
