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
