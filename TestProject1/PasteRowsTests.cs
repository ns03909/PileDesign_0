using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;

namespace TestProject1
{
    /// <summary>
    /// 表への貼り付けで、行を黙って捨てたり、失敗したのに表を変えたりしないこと。
    ///
    /// - 以前は先頭行のセルがすべて数値に読めないと見出しとみなして捨てていた。
    ///   荷重ケース名のような文字の列に「CASE-A / CASE-B」を貼ると、CASE-A が消えた。
    ///   今は、貼り付け先の列の見出しと一致するときだけ読み飛ばす。
    /// - 以前は足りない行を先に表へ足してから検証していたので、検証で止まっても空の行が残った。
    ///   今は仮の行で検証し、全体が妥当なときだけ表へ足す。
    /// </summary>
    [TestClass]
    public class PasteRowsTests
    {
        public sealed class CaseRow
        {
            public string Name { get; set; } = "";
            public double Value { get; set; }
        }

        private static void OnSta(Action action)
        {
            var ex = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
            Assert.IsFalse(timedOut, "STA スレッドでの実行が時間内に終わりませんでした");
            if (ex != null) throw new AssertFailedException(ex.ToString());
        }

        /// <summary>「名称」「値」の 2 列の表。<paramref name="startRow"/> 行目の先頭セルを選んだ状態にする。</summary>
        private static EnhancedDataGrid Grid(ObservableCollection<CaseRow> rows, int startRow = 0, int startCol = 0)
        {
            var grid = new EnhancedDataGrid
            {
                AutoGenerateColumns = false,
                SelectionUnit = DataGridSelectionUnit.Cell,
                SelectionMode = DataGridSelectionMode.Extended,
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new Binding(nameof(CaseRow.Name)) });
            grid.Columns.Add(new DataGridTextColumn { Header = "値 (kN)", Binding = new Binding(nameof(CaseRow.Value)) });
            grid.ItemsSource = rows;
            // 画面に出していない表では表示位置 (DisplayIndex) が決まらないので、画面と同じ並びを明示する
            for (int i = 0; i < grid.Columns.Count; i++) grid.Columns[i].DisplayIndex = i;
            grid.SelectedCells.Add(new DataGridCellInfo(rows[startRow], grid.Columns[startCol]));
            return grid;
        }

        private static bool Paste(EnhancedDataGrid grid, string text)
        {
            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;   // エラーのダイアログで止まらないように
            try { return grid.TryPasteText(text); }
            finally { MessageService.IsUnattended = unattended; }
        }

        /// <summary>変動軸力の表と同じ形: 絶対値の列と変動の列が交互に並び、モードで片方を隠す。</summary>
        public sealed class AxialRow
        {
            public double Vl { get; set; } = 100;
            public double Abs1 { get; set; }
            public double Var1 { get => Abs1 - Vl; set => Abs1 = Vl + value; }
            public double Abs2 { get; set; }
            public double Var2 { get => Abs2 - Vl; set => Abs2 = Vl + value; }
        }

        private static EnhancedDataGrid AxialGrid(ObservableCollection<AxialRow> rows, bool variationMode)
        {
            var grid = new EnhancedDataGrid
            {
                AutoGenerateColumns = false,
                SelectionUnit = DataGridSelectionUnit.Cell,
                SelectionMode = DataGridSelectionMode.Extended,
            };
            var hiddenAbs = variationMode ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            var hiddenVar = variationMode ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            grid.Columns.Add(new DataGridTextColumn { Header = "L1-1", Binding = new Binding(nameof(AxialRow.Abs1)), Visibility = hiddenAbs });
            grid.Columns.Add(new DataGridTextColumn { Header = "ΔL1-1", Binding = new Binding(nameof(AxialRow.Var1)), Visibility = hiddenVar });
            grid.Columns.Add(new DataGridTextColumn { Header = "L1-2", Binding = new Binding(nameof(AxialRow.Abs2)), Visibility = hiddenAbs });
            grid.Columns.Add(new DataGridTextColumn { Header = "ΔL1-2", Binding = new Binding(nameof(AxialRow.Var2)), Visibility = hiddenVar });
            grid.ItemsSource = rows;
            for (int i = 0; i < grid.Columns.Count; i++) grid.Columns[i].DisplayIndex = i;
            grid.SelectedCells.Add(new DataGridCellInfo(rows[0], grid.Columns[variationMode ? 1 : 0]));
            return grid;
        }

        /// <summary>
        /// 隠れた列のある表へ複数列を貼ると、見えている列へ順に入る。以前は隠れた列も数えたので、絶対値の表示で 2 列貼ると
        /// 2 列目の値が隠れた変動の列へ入って 1 列目を VL + 値で上書きし、2 列目は変わらなかった (2026-10-13 利用者の報告)。
        /// </summary>
        [DataTestMethod]
        [DataRow(false, DisplayName = "絶対値の表示")]
        [DataRow(true, DisplayName = "変動の表示")]
        public void MultipleColumns_GoToTheVisibleColumns_SkippingHiddenOnes(bool variationMode)
        {
            OnSta(() =>
            {
                var rows = new ObservableCollection<AxialRow> { new(), new() };
                Assert.IsTrue(Paste(AxialGrid(rows, variationMode), "10\t20\n30\t40"));
                double[] first = variationMode ? [rows[0].Var1, rows[0].Var2] : [rows[0].Abs1, rows[0].Abs2];
                double[] second = variationMode ? [rows[1].Var1, rows[1].Var2] : [rows[1].Abs1, rows[1].Abs2];
                CollectionAssert.AreEqual(new[] { 10.0, 20.0 }, first, "1 行目の 2 列が、見えている 2 列へ入っていません");
                CollectionAssert.AreEqual(new[] { 30.0, 40.0 }, second, "2 行目の 2 列が、見えている 2 列へ入っていません");
            });
        }

        /// <summary>
        /// 画面の変動軸力の表と同じ列 (杭配置の絶対値 AxialForceLevel1s[i] と変動 AxialForceVariationLevel1s[i] の添字付きバインディング、
        /// モードで片方を隠す) を、実際の杭配置 (<see cref="PileDesign.Models.InputData.PileLayoutDataItem"/>) に組む。
        /// </summary>
        private static EnhancedDataGrid PileAxialGrid(ObservableCollection<PileDesign.Models.InputData.PileLayoutDataItem> piles,
            bool variationMode, int startRow = 0, int startCase = 0)
        {
            var grid = new EnhancedDataGrid
            {
                AutoGenerateColumns = false,
                SelectionUnit = DataGridSelectionUnit.Cell,
                SelectionMode = DataGridSelectionMode.Extended,
            };
            var absVis = variationMode ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            var varVis = variationMode ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            for (int i = 0; i < 4; i++)
            {
                grid.Columns.Add(new DataGridTextColumn { Header = $"L1-{i + 1}", Binding = new Binding($"AxialForceLevel1s[{i}]"), Visibility = absVis });
                grid.Columns.Add(new DataGridTextColumn { Header = $"ΔL1-{i + 1}", Binding = new Binding($"AxialForceVariationLevel1s[{i}]"), Visibility = varVis });
            }
            grid.ItemsSource = piles;
            for (int i = 0; i < grid.Columns.Count; i++) grid.Columns[i].DisplayIndex = i;
            grid.SelectedCells.Add(new DataGridCellInfo(piles[startRow], grid.Columns[2 * startCase + (variationMode ? 1 : 0)]));
            return grid;
        }

        private static ObservableCollection<PileDesign.Models.InputData.PileLayoutDataItem> Piles(int count, double vl)
        {
            var piles = new ObservableCollection<PileDesign.Models.InputData.PileLayoutDataItem>();
            for (int i = 0; i < count; i++)
            {
                var p = new PileDesign.Models.InputData.PileLayoutDataItem { No = i + 1, PileNo = i + 1 };
                p.AxialForceVL0 = vl;
                for (int c = 0; c < 4; c++) p.AxialForceLevel1s[c] = vl + 1000 * (i + 1) + c;   // 区別できる値
                piles.Add(p);
            }
            return piles;
        }

        /// <summary>
        /// 実際の杭配置と添字付きバインディングで、貼った値が絶対値と変動の両方にそろって入る (変動 = 絶対値 − VL)。
        /// 変動の表示に貼れば絶対値は VL + 値、絶対値の表示に貼れば変動は値 − VL。
        /// </summary>
        [DataTestMethod]
        [DataRow(false, DisplayName = "絶対値の表示")]
        [DataRow(true, DisplayName = "変動の表示")]
        public void RealPileRows_KeepAbsoluteAndVariationInStep(bool variationMode)
        {
            OnSta(() =>
            {
                const double vl = 500;
                var piles = Piles(2, vl);
                Assert.IsTrue(Paste(PileAxialGrid(piles, variationMode), "10\t20\n30\t40"));
                double[][] pasted = [[10, 20], [30, 40]];
                for (int r = 0; r < 2; r++)
                    for (int c = 0; c < 2; c++)
                    {
                        double abs = piles[r].AxialForceLevel1s[c], dv = piles[r].AxialForceVariationLevel1s[c];
                        Assert.AreEqual(variationMode ? vl + pasted[r][c] : pasted[r][c], abs, 1e-9, $"杭 {r + 1} の L1-{c + 1} の絶対値");
                        Assert.AreEqual(abs - vl, dv, 1e-9, $"杭 {r + 1} の L1-{c + 1}: 変動が絶対値 − VL になっていません");
                    }
            });
        }

        /// <summary>
        /// 途中の列 (L1-2)・途中の行から貼ると、その範囲のセルだけが変わり、範囲の外 (左の列・右の列・上下の行) は変わらない。
        /// </summary>
        [DataTestMethod]
        [DataRow(false, DisplayName = "絶対値の表示")]
        [DataRow(true, DisplayName = "変動の表示")]
        public void PastingFromTheMiddle_LeavesCellsOutsideTheRangeAlone(bool variationMode)
        {
            OnSta(() =>
            {
                const double vl = 500;
                var piles = Piles(4, vl);
                var before = piles.Select(p => p.AxialForceLevel1s.ToArray()).ToArray();
                Assert.IsTrue(Paste(PileAxialGrid(piles, variationMode, startRow: 1, startCase: 1), "7\t8\n9\t10"));
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                    {
                        bool inside = r is 1 or 2 && c is 1 or 2;
                        double expected = inside
                            ? (variationMode ? vl : 0) + new[] { 7.0, 8.0, 9.0, 10.0 }[(r - 1) * 2 + (c - 1)]
                            : before[r][c];
                        Assert.AreEqual(expected, piles[r].AxialForceLevel1s[c], 1e-9,
                            $"杭 {r + 1} の L1-{c + 1} ({(inside ? "貼った範囲" : "範囲の外")}) の値が違います");
                    }
            });
        }

        /// <summary>
        /// 複数のセルへの貼り付けは、元に戻す 1 回で全体が戻る。画面の軸力の表は貼り付けの完了
        /// (DataGridPileAxialForce_PasteCompleted) で 1 つの控えを積む。ここでは同じ順 (貼る → 控えを積む) をビューモデルで再現する。
        /// </summary>
        [TestMethod]
        public void OneUndo_RestoresTheWholePaste()
        {
            OnSta(() =>
            {
                bool unattended = MessageService.IsUnattended;
                MessageService.IsUnattended = true;
                try
                {
                    using var vm = new PileDesign.ViewModels.MainWindowViewModel();
                    var m = vm.CurrentInputModel!;
                    m.PileLayoutItems = Piles(3, 500);
                    vm.SaveUndoState("読込");   // 読込と同じく、今の入力を元に戻す起点にする
                    vm.MarkProjectReplaced();
                    var before = m.PileLayoutItems.Select(p => p.AxialForceLevel1s.ToArray()).ToArray();

                    Assert.IsTrue(Paste(PileAxialGrid(m.PileLayoutItems, variationMode: false), "1\t2\t3\n4\t5\t6\n7\t8\t9"));
                    vm.SaveUndoState("杭軸力 貼り付け");   // 画面の貼り付け完了と同じ
                    Assert.AreEqual(9.0, m.PileLayoutItems[2].AxialForceLevel1s[2], "(前提) 貼り付けた");

                    vm.UndoCommand.Execute(null);
                    var after = vm.CurrentInputModel!.PileLayoutItems.Select(p => p.AxialForceLevel1s.ToArray()).ToArray();
                    for (int r = 0; r < 3; r++)
                        CollectionAssert.AreEqual(before[r], after[r], $"元に戻す 1 回で杭 {r + 1} の軸力が戻っていません");
                }
                finally { MessageService.IsUnattended = unattended; }
            });
            string handler = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Views", "MainWindow.DataGridHandlers.cs"),
                "private void DataGridPileAxialForce_PasteCompleted(");
            StringAssert.Contains(handler, "vm.SaveUndoState(", "軸力の表の貼り付け完了で控えを積んでいません (貼り付けを 1 回で戻せません)");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml"), "PasteCompleted=\"DataGridPileAxialForce_PasteCompleted\"");
        }

        [TestMethod]
        public void TextOnlyFirstRowIsData()
        {
            OnSta(() =>
            {
                var rows = new ObservableCollection<CaseRow> { new(), new() };
                Assert.IsTrue(Paste(Grid(rows), "CASE-A\nCASE-B"));
                CollectionAssert.AreEqual(new[] { "CASE-A", "CASE-B" }, rows.Select(r => r.Name).ToArray(),
                    "文字だけの先頭行が、見出しとして捨てられています");
            });
        }

        [TestMethod]
        public void HeaderRowOfThisGridIsSkipped()
        {
            OnSta(() =>
            {
                // この表を見出しごと全体コピーした形 (多段見出しは空白で繋いである)
                var rows = new ObservableCollection<CaseRow> { new(), new() };
                Assert.IsTrue(Paste(Grid(rows), "名称\t値 (kN)\nCASE-A\t1.5\nCASE-B\t2.5"));
                CollectionAssert.AreEqual(new[] { "CASE-A", "CASE-B" }, rows.Select(r => r.Name).ToArray());
                CollectionAssert.AreEqual(new[] { 1.5, 2.5 }, rows.Select(r => r.Value).ToArray());
            });
        }

        [TestMethod]
        public void HeaderIsMatchedFromTheStartColumn()
        {
            OnSta(() =>
            {
                // 2 列目から貼るときは、2 列目の見出しと比べる
                var rows = new ObservableCollection<CaseRow> { new() { Name = "keep" }, new() { Name = "keep" } };
                Assert.IsTrue(Paste(Grid(rows, startCol: 1), "値(kN)\n3\n4"));
                CollectionAssert.AreEqual(new[] { 3.0, 4.0 }, rows.Select(r => r.Value).ToArray());
                Assert.IsTrue(rows.All(r => r.Name == "keep"), "貼り付け先でない列が変わりました");
            });
        }

        [TestMethod]
        public void FailedPasteDoesNotAddRows()
        {
            OnSta(() =>
            {
                var rows = new ObservableCollection<CaseRow> { new() { Name = "A", Value = 1.0 } };
                // 2 行目は表に無いので足す必要があるが、その行の値が数値でない → 検証で止まる
                Assert.IsFalse(Paste(Grid(rows, startCol: 1), "9.5\nabc"));
                Assert.AreEqual(1, rows.Count, "貼り付けが失敗したのに、行が足されたまま残っています");
                Assert.AreEqual(1.0, rows[0].Value, "貼り付けが失敗したのに、既存の行が書き換わっています");
            });
        }

        [TestMethod]
        public void NonFiniteOnANewRowDoesNotAddRows()
        {
            OnSta(() =>
            {
                var rows = new ObservableCollection<CaseRow> { new() { Value = 1.0 } };
                Assert.IsFalse(Paste(Grid(rows, startCol: 1), "2\nNaN"));
                Assert.AreEqual(1, rows.Count, "足す予定の行の値が不正なのに、行が足されています");
            });
        }

        [TestMethod]
        public void SuccessfulPasteAddsTheMissingRows()
        {
            OnSta(() =>
            {
                var rows = new ObservableCollection<CaseRow> { new() };
                Assert.IsTrue(Paste(Grid(rows), "CASE-A\t1\nCASE-B\t2\nCASE-C\t3"));
                Assert.AreEqual(3, rows.Count, "足りない行が足されていません");
                CollectionAssert.AreEqual(new[] { "CASE-A", "CASE-B", "CASE-C" }, rows.Select(r => r.Name).ToArray());
                CollectionAssert.AreEqual(new[] { 1.0, 2.0, 3.0 }, rows.Select(r => r.Value).ToArray());
            });
        }

        // ── 途中の空行 ────────────────────────────────────────────────
        // 以前は空の行をすべて捨てていたので、1 列の「10 / 空白 / 30」が「10 / 30」に詰まり、
        // 30 が 1 行上の別の行に入った。

        [TestMethod]
        public void BlankLineIsKeptAsAnEmptyCell()
        {
            CollectionAssert.AreEqual(new[] { "10", "", "30" },
                EnhancedDataGrid.SplitPastedRows("10\r\n\r\n30\r\n").Select(r => r[0]).ToArray(),
                "途中の空行が捨てられています (末尾の改行だけを捨てること)");
            Assert.AreEqual(2, EnhancedDataGrid.SplitPastedRows("A\nB\n\n").Length, "末尾の空行が行として残っています");
        }

        [TestMethod]
        public void BlankLineIntoANumberColumnStopsWithoutShifting()
        {
            OnSta(() =>
            {
                var rows = new ObservableCollection<CaseRow> { new() { Value = 1 }, new() { Value = 2 }, new() { Value = 3 } };
                Assert.IsFalse(Paste(Grid(rows, startCol: 1), "10\n\n30"),
                    "空欄にできない数値の列に空欄を貼ったのに、通りました");
                CollectionAssert.AreEqual(new[] { 1.0, 2.0, 3.0 }, rows.Select(r => r.Value).ToArray(),
                    "止まったのに値が変わっています (行が詰まって別の行に入っていないか)");
            });
        }

        [TestMethod]
        public void BlankLineIntoATextColumnKeepsThePosition()
        {
            OnSta(() =>
            {
                var rows = new ObservableCollection<CaseRow> { new() { Name = "x" }, new() { Name = "y" }, new() { Name = "z" } };
                Assert.IsTrue(Paste(Grid(rows), "A\n\nC"));
                CollectionAssert.AreEqual(new[] { "A", "", "C" }, rows.Select(r => r.Name).ToArray(),
                    "空行のぶん行がずれています");
            });
        }

        [TestMethod]
        public void TrailingNewlineDoesNotAddARow()
        {
            OnSta(() =>
            {
                var rows = new ObservableCollection<CaseRow> { new(), new() };
                Assert.IsTrue(Paste(Grid(rows), "A\r\nB\r\n"));
                Assert.AreEqual(2, rows.Count, "末尾の改行のぶん、行が足されました");
            });
        }

        // ── Ctrl+C のコピー ───────────────────────────────────────────
        // 以前は CSV・右クリックのコピーと別の写しで値を取り出し、テンプレート列は画面の部品から読んでいた。
        // 画面外の行は部品が無く、選択肢の値などが空欄になった。
        // ここでは画面に出していない表 (全行が画面外と同じ状態) で確かめる。

        public enum Soil { Sand, Clay }

        public sealed class LayerRow
        {
            public Soil Kind { get; set; }
            public double Weight { get; set; }
        }

        [TestMethod]
        public void CtrlCopyReadsTemplateColumnsWithoutTheirParts()
        {
            OnSta(() =>
            {
                var rows = new ObservableCollection<LayerRow> { new() { Kind = Soil.Sand, Weight = 18.6 }, new() { Kind = Soil.Clay, Weight = 16.7 } };
                var grid = new EnhancedDataGrid { AutoGenerateColumns = false, SelectionUnit = DataGridSelectionUnit.Cell };
                // 土層分類の列と同じ形: 選択肢を SelectedItem で結んだテンプレート列
                var template = (System.Windows.DataTemplate)System.Windows.Markup.XamlReader.Parse(
                    "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>"
                    + "<ComboBox SelectedItem='{Binding Kind}'/></DataTemplate>");
                grid.Columns.Add(new DataGridTemplateColumn { Header = "土層分類", CellTemplate = template });
                grid.Columns.Add(new DataGridTextColumn { Header = "単位体積重量", Binding = new Binding(nameof(LayerRow.Weight)) { StringFormat = "N2" } });
                grid.ItemsSource = rows;
                for (int i = 0; i < grid.Columns.Count; i++) grid.Columns[i].DisplayIndex = i;
                foreach (var row in rows)
                    foreach (var col in grid.Columns)
                        grid.SelectedCells.Add(new DataGridCellInfo(row, col));

                string? text = grid.BuildSelectionText();
                Assert.IsNotNull(text);
                var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
                CollectionAssert.AreEqual(new[] { "土層分類\t単位体積重量", "Sand\t18.60", "Clay\t16.70" }, lines,
                    "画面外の行のテンプレート列が空欄になっています (CSV と同じ取り出し方になっていない)");
            });
        }
    }
}
