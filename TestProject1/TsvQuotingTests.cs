using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Output;
using PileDesign.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;

namespace TestProject1
{
    /// <summary>
    /// 表のコピー (タブ区切り) と貼り付けで、セルの中のタブ・改行を区切りと取り違えないこと。
    ///
    /// 以前はコピーが値をそのまま繋ぎ、貼り付けはタブと改行で単純に割っていた。名称などにタブや改行が入ると、
    /// 貼り付け先で列や行がずれた。今はコピーがタブ・改行を含む値 (と引用符で始まる値) を引用符で囲み、
    /// 貼り付けが同じ規則で読み戻す (Excel がセルをコピーするときと同じ形)。
    /// </summary>
    [TestClass]
    public class TsvQuotingTests
    {
        [TestMethod]
        public void OnlyValuesThatNeedItAreQuoted()
        {
            Assert.AreEqual("abc", DataGridCsv.EscapeTsvField("abc"));
            Assert.AreEqual("1.25", DataGridCsv.EscapeTsvField("1.25"));
            Assert.AreEqual("", DataGridCsv.EscapeTsvField(null));
            Assert.AreEqual("a,b", DataGridCsv.EscapeTsvField("a,b"), "カンマはタブ区切りでは区切りではないので囲まない");
            Assert.AreEqual("5\"", DataGridCsv.EscapeTsvField("5\""), "途中の引用符だけなら囲まない (Excel と同じ)");
            Assert.AreEqual("\"A\tB\"", DataGridCsv.EscapeTsvField("A\tB"));
            Assert.AreEqual("\"上\n下\"", DataGridCsv.EscapeTsvField("上\n下"));
            Assert.AreEqual("\"\"\"q\"\"\"", DataGridCsv.EscapeTsvField("\"q\""), "引用符で始まる値は囲み、中の引用符を二重にする");
        }

        /// <summary>書き出した形を、貼り付けの読み方でそのまま読み戻せること。</summary>
        [TestMethod]
        public void WhatIsCopiedIsReadBackAsIs()
        {
            string[][] table =
            [
                ["名称\tA", "1.5", "line1\nline2"],
                ["\"quoted\"", "", "plain"],
                ["5\"", "a,b", "末尾"],
            ];
            string text = string.Join("\r\n", table.Select(r => string.Join("\t", r.Select(DataGridCsv.EscapeTsvField)))) + "\r\n";

            var back = EnhancedDataGrid.SplitPastedRows(text);
            Assert.AreEqual(table.Length, back.Length, "行数が変わりました (セルの中の改行を行の区切りと読んでいます)");
            for (int r = 0; r < table.Length; r++)
                CollectionAssert.AreEqual(table[r], back[r], $"{r + 1} 行目が元の値に戻りません");
        }

        /// <summary>Excel が改行を含むセルをコピーした形 (囲みの中に改行) も、1 つのセルとして読むこと。</summary>
        [TestMethod]
        public void ExcelStyleMultilineCellIsOneCell()
        {
            var rows = EnhancedDataGrid.SplitPastedRows("\"一行目\r\n二行目\"\t10\r\nB\t20\r\n");
            Assert.AreEqual(2, rows.Length);
            CollectionAssert.AreEqual(new[] { "一行目\n二行目", "10" }, rows[0]);
            CollectionAssert.AreEqual(new[] { "B", "20" }, rows[1]);
        }

        public sealed class NamedRow
        {
            public string Name { get; set; } = "";
            public double Value { get; set; }
        }

        /// <summary>表から表へ: コピーした文字を貼っても、タブ・改行を含む名称で列や行がずれないこと。</summary>
        [TestMethod]
        public void CopyThenPasteKeepsCellsWithTabsAndNewlines()
        {
            var ex = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var source = new ObservableCollection<NamedRow>
                {
                    new() { Name = "杭A\t北", Value = 1 },
                    new() { Name = "杭B\n南", Value = 2 },
                };
                var from = Grid(source);
                foreach (var row in source)
                    foreach (var col in from.Columns)
                        from.SelectedCells.Add(new DataGridCellInfo(row, col));
                string copied = DataGridCsv.BuildSelectionText(from, withTitleRow: false)!;

                var target = new ObservableCollection<NamedRow> { new(), new() };
                var to = Grid(target);
                to.SelectedCells.Add(new DataGridCellInfo(target[0], to.Columns[0]));
                bool unattended = MessageService.IsUnattended;
                MessageService.IsUnattended = true;
                try { Assert.IsTrue(to.TryPasteText(copied), "貼り付けが止まりました"); }
                finally { MessageService.IsUnattended = unattended; }

                Assert.AreEqual(2, target.Count, "セルの中の改行で行が増えています");
                CollectionAssert.AreEqual(new[] { "杭A\t北", "杭B\n南" }, target.Select(r => r.Name).ToArray(),
                    "名称が元どおりに貼られていません (タブ・改行を区切りと読んでいます)");
                CollectionAssert.AreEqual(new[] { 1.0, 2.0 }, target.Select(r => r.Value).ToArray(), "値が別の列に入っています");
            }, out bool timedOut);
            Assert.IsFalse(timedOut);
            if (ex != null) throw new AssertFailedException(ex.ToString());

            static EnhancedDataGrid Grid(ObservableCollection<NamedRow> rows)
            {
                var g = new EnhancedDataGrid { AutoGenerateColumns = false, SelectionUnit = DataGridSelectionUnit.Cell, SelectionMode = DataGridSelectionMode.Extended };
                g.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new Binding(nameof(NamedRow.Name)) });
                g.Columns.Add(new DataGridTextColumn { Header = "値", Binding = new Binding(nameof(NamedRow.Value)) });
                g.ItemsSource = rows;
                for (int i = 0; i < g.Columns.Count; i++) g.Columns[i].DisplayIndex = i;
                return g;
            }
        }
    }
}
