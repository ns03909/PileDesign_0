using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Linq;
using Order = PileDesign.ViewModels.MainWindowViewModel.PileLayoutOrder;

namespace TestProject1
{
    /// <summary>
    /// 杭配置の表の並べ替え (選択した杭を上へ・下へ・ドラッグ)。杭 No. は表の並び順なので、並べ替えると番号が付け直され、
    /// 杭 No. で杭を指している入力 (一般節点・群杭沈下の矩形荷重) もそれに付いていく。
    /// </summary>
    [TestClass, DoNotParallelize]
    public class PileLayoutReorderTests
    {
        private bool _unattended;
        [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
        [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

        private static string Up(string rows, string selected) => string.Concat(Order.MoveSelectedBy(rows.ToList(), selected.Contains, -1));
        private static string Down(string rows, string selected) => string.Concat(Order.MoveSelectedBy(rows.ToList(), selected.Contains, +1));
        private static string To(string rows, string selected, int at) => string.Concat(Order.MoveSelectedTo(rows.ToList(), selected.Contains, at));

        [TestMethod]
        public void MoveUpAndDown_MovesEachSelectedRowOneStep_AndStopsAtTheEnds()
        {
            Assert.AreEqual("ACBDE", Up("ABCDE", "C"));
            Assert.AreEqual("ABDCE", Down("ABCDE", "C"));
            // 離れた行はそれぞれ 1 行
            Assert.AreEqual("BADCE", Up("ABCDE", "BD"));
            Assert.AreEqual("ACBED", Down("ABCDE", "BD"));
            // 続いた行は 1 かたまりで
            Assert.AreEqual("BCADE", Up("ABCDE", "BC"));
            // 端に着いた行は止まり、ほかの行は追い越さない
            Assert.AreEqual("ABCDE", Up("ABCDE", "A"));
            Assert.AreEqual("ABDCE", Up("ABCDE", "AD"));
            Assert.AreEqual("ABCDE", Down("ABCDE", "E"));
            Assert.AreEqual("ACBDE", Down("ABCDE", "BE"));
            Assert.AreEqual("ABCDE", Up("ABCDE", ""));
        }

        [TestMethod]
        public void MoveTo_PutsTheSelectedRowsTogetherBeforeTheDropRow()
        {
            Assert.AreEqual("DABCE", To("ABCDE", "D", 0));
            Assert.AreEqual("BCDAE", To("ABCDE", "A", 4));
            Assert.AreEqual("BCDEA", To("ABCDE", "A", 5));
            Assert.AreEqual("ACBDE", To("ABCDE", "C", 1));
            // 離れた行は元の順のまま 1 かたまりに
            Assert.AreEqual("BCAEDF", To("ABCDEF", "AE", 3));
            // 落とした先が選択した行なら、その次の選択していない行の前へ (動かない)
            Assert.AreEqual("ABCDE", To("ABCDE", "BC", 2));
            // 範囲外は端へ
            Assert.AreEqual("CABDE", To("ABCDE", "C", -3));
            Assert.AreEqual("ABDEC", To("ABCDE", "C", 99));
        }

        [TestMethod]
        public void Reorder_RenumbersPiles_FollowsTheLinkedInputs_AndCanBeUndone()
        {
            using var vm = new MainWindowViewModel();
            var m = vm.CurrentInputModel!;
            m.PileLayoutItems = [];
            m.InputNodes = [];
            m.FoundationBeamInput.Beams = [];
            m.FoundationBeamInput.Nodes = [];
            m.PileGroupSettlement.RectLoads = [];
            for (int i = 1; i <= 4; i++)
                m.PileLayoutItems.Add(new PileLayoutDataItem { No = i, PileNo = i, X = 10 * i });
            m.InputNodes.Add(new InputNode { Type = NodeType.Pile, LinkedPileNo = 3, X = 30 });
            m.PileGroupSettlement.RectLoads.Add(new RectLoad { LinkedPileNo = 3, QA = 33 });
            m.PileGroupSettlement.RectLoads.Add(new RectLoad { LinkedPileNo = 1, QA = 11 });
            vm.MarkProjectReplaced();

            // 杭 3 (X = 30) を選んで 1 行上へ
            m.PileLayoutItems[2].IsSelected = true;
            vm.MovePileLayoutUpCommand.Execute(null);

            CollectionAssert.AreEqual(new[] { 10.0, 30.0, 20.0, 40.0 }, m.PileLayoutItems.Select(p => p.X).ToArray(), "並びが変わっていません");
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, m.PileLayoutItems.Select(p => p.PileNo).ToArray(), "杭 No. が並び順に付け直されていません");
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, m.PileLayoutItems.Select(p => p.No).ToArray());
            Assert.AreEqual(2, m.InputNodes[0].LinkedPileNo, "一般節点が元の杭 (X = 30) を指していません");
            Assert.AreEqual(2, m.PileGroupSettlement.RectLoads[0].LinkedPileNo, "矩形荷重が元の杭 (X = 30) を指していません");
            Assert.AreEqual(1, m.PileGroupSettlement.RectLoads[1].LinkedPileNo, "動かしていない杭の矩形荷重の番号が変わりました");

            // ドラッグ: 杭 1 (X = 10) を末尾へ
            foreach (var p in m.PileLayoutItems) p.IsSelected = p.X == 10;
            vm.MoveSelectedPilesTo(m.PileLayoutItems.Count);
            CollectionAssert.AreEqual(new[] { 30.0, 20.0, 40.0, 10.0 }, m.PileLayoutItems.Select(p => p.X).ToArray());
            Assert.AreEqual(4, m.PileGroupSettlement.RectLoads[1].LinkedPileNo);
            Assert.AreEqual(1, m.PileGroupSettlement.RectLoads[0].LinkedPileNo);

            // 元に戻すと番号と対応も戻る
            vm.UndoCommand.Execute(null);
            vm.UndoCommand.Execute(null);
            var back = vm.CurrentInputModel!;
            CollectionAssert.AreEqual(new[] { 10.0, 20.0, 30.0, 40.0 }, back.PileLayoutItems.Select(p => p.X).ToArray(), "元に戻せません");
            Assert.AreEqual(3, back.InputNodes[0].LinkedPileNo);
            Assert.AreEqual(3, back.PileGroupSettlement.RectLoads[0].LinkedPileNo);
            Assert.AreEqual(1, back.PileGroupSettlement.RectLoads[1].LinkedPileNo);

            // やり直すと並び・番号・対応がもう一度入る
            vm.RedoCommand.Execute(null);
            vm.RedoCommand.Execute(null);
            var again = vm.CurrentInputModel!;
            CollectionAssert.AreEqual(new[] { 30.0, 20.0, 40.0, 10.0 }, again.PileLayoutItems.Select(p => p.X).ToArray(), "やり直せません");
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, again.PileLayoutItems.Select(p => p.PileNo).ToArray());
            Assert.AreEqual(1, again.InputNodes[0].LinkedPileNo);
            Assert.AreEqual(1, again.PileGroupSettlement.RectLoads[0].LinkedPileNo);
            Assert.AreEqual(4, again.PileGroupSettlement.RectLoads[1].LinkedPileNo);
        }

        /// <summary>
        /// 並べ替えは一覧の Move を繰り返すが、画面に知らせるのは終わったときの 1 回だけ (途中は IsReorderingPileLayout が立つ)。
        /// 以前は Move のたびに 3 つの表の選択を作り直し、描き直し、杭要素分割の前なら土層-杭セットまで作り直していた。
        /// </summary>
        [TestMethod]
        public void Reordering_NotifiesTheScreenOnce_AfterAllMoves()
        {
            using var vm = new MainWindowViewModel();
            var m = vm.CurrentInputModel!;
            m.PileLayoutItems = [];
            for (int i = 1; i <= 5; i++) m.PileLayoutItems.Add(new PileLayoutDataItem { No = i, PileNo = i, X = i });
            vm.MarkProjectReplaced();
            int moves = 0, movesWhileFlagged = 0, notified = 0;
            m.PileLayoutItems.CollectionChanged += (_, e) =>
            {
                if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Move) return;
                moves++;
                if (vm.IsReorderingPileLayout) movesWhileFlagged++;
            };
            vm.PileLayoutReordered += (_, _) =>
            {
                notified++;
                Assert.IsFalse(vm.IsReorderingPileLayout, "知らせたときに並べ替えの途中の印が残っています");
            };

            vm.ReorderPileLayout(m.PileLayoutItems.Reverse().ToList());

            Assert.IsTrue(moves >= 2, "(前提) Move を繰り返した");
            Assert.AreEqual(moves, movesWhileFlagged, "Move の途中に並べ替えの印が立っていません (画面が Move のたびに描き直します)");
            Assert.AreEqual(1, notified, "並べ替えの終わりを 1 回だけ知らせていません");
        }

        /// <summary>
        /// 並べ替えの前に表の入力中の値を確定し、確定できなければ並べ替えない (ボタン・Alt+↑/↓・ドラッグのどれも同じ入口)。
        /// 列見出し・スクロールバーなど行でないところに落としても末尾へは動かさない。画面の操作なのでコードで確かめる。
        /// </summary>
        [TestMethod]
        public void TheScreenCommitsEditsFirst_AndRejectsDropsOutsideTheRows()
        {
            string code = TestSource.Read("Graphics_r1", "Views", "MainWindow.PileLayoutReorder.cs");
            string commit = TestSource.MethodBody(code, "private bool CommitPileTablesBeforeReorder(");
            StringAssert.Contains(commit, "grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true)",
                "入力中の値の確定の成否を見ていません");
            StringAssert.Contains(TestSource.MethodBody(code, "private void MoveSelectedPileRows("), "if (!CommitPileTablesBeforeReorder()) return;");
            StringAssert.Contains(TestSource.MethodBody(code, "private void DataGridPileLayout_ReorderMouseMove("), "if (!CommitPileTablesBeforeReorder()) return;");
            StringAssert.Contains(TestSource.MethodBody(code, "private void DataGridPileLayout_ReorderKeyDown("), "MoveSelectedPileRows(");
            string xaml = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");
            StringAssert.Contains(xaml, "Click=\"MovePileRowUp_Click\"", "上へのボタンが確定を通る入口につながっていません");
            StringAssert.Contains(xaml, "Click=\"MovePileRowDown_Click\"");

            string drop = TestSource.MethodBody(code, "private int DropIndexAt(");
            StringAssert.Contains(drop, "DataGridColumnHeader", "列見出しへのドロップを断っていません");
            StringAssert.Contains(drop, "ScrollBar", "スクロールバーへのドロップを断っていません");
            StringAssert.Contains(drop, "return -1;");
            StringAssert.Contains(TestSource.MethodBody(code, "private void DataGridPileLayout_ReorderDrop("), "if (index < 0");
        }

        [TestMethod]
        public void MovingWithoutASelection_SaysWhy_AndChangesNothing()
        {
            using var vm = new MainWindowViewModel();
            var m = vm.CurrentInputModel!;
            m.PileLayoutItems = [new PileLayoutDataItem { No = 1, PileNo = 1, X = 1 }, new PileLayoutDataItem { No = 2, PileNo = 2, X = 2 }];
            vm.MarkProjectReplaced();
            int version = vm.InputEditVersion;

            vm.MovePileLayoutDownCommand.Execute(null);
            StringAssert.Contains(vm.StatusMessage, "選んで");
            m.PileLayoutItems[1].IsSelected = true;
            vm.MovePileLayoutDownCommand.Execute(null);
            StringAssert.Contains(vm.StatusMessage, "末尾");
            Assert.AreEqual(version, vm.InputEditVersion, "何も動かないのに編集として数えています");
            CollectionAssert.AreEqual(new[] { 1.0, 2.0 }, m.PileLayoutItems.Select(p => p.X).ToArray());
        }
    }
}
