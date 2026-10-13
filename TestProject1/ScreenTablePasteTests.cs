using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Data;

namespace TestProject1
{
    /// <summary>
    /// 画面ごとの表への貼り付けが、セルの編集と同じ後処理 (解析への反映・元に戻すの記録) を通ること (2026-10-14 のレビュー)。
    /// 貼り付け (と Delete でのクリア) はセルの編集の確定 (CellEditEnding) を通らないので、確定でだけ後処理をしている表は
    /// 貼り付けで後処理が抜ける。
    /// </summary>
    [TestClass]
    public class ScreenTablePasteTests
    {
        private static void OnSta(Action action)
        {
            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;   // 確認・エラーのダイアログで止まらないように
            try
            {
                var ex = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
                Assert.IsFalse(timedOut, "STA スレッドでの実行が時間内に終わりませんでした");
                if (ex is AssertFailedException or AssertInconclusiveException) throw ex;
                if (ex != null) throw new AssertFailedException(ex.ToString());
            }
            finally { MessageService.IsUnattended = unattended; }
        }

        private static EnhancedDataGrid Grid(System.Collections.IEnumerable rows, object firstRow, params string[] paths)
        {
            var grid = new EnhancedDataGrid
            {
                AutoGenerateColumns = false,
                SelectionUnit = DataGridSelectionUnit.Cell,
                SelectionMode = DataGridSelectionMode.Extended,
            };
            foreach (var p in paths) grid.Columns.Add(new DataGridTextColumn { Header = p, Binding = new Binding(p) });
            grid.ItemsSource = rows;
            for (int i = 0; i < grid.Columns.Count; i++) grid.Columns[i].DisplayIndex = i;
            grid.SelectedCells.Add(new DataGridCellInfo(firstRow, grid.Columns[0]));
            return grid;
        }

        // ── 要素分割画面の kh0 ─────────────────────────────

        private static (ElementDivisionViewModel Vm, SoilPile Pile) Kh0Screen()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) Assert.Inconclusive(error);
            var mainVm = new MainWindowViewModel { CurrentInputModel = model! };
            var vm = new ElementDivisionViewModel(mainVm);
            var pile = vm.SoilPiles[vm.SelectedSoilPileNo - 1];
            vm.SelectedZDataItems = new ObservableCollection<PileZDataItem>(pile.ZDataItems.Select(i => i.DeepCopy()));
            vm.SetHorizontalSoilReaction();
            return (vm, pile);
        }

        /// <summary>
        /// 水平地盤反力の表へ貼った kh0 は、セルの編集と同じく土層単位の手入力として土層-杭セット (解析が読む) に入り、
        /// 元に戻す 1 回で消える。以前は表示用の行の値だけが変わり、表を作り直すと消え、解析には届いていなかった。
        /// </summary>
        [TestMethod]
        public void PastedKh0_ReachesTheAnalysis_AndCanBeUndone()
        {
            OnSta(() =>
            {
                var (vm, pile) = Kh0Screen();
                var rows = vm.SelectedHorizontalSoilReactions;
                var first = rows.First(r => !string.IsNullOrEmpty(r.Name));
                string name = first.Name!;
                Assert.IsNull(pile.GetKh0Override(name), "(前提) 手入力が無い土層");

                Assert.IsTrue(Grid(rows, first, nameof(HorizontalSoilReactionItem.Kh0)).TryPasteText("23456"));
                vm.ApplyKh0FromTable();   // 画面の貼り付け完了 (Horizon_PasteCompleted) と同じ

                Assert.AreEqual(23456.0, pile.GetKh0Override(name) ?? -1, 1e-9, "貼り付けた kh0 が土層-杭セットの手入力に入っていません");
                Assert.IsTrue(vm.SelectedHorizontalSoilReactions.Where(r => r.Name == name).All(r => r.Kh0 == 23456.0 && r.IsKh0Manual),
                    "同じ土層のほかの要素に入っていない、または表を作り直したら消えました");

                vm.UndoCommand.Execute(null);
                Assert.IsNull(vm.SoilPiles[vm.SelectedSoilPileNo - 1].GetKh0Override(name), "元に戻す 1 回で消えません");
            });
            string code = TestSource.Read("Graphics_r1", "Views", "ElementDivisionWindow.xaml.cs");
            StringAssert.Contains(TestSource.MethodBody(code, "private void Horizon_PasteCompleted("), "ApplyKh0FromTable");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "ElementDivisionWindow.xaml"), "PasteCompleted=\"Horizon_PasteCompleted\"");
        }

        /// <summary>正でない kh0・同じ土層の要素に違う値は、理由を示して何も変えない (表は土層-杭セットの値に戻す)。</summary>
        [TestMethod]
        public void InvalidPastedKh0_ChangesNothing()
        {
            OnSta(() =>
            {
                var (vm, pile) = Kh0Screen();
                var rows = vm.SelectedHorizontalSoilReactions;
                var first = rows.First(r => !string.IsNullOrEmpty(r.Name));
                string name = first.Name!;
                double original = first.Kh0;

                first.Kh0 = -5;   // 貼り付けで入る形 (行の値だけが変わる)
                vm.ApplyKh0FromTable();
                Assert.IsNull(pile.GetKh0Override(name), "負の kh0 を手入力にしました");
                Assert.AreEqual(original, vm.SelectedHorizontalSoilReactions.First(r => r.Name == name).Kh0, 1e-9, "表が元の値に戻っていません");

                var sameLayer = vm.SelectedHorizontalSoilReactions.Where(r => r.Name == name).ToList();
                if (sameLayer.Count < 2) return;   // 1 要素だけの土層では違う値は入れられない
                sameLayer[0].Kh0 = 11111; sameLayer[1].Kh0 = 22222;
                vm.ApplyKh0FromTable();
                Assert.IsNull(pile.GetKh0Override(name), "同じ土層に違う値を貼ったのに、どちらかを手入力にしました");
            });
        }

        // ── 荷重ケース・Chang の表の元に戻す ─────────────────────────────

        /// <summary>
        /// セルの編集の確定で元に戻すの控えを積んでいる表は、貼り付けの完了でも積む。
        /// 以前は荷重ケースの表と Chang の表が確定でしか積んでおらず、貼り付けを元に戻せなかった。
        /// </summary>
        [TestMethod]
        public void TablesThatRecordUndoOnEdit_AlsoRecordItOnPaste()
        {
            foreach (var (window, grids) in new[]
            {
                ("LoadCaseWindow.xaml", new[] { "DataGridLoadCombination", "DataGridCommonLoadCase1", "DataGridLoadCase1", "DataGridCommonLoadCase2", "DataGridLoadCase2" }),
                ("ChangWindow.xaml", new[] { "DataGridSoilPile", "DataGridChang" }),
            })
            {
                string xaml = Regex.Replace(TestSource.Read("Graphics_r1", "Views", window), "<!--.*?-->", "", RegexOptions.Singleline);
                foreach (var name in grids)
                {
                    var tag = Regex.Match(xaml, $@"<common:EnhancedDataGrid\s[^>]*x:Name=""{name}""[^>]*>", RegexOptions.Singleline);
                    Assert.IsTrue(tag.Success, $"{window}: 表 {name} が見つかりません");
                    StringAssert.Contains(tag.Value, "PasteCompleted=\"DataGrid_PasteCompleted\"", $"{window}: 表 {name} の貼り付けを元に戻すの履歴に積んでいません");
                }
            }
            StringAssert.Contains(TestSource.MethodBody(TestSource.Read("Graphics_r1", "Views", "LoadCaseWindow.xaml.cs"), "private void DataGrid_PasteCompleted("), "PushUndoState");
            StringAssert.Contains(TestSource.MethodBody(TestSource.Read("Graphics_r1", "Views", "ChangWindow.xaml.cs"), "private void DataGrid_PasteCompleted("), "PushPasteUndo");
        }

        /// <summary>Chang の表へ複数のセルを貼ると、元に戻す 1 回で全部戻り、やり直すで全部入る。</summary>
        [TestMethod]
        public void ChangPaste_IsOneUndoStep()
        {
            OnSta(() =>
            {
                var vm = new ChangViewModel(new InputModel());
                vm.AddChang();
                vm.AddChang();
                var rows = vm.Changs;
                var before = rows.Select(c => (c.HorizontalLoad, c.EI)).ToArray();

                var grid = Grid(rows, rows[0], nameof(Chang.HorizontalLoad), nameof(Chang.EI));
                Assert.IsTrue(grid.TryPasteText("111\t2.5E8\n222\t3.5E8"));
                vm.PushPasteUndo(grid.LastAddedTo, grid.LastAddedRows, grid.LastChanges);   // 画面の貼り付け完了と同じ
                Assert.AreEqual(4, grid.LastChanges.Count, "(前提) 4 つのセルを書いた");

                vm.UndoCommand.Execute(null);
                CollectionAssert.AreEqual(before, rows.Select(c => (c.HorizontalLoad, c.EI)).ToArray(), "元に戻す 1 回で貼り付け全体が戻りません");
                vm.RedoCommand.Execute(null);
                CollectionAssert.AreEqual(new[] { (111.0, 2.5e8), (222.0, 3.5e8) }, rows.Take(2).Select(c => (c.HorizontalLoad, c.EI)).ToArray(), "やり直すで戻りません");
            });
        }

        /// <summary>貼り付けで行が足りずに足した行は、元に戻すで取り除く (値を戻したあと)。やり直すでまた足す。</summary>
        [TestMethod]
        public void ChangPaste_RowsAddedByThePasteAreRemovedOnUndo()
        {
            OnSta(() =>
            {
                var vm = new ChangViewModel(new InputModel());
                var rows = vm.ChangSoilPiles;
                if (rows.Count == 0) vm.AddChangSoilPile();
                int n = rows.Count;
                var grid = Grid(rows, rows[n - 1], nameof(ChangSoilPile.Thickness));
                Assert.IsTrue(grid.TryPasteText("1.5\n2.5\n3.5"), "(前提) 足りない行を足して貼れる");
                Assert.AreEqual(n + 2, rows.Count, "(前提) 2 行足した");
                vm.PushPasteUndo(grid.LastAddedTo, grid.LastAddedRows, grid.LastChanges);

                vm.UndoCommand.Execute(null);
                Assert.AreEqual(n, rows.Count, "貼り付けで足した行が残っています");
                vm.RedoCommand.Execute(null);
                Assert.AreEqual(n + 2, rows.Count, "やり直すで行が戻りません");
                Assert.AreEqual(3.5, rows[^1].Thickness, 1e-12);
            });
        }
    }
}
