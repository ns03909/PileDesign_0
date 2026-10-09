using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-09 の見直しで直した、メイン画面の編集の入口の取りこぼし (履歴・再解析が必要の印・杭要素分割)。
    /// 入口ごとに別の書き方をしていたので、同じ編集でも入口によって履歴に残ったり残らなかったりした。
    /// </summary>
    [TestClass]
    public class MainScreenEditFixes20261009Tests
    {
        private bool _unattended;
        [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
        [TestCleanup]
        public void Cleanup()
        {
            MessageService.IsUnattended = _unattended;
            MessageService.UnattendedAnswer = null;
        }

        private static MainWindowViewModel Create()
        {
            var vm = new MainWindowViewModel();
            vm.CurrentInputModel = new InputModel
            {
                PileLayoutItems = [],
                InputNodes = [],
                GridXItems = [],
                GridYItems = [],
                LoadCasesInput = vm.CurrentInputModel!.LoadCasesInput,
                PileGroupSettlement = new PileGroupSettlement { RectLoads = [], SettlementSoilLayers = [] },
                FoundationBeamInput = new FoundationBeamInput { Beams = [], Nodes = [] }
            };
            vm.MarkProjectReplaced();
            return vm;
        }

        private static PileLayoutDataItem Pile(int no, double x) => new() { No = no, PileNo = no, X = x, PileBodyNo = 1, GroundNo = 1 };

        /// <summary>
        /// 杭の削除は、どの入口でも前後を 1 手として積み、消えた杭につながる梁要素を消し、残った杭を指す番号を付け直す。
        /// 以前の Delete キーと行の削除ボタンは、矩形荷重の対応杭が 1 つ後ろの別の杭を指したまま残り、Ctrl+Y で削除後に戻れなかった。
        /// </summary>
        [TestMethod]
        public void DeletingAPile_RelinksTheRest_RemovesAttachedBeams_AndSupportsUndoRedo()
        {
            using var vm = Create();
            var input = vm.CurrentInputModel!;
            var p1 = Pile(1, 0); var p2 = Pile(2, 3); var p3 = Pile(3, 6);
            input.PileLayoutItems = [p1, p2, p3];
            input.FoundationBeamInput.Beams.Add(new FoundationBeam
            {
                NodeI_Type = NodeReferenceType.PileLayout, NodeI_Id = p1.UniqueId,
                NodeJ_Type = NodeReferenceType.PileLayout, NodeJ_Id = p2.UniqueId,
            });
            input.FoundationBeamInput.Beams.Add(new FoundationBeam
            {
                NodeI_Type = NodeReferenceType.PileLayout, NodeI_Id = p1.UniqueId,
                NodeJ_Type = NodeReferenceType.PileLayout, NodeJ_Id = p3.UniqueId,
            });
            input.PileGroupSettlement.RectLoads.Add(new RectLoad { LinkedPileNo = 3, X1 = 5, X2 = 7, Y1 = -1, Y2 = 1 });

            Assert.IsTrue(vm.DeleteInputItems([p2], null, null, "杭配置 削除"));

            Assert.AreEqual(2, input.PileLayoutItems.Count);
            Assert.AreEqual(1, input.FoundationBeamInput.Beams.Count, "消えた杭につながる梁要素が残っています");
            Assert.AreEqual(2, input.PileGroupSettlement.RectLoads[0].LinkedPileNo,
                "杭 3 が 2 番になったのに、矩形荷重の対応杭を付け直していません");

            vm.UndoCommand.Execute(null);
            Assert.AreEqual(3, vm.CurrentInputModel!.PileLayoutItems.Count, "Ctrl+Z で削除前に戻りません");
            Assert.AreEqual(2, vm.CurrentInputModel!.FoundationBeamInput.Beams.Count);
            vm.RedoCommand.Execute(null);
            Assert.AreEqual(2, vm.CurrentInputModel!.PileLayoutItems.Count, "Ctrl+Y で削除後に戻りません (変更後を積んでいない)");
        }

        /// <summary>
        /// 杭配置表の杭体番号・地盤番号のコンボは、プロパティパネルと同じ流れで書く:
        /// 杭要素分割の取り消しを確かめ (断れば書かない)、前後を履歴に積み、再解析が必要の印を付ける。
        /// </summary>
        [TestMethod]
        public void PileLayoutCombos_GoThroughTheSameFlowAsThePropertyPanel()
        {
            using var vm = Create();
            var pile = Pile(1, 0);
            vm.CurrentInputModel!.PileLayoutItems = [pile];
            vm.IsElementSplit = true;

            // 断れば書かない (分割済みのまま、古い土層-杭セットで解くことを防ぐ)
            Assert.IsFalse(vm.TrySetPileLayoutReference(pile, isPileBody: true, value: 2));
            Assert.AreEqual(1, pile.PileBodyNo);
            Assert.IsTrue(vm.IsElementSplit);

            MessageService.UnattendedAnswer = (_, _) => MessageBoxResult.Yes;
            int version = vm.InputEditVersion;
            Assert.IsTrue(vm.TrySetPileLayoutReference(pile, isPileBody: false, value: 2));
            Assert.AreEqual(2, pile.GroundNo);
            Assert.IsFalse(vm.IsElementSplit, "杭要素分割を取り消していません (土層-杭セットが作り直されない)");
            Assert.IsTrue(vm.InputEditVersion > version, "編集として記録していません");
            Assert.IsTrue(vm.HasUnsavedWork);

            vm.UndoCommand.Execute(null);
            Assert.AreEqual(1, vm.CurrentInputModel!.PileLayoutItems[0].GroundNo, "Ctrl+Z で戻りません");
            vm.RedoCommand.Execute(null);
            Assert.AreEqual(2, vm.CurrentInputModel!.PileLayoutItems[0].GroundNo, "Ctrl+Y で戻りません");
        }

        /// <summary>
        /// 元に戻す・やり直しで差し替える入力 (複製) が、杭体番号・地盤番号のコンボの選択肢を持っている。
        /// 以前は空で、画面が結んだ時点で選択肢に無い値になり、杭配置表の杭体番号が空欄に見えた (実機で確認)。
        /// </summary>
        [TestMethod]
        public void UndoCopies_HaveTheNumberListsForTheCombos()
        {
            using var vm = Create();
            vm.CurrentInputModel!.PileBodies = [new PileBodyInput(), new PileBodyInput(), new PileBodyInput()];
            vm.CurrentInputModel!.UpdateCountLists();
            var copy = vm.CurrentInputModel!.DeepCopy();
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, copy.PileBodiesCountList.ToArray(), "複製に杭体番号の選択肢がありません");
            Assert.IsTrue(copy.GroundsInputCountList.Count >= 1);
        }

        /// <summary>
        /// Undo・Redo で「杭要素分割済み」の印もその状態のものに戻る。
        /// 以前は印が戻らず、分割を Ctrl+Z で戻しても「分割済み」のままで、未分割の注意が出ずに粗い要素で解析できた。
        /// </summary>
        [TestMethod]
        public void UndoRedo_RestoresTheElementSplitFlag()
        {
            using var vm = Create();
            vm.CurrentInputModel!.PileLayoutItems = [Pile(1, 0)];
            vm.IsElementSplit = false;
            var before = vm.CaptureInputEdit();
            vm.CurrentInputModel!.PileLayoutItems[0].X = 1.0;
            vm.IsElementSplit = true;
            vm.CompleteInputEdit(before, "杭要素分割");

            vm.UndoCommand.Execute(null);
            Assert.IsFalse(vm.IsElementSplit, "分割の前に戻したのに「分割済み」のままです");
            vm.RedoCommand.Execute(null);
            Assert.IsTrue(vm.IsElementSplit, "分割の後に戻したのに「未分割」です");
        }

        /// <summary>
        /// 杭要素分割の確認は、分割だけを取り消し、解析結果は消さない (プロパティパネルの編集と同じ扱い)。
        /// 軸力・前後杭の表は分割に関わらないので、確認自体を出さない。
        /// </summary>
        [TestMethod]
        public void ElementSplitConfirmation_KeepsResults_AndIsNotAskedForAxialOrFrontBack()
        {
            using var vm = Create();
            vm.IsElementSplit = true;
            MessageService.UnattendedAnswer = (_, _) => MessageBoxResult.Yes;
            Assert.IsTrue(vm.CheckAndResetElementSplit("地盤"));
            Assert.IsFalse(vm.IsElementSplit);

            string body = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.DataGridEditing.cs");
            var m = Regex.Match(body, @"public bool CheckAndResetElementSplit\(string text\)\s*=>\s*ConfirmDiscardInvalidatedByInputChange\(true");
            Assert.IsTrue(m.Success, "杭要素分割の確認が、結果を残す共通の確認を通っていません");

            string main = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml.cs");
            Assert.IsFalse(main.Contains("CheckAndResetElementSplit(\"杭軸力\")"), "軸力の表に入っただけで杭要素分割を取り消そうとしています");
            Assert.IsFalse(main.Contains("CheckAndResetElementSplit(\"前後方杭\")"), "前後杭の表に入っただけで杭要素分割を取り消そうとしています");
        }

        /// <summary>
        /// 表の Ctrl+V / Delete は、杭配置表と軸力表以外でも履歴と印を通る。杭の形が変わる表は杭要素分割を取り消す。
        /// </summary>
        [TestMethod]
        public void GridPaste_IsRecorded_AndDiscardsTheSplitOnlyForGeometry()
        {
            using var vm = Create();
            vm.IsElementSplit = true;
            int version = vm.InputEditVersion;
            vm.CompleteGridPaste("前後杭 貼り付け", changesPileGeometry: false);
            Assert.IsTrue(vm.IsElementSplit, "形の変わらない表の貼り付けで杭要素分割を取り消しています");
            Assert.IsTrue(vm.InputEditVersion > version);
            vm.CompleteGridPaste("根入部 貼り付け", changesPileGeometry: true);
            Assert.IsFalse(vm.IsElementSplit, "根入部の貼り付けで杭要素分割を取り消していません");

            string xaml = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");
            foreach (var grid in new[] { "DataGridInputNodes", "DataGridBeamMaterials", "DataGridBeamSections", "DataGridFoundationBeams", "DataGridIsFrontPile", "DataGridEmbedment" })
                StringAssert.Contains(xaml, $"x:Name=\"{grid}\" PasteCompleted=\"DataGridInput_PasteCompleted\"", $"{grid} の貼り付けが履歴を通りません");
        }

        /// <summary>
        /// 直接書くコンボ (杭配置表の杭体・地盤、梁要素表の節点・材料・断面) と作図での梁要素の追加が、履歴を通ること。
        /// </summary>
        [TestMethod]
        public void DirectWritingControls_GoThroughTheHistory()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");
            StringAssert.Contains(xaml, "SelectedItem=\"{Binding PileBodyNo, Mode=OneWay}\"");
            StringAssert.Contains(xaml, "SelectedItem=\"{Binding GroundNo, Mode=OneWay}\"");
            int start = xaml.IndexOf("x:Name=\"DataGridFoundationBeams\"");
            int end = xaml.IndexOf("</common:EnhancedDataGrid>", start);
            Assert.AreEqual(4, Regex.Matches(xaml[start..end], "SelectionChanged=\"FoundationBeamCombo_SelectionChanged\"").Count,
                "梁要素の表のコンボ 4 つが履歴を通りません");

            string drawing = TestSource.Read("Graphics_r1", "Views", "MainWindow.FoundationBeamEditing.cs");
            StringAssert.Contains(drawing, "vm.CompleteInputEdit(before, \"梁要素 追加\")");
            Assert.IsFalse(drawing.Contains("Undo登録（今後実装）"), "作図の追加・削除に履歴を通らないものが残っています");

            string handler = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.cs");
            StringAssert.Contains(handler, "var before = CaptureInputEdit();\r\n\r\n                // バインディングソースの更新".Replace("\r\n", handler.Contains("\r\n") ? "\r\n" : "\n"),
                "表のセルの確定が、変更の前後を積んでいません");
        }
    }
}
