using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 表の選択セルをコピーする処理が 1 か所にあること。
    ///
    /// 「選択セルを行ごとにまとめてタブで繋ぐ」だけの 15 行が、
    /// <c>BaseViewModel</c>・<c>MainWindowViewModel</c>・<c>SettlementViewModel</c>・
    /// <c>ChangWindow</c> の 4 か所に書き写されていた。
    /// どれも中身は同じだったが、貼り付けの形は一度直している
    /// (行番号の列を入れると Excel で列がずれる)。1 つ直し忘れれば、
    /// <b>その画面だけ貼り付けがずれる</b>。例外にはならず、貼ってみるまで分からない。
    ///
    /// セルの値の取り方 (<c>DataGridCsv.GetCellValue</c>) と同じ場所に置いた。
    /// </summary>
    [TestClass]
    public class DataGridClipboardTests
    {
        [TestMethod]
        public void TheSelectionCopy_LivesInOnePlace()
        {
            var root = TestSource.Dir("Graphics_r1");
            var copies = new List<string>();
            int scanned = 0;

            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                scanned++;

                foreach (var line in File.ReadAllLines(file))
                {
                    var t = line.Trim();
                    if (t.StartsWith("//") || t.StartsWith("///")) continue;
                    // 選択セルを行ごとにまとめる形
                    if (t.Contains("SelectedCells.GroupBy"))
                        copies.Add($"{Path.GetFileName(file)}: {t}");
                }
            }

            TestSource.AssertScanned(scanned, 300, "本体のソース");
            Assert.AreEqual(1, copies.Count,
                $"選択セルをまとめる処理が {copies.Count} か所にあります。"
                + "DataGridCsv.CopySelectionToClipboard に 1 つだけ置いてください。"
                + "書き写すと、貼り付けの形を直したときに 1 つ直し忘れます:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", copies));
        }

        /// <summary>
        /// 行番号の列を入れないこと。入れると貼り付け先で列がずれる。
        ///
        /// 行番号は <c>DataGrid_LoadingRow</c> で行ヘッダーに入れており、セルではない。
        /// <c>SelectedCells</c> をたどる限り混ざらないが、
        /// 「行番号も付けたい」と足されると再発する。
        /// </summary>
        [TestMethod]
        public void TheSelectionCopy_DoesNotAddRowNumbers()
        {
            var body = TestSource.MethodBody(
                TestSource.Read("Graphics_r1", "Output", "DataGridCsv.cs"),
                "public static void CopySelectionToClipboard(DataGrid dataGrid)");

            foreach (var forbidden in new[] { "GetIndex()", "Header", "IndexOf(" })
                Assert.IsFalse(body.Contains(forbidden),
                    $"コピーに行番号らしきもの ({forbidden}) が入っています。"
                    + "貼り付け先で列がずれます");
        }
    }
}
