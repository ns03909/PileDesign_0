using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 杭要素分割ウィンドウの Undo / Redo が成り立っていること。
    ///
    /// <para><b>1. Undo が履歴を壊していた。</b>
    /// <c>Undo()</c> は戻したあと <c>OnZDataItemsChanged()</c> を呼ぶが、
    /// その中で<b>また控えを積んでいる</b>。<c>UndoManager.SaveState</c> は
    /// 現在位置より後ろを切り捨てるので、<b>Undo した瞬間に Redo 側が消える</b>。
    /// さらに履歴が 1 回の Undo ごとに 2 段伸び、位置が末尾へ戻るため
    /// <b>2 回目以降の Ctrl+Z が効かない</b>。
    /// 抑止フラグ (<c>_suppressUndoSave</c>) は用意されていたのに、
    /// 一括更新の 2 か所でしか使われていなかった。</para>
    ///
    /// <para><b>2. 1 つの履歴に 2 つの形が混ざっていた。</b>
    /// 土層-杭セット (<c>List&lt;SoilPile&gt;</c>) と根入れ部の Z
    /// (<c>List&lt;EmbedmentZDataItem&gt;</c>) を同じ履歴へ積んでいるのに、
    /// 読む側は前者しか見ていない。根入れ部をいじってから Ctrl+Z すると
    /// <b>何も起きないまま履歴の位置だけ進む</b>。根入れ部は Undo で戻らなかった。</para>
    ///
    /// どちらも「控えの形と、それを戻す場所が噛み合っていない」同じ根なので、
    /// 控えを 1 つの器にまとめ、戻す間は控えを取らないようにした。
    /// </summary>
    [TestClass]
    public class ElementDivisionUndoTests
    {
        [TestMethod]
        public void Undo_DoesNotDestroyRedo()
        {
            RunWithViewModel((vm, model) =>
            {
                // 水平地盤反力の行は画面を開いたときに作られるので、ここで作る
                var pile = vm.SoilPiles[vm.SelectedSoilPileNo - 1];
                vm.SelectedZDataItems = new System.Collections.ObjectModel.ObservableCollection<PileZDataItem>(
                    pile.ZDataItems.Select(i => i.DeepCopy()));
                vm.SetHorizontalSoilReaction();

                var layer = vm.SelectedHorizontalSoilReactions?.FirstOrDefault(
                    i => !string.IsNullOrEmpty(i.Name));
                Assert.IsNotNull(layer,
                    "水平地盤反力係数の行が作られていません (網が空振りします)");
                string name = layer.Name;

                // 手入力で kh0 を上書き (控えが 1 段積まれる)
                vm.ApplyKh0Edit(layer, 12345.0);
                Assert.AreEqual(12345.0, pile.GetKh0Override(name) ?? -1, 1e-9,
                    "kh0 の手入力が効いていません (揺らせていない)");

                vm.UndoCommand.Execute(null);

                var undone = vm.SoilPiles[vm.SelectedSoilPileNo - 1];
                Assert.IsNull(undone.GetKh0Override(name),
                    "Undo で kh0 の手入力が消えていません (Undo そのものが効いていない)");

                vm.RedoCommand.Execute(null);

                var pileAfter = vm.SoilPiles[vm.SelectedSoilPileNo - 1];
                Assert.AreEqual(12345.0, pileAfter.GetKh0Override(name) ?? -1, 1e-9,
                    "Redo で kh0 の手入力が戻ってきません。"
                    + "戻す処理の中で控えを積み直しているため、Undo した瞬間に履歴の後ろが"
                    + "切り捨てられています");
            });
        }

        /// <summary>
        /// この画面が履歴へ積む控えの形が<b>1 つだけ</b>であること。
        ///
        /// 以前は土層-杭セットと根入れ部の Z を同じ履歴へ別の形で積んでいて、
        /// 戻す側は前者しか見ていなかった。根入れ部をいじってから Ctrl+Z すると
        /// <b>何も起きないまま履歴の位置だけ進む</b>。
        ///
        /// (根入れ部の操作は行番号の振り直しでウィンドウ実体を要求するため、
        ///  ここは実際に動かさずに形だけを見る)
        /// </summary>
        [TestMethod]
        public void TheHistory_HoldsOneShapeOfState()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "ElementDivisionViewModel.cs");
            src = System.Text.RegularExpressions.Regex.Replace(src, @"//[^
]*", "");

            var shapes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(src, @"_undoManager\.SaveState\(\s*([^;]*?)\s*\);"))
            {
                shapes.Add(System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim());
            }

            TestSource.AssertScanned(shapes.Count, 1, "履歴へ積んでいる控えの形");

            Assert.AreEqual(1, shapes.Count,
                $"1 つの履歴に {shapes.Count} 種類の形を積んでいます。"
                + "戻す側は 1 つの形しか見られないので、残りは黙って戻りません:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", shapes));
        }

        /// <summary>
        /// 戻す処理の中で控えを積まないこと。積むと履歴の後ろが切り捨てられる。
        /// </summary>
        [TestMethod]
        public void TheRestorePath_DoesNotRecordNewHistory()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "ElementDivisionViewModel.cs");

            // Undo / Redo は戻す処理を共有していること (片方だけ直すのを防ぐ)
            foreach (string which in new[] { "private void Undo()", "private void Redo()" })
            {
                StringAssert.Contains(Strip(TestSource.MethodBody(src, which)), "ApplyUndoState()",
                    $"{which} が戻す処理を共有していません");
            }

            // 戻している間は控えを取らないこと
            string apply = Strip(TestSource.MethodBody(src, "private void ApplyUndoState()"));
            StringAssert.Contains(apply, "_suppressUndoSave = true",
                "戻している間の控えを抑止していません。"
                + "戻したあとの再計算が控えを積み、SaveState が履歴の後ろを切り捨てるので "
                + "Redo が消え、2 回目以降の Ctrl+Z も効かなくなります");
            StringAssert.Contains(apply, "finally",
                "抑止フラグを finally で戻していません (途中で例外が出ると以後の控えが取れません)");
        }

        private static string Strip(string source)
            => System.Text.RegularExpressions.Regex.Replace(source, "//.*", "");

        private static void RunWithViewModel(Action<ElementDivisionViewModel, InputModel> check)
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }

            var soilPiles = model.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0)
            {
                Assert.Inconclusive("要素分割された地盤杭セットがありません");
                return;
            }

            // ViewModel の生成は WPF のコントロールを作るので STA が要る
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var mainVm = new MainWindowViewModel { CurrentInputModel = model };
                var vm = new ElementDivisionViewModel(mainVm);
                check(vm, model);
            }, out bool timedOut);

            if (timedOut) { Assert.Inconclusive("STA スレッドで 60 秒以内に終わりませんでした"); return; }
            if (captured is AssertFailedException || captured is AssertInconclusiveException)
                throw captured;
            if (captured != null)
                Assert.Fail($"{captured.GetType().Name}: {captured.Message}"
                    + Environment.NewLine + captured.StackTrace);
        }
    }
}
