using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 杭断面ウィンドウの Undo が、<b>杭体セグメントが持つ実体</b>を戻すこと。
    ///
    /// この画面は <c>PileBodySegment.PileSection</c> の実体をそのまま編集する
    /// (OK は閉じるだけ、キャンセルが <c>RestoreFrom</c> で戻す)。
    /// ところが Undo / Redo は <c>PileSection = state.DeepCopy()</c> と
    /// <b>オブジェクトごと差し替えて</b>いた。差し替えると ViewModel の指す先が
    /// セグメントの実体から外れ、
    ///
    /// <list type="number">
    /// <item>画面には戻った値が出るが、<b>実体は編集後のまま</b>で解析も計算書もそれを使う</item>
    /// <item>そのあとキャンセルを押しても、戻されるのは<b>外れた複製</b>なので
    ///       <b>キャンセルも効かなくなる</b></item>
    /// </list>
    ///
    /// どちらも黙って起きる。杭断面は N-M 曲線 = 検定結果に直結するので影響が大きい。
    /// キャンセルと同じ <c>RestoreFrom</c> (中身だけ写す) に揃えた。
    /// </summary>
    [TestClass]
    public class PileSectionUndoTests
    {
        [TestMethod]
        public void Undo_RestoresTheSectionTheSegmentHolds()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }

            var segment = model.PileBodies?.FirstOrDefault()?.PileBodySegments?.FirstOrDefault();
            Assert.IsNotNull(segment, "杭体セグメントがありません");
            var live = segment!.PileSection;
            Assert.IsNotNull(live, "セグメントが断面を持っていません");

            var mainVm = new MainWindowViewModel { CurrentInputModel = model };
            var vm = new PileSectionViewModel(mainVm, live!, 1, 1);

            double before = live!.MainBarDr;
            Assert.IsTrue(before > 0, $"主筋配置直径が 0 です ({before})");

            // 画面は編集の直前に控えを取る (コードビハインドの PreviewTextInput ほか)
            vm._undoManager.SaveState(vm.PileSection.DeepCopy());

            // 編集
            vm.PileSection.MainBarDr = before - 50.0;
            Assert.AreEqual(before - 50.0, segment.PileSection.MainBarDr, 1e-9,
                "編集が実体に届いていません (揺らせていない)");

            // Ctrl+Z
            vm.UndoCommand.Execute(null);

            Assert.AreSame(segment.PileSection, vm.PileSection,
                "Undo で ViewModel がセグメントの実体から外れました。"
                + "画面は戻った値を出しますが、解析と計算書は編集後の値を使い、"
                + "そのあとキャンセルを押しても効きません");

            Assert.AreEqual(before, segment.PileSection.MainBarDr, 1e-9,
                "Undo で実体の主筋配置直径が戻っていません");
        }

        /// <summary>
        /// Undo のあとも、入力がモデルに届くこと。
        ///
        /// 差し替えていると ViewModel は外れた複製を指すので、
        /// <b>Ctrl+Z のあとに打った値がどこにも効かなくなる</b>
        /// (画面には出るが、解析・計算書・保存はどれも古い実体を見る)。
        /// </summary>
        [TestMethod]
        public void EditsStillReachTheModel_AfterAnUndo()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }

            var segment = model.PileBodies?.FirstOrDefault()?.PileBodySegments?.FirstOrDefault();
            Assert.IsNotNull(segment, "杭体セグメントがありません");
            var live = segment!.PileSection;

            var mainVm = new MainWindowViewModel { CurrentInputModel = model };
            var vm = new PileSectionViewModel(mainVm, live!, 1, 1);

            double opened = live!.MainBarDr;

            // 一度 Ctrl+Z を通す
            vm._undoManager.SaveState(vm.PileSection.DeepCopy());
            vm.UndoCommand.Execute(null);

            // そのあとに編集する
            double edited = opened - 100.0;
            vm.PileSection.MainBarDr = edited;

            Assert.AreEqual(edited, segment.PileSection.MainBarDr, 1e-9,
                "Ctrl+Z のあとに打った値がモデルへ届いていません。"
                + "Undo が断面をオブジェクトごと差し替えて、"
                + "杭体セグメントが持つ実体から外れています");
        }

        /// <summary>
        /// Undo / Redo が<b>オブジェクトを差し替えていない</b>こと。
        /// キャンセルと同じ「中身だけ写す」に揃えておかないと、また外れる。
        /// </summary>
        [TestMethod]
        public void UndoAndRedo_DoNotSwapTheSectionObject()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "PileSectionViewModel.cs");

            // Undo / Redo は戻す処理を共有していること (片方だけ直すのを防ぐ)
            foreach (string which in new[] { "public void Undo()", "public void Redo()" })
            {
                StringAssert.Contains(Regex.Replace(TestSource.MethodBody(src, which), "//.*", ""),
                    "ApplyUndoState()", $"{which} が戻す処理を共有していません");
            }

            string apply = Regex.Replace(
                TestSource.MethodBody(src, "private void ApplyUndoState()"), "//.*", "");

            Assert.IsFalse(Regex.IsMatch(apply, @"PileSection\s*=\s*state"),
                "断面をオブジェクトごと差し替えています。"
                + "杭体セグメントが持つ実体から外れ、Undo もキャンセルも効かなくなります");

            StringAssert.Contains(apply, "RestoreFrom",
                "中身だけを写す形になっていません (キャンセルと同じ RestoreFrom に揃えること)");
        }
    }
}
