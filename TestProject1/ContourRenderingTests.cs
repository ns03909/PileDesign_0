using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Output;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// 計算書のコンタ図と、解析後の編集判定。
    ///
    /// コンタ図はセルを 4x4 の単色矩形で塗っていたため、1 区画あたり 16 色しか使えず、
    /// 格子の粗い方向に段差が出て<b>縞</b>に見えていた。画素ごとに補間する方式に変え、
    /// その索引引きが <see cref="DiagramRenderer.FindCell"/>。
    /// </summary>
    [TestClass]
    public class ContourRenderingTests
    {
        private static readonly List<double> Grid = [0.0, 1.0, 3.0, 6.0];

        [TestMethod]
        public void FindCell_ReturnsTheCellContainingTheValue()
        {
            Assert.AreEqual(0, DiagramRenderer.FindCell(Grid, 0.0), "下端は最初のセル");
            Assert.AreEqual(0, DiagramRenderer.FindCell(Grid, 0.5));
            Assert.AreEqual(1, DiagramRenderer.FindCell(Grid, 1.0), "境界は右側のセルの左端");
            Assert.AreEqual(1, DiagramRenderer.FindCell(Grid, 2.9));
            Assert.AreEqual(2, DiagramRenderer.FindCell(Grid, 3.0));
            Assert.AreEqual(2, DiagramRenderer.FindCell(Grid, 6.0), "上端は最後のセル");
        }

        /// <summary>格子の外は -1。塗らずに透明のままにするため。</summary>
        [TestMethod]
        public void FindCell_ReturnsMinusOneOutsideTheGrid()
        {
            Assert.AreEqual(-1, DiagramRenderer.FindCell(Grid, -0.001));
            Assert.AreEqual(-1, DiagramRenderer.FindCell(Grid, 6.001));
            Assert.AreEqual(-1, DiagramRenderer.FindCell([], 0.0));
            Assert.AreEqual(-1, DiagramRenderer.FindCell([1.0], 1.0), "セルが作れない");
            Assert.AreEqual(-1, DiagramRenderer.FindCell(null!, 0.0));
        }

        /// <summary>
        /// 返した索引で ix+1 を引いても範囲外にならないこと。
        /// 双線形補間は 4 隅を読むので、ここが崩れると例外になる。
        /// </summary>
        [TestMethod]
        public void FindCell_NeverPointsAtTheLastNode()
        {
            for (double v = 0.0; v <= 6.0; v += 0.01)
            {
                int i = DiagramRenderer.FindCell(Grid, v);
                Assert.IsTrue(i >= 0 && i + 1 < Grid.Count, $"{v} で索引 {i}");
            }
        }

        /// <summary>
        /// 「解析のあとに編集されたか」は保存ファイルに持たせること。
        ///
        /// 以前は「解析時の入力のスナップショットが現在の入力と別インスタンスか」で
        /// 代用していたが、スナップショットは解析時に必ず複製して作るので常に別物になる。
        /// そのため<b>編集していなくても、開くたびに「編集されています」と言われて</b>いた。
        /// </summary>
        [TestMethod]
        public void ProjectData_CarriesWhetherTheInputWasEditedAfterAnalysis()
        {
            var options = new JsonSerializerOptions();

            foreach (bool edited in new[] { true, false })
            {
                string json = JsonSerializer.Serialize(
                    new PileDesign.Models.ProjectData { InputChangedSinceAnalysis = edited }, options);
                var restored = JsonSerializer.Deserialize<PileDesign.Models.ProjectData>(json, options);

                Assert.AreEqual(edited, restored!.InputChangedSinceAnalysis,
                    "保存した編集状態が復元できていません");
            }

            // 旧ファイルには無い → null。読込側はそのとき従来の判定に落とす
            var old = JsonSerializer.Deserialize<PileDesign.Models.ProjectData>("{}", options);
            Assert.IsNull(old!.InputChangedSinceAnalysis);
        }

        /// <summary>読込側が、保存された値を使っていること。</summary>
        [TestMethod]
        public void TheLoadPathUsesTheStoredFlag()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(ContourRenderingTests).Assembly.Location)!);
            string? root = null;
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                {
                    root = dir.FullName;
                    break;
                }
            }
            Assert.IsNotNull(root);

            string code = File.ReadAllText(
                Path.Combine(root!, "Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"));

            StringAssert.Contains(code, "projectData.InputChangedSinceAnalysis ?? snapshotIsSeparate",
                "保存された編集状態を使っていません (参照の同一性だけで判定していないか)");
        }

        /// <summary>
        /// 読込の仕上げの <c>SaveUndoState</c> のあとに、復元した値へ戻していること。
        ///
        /// <c>SaveUndoState</c> は全編集の集約点なので、そこで
        /// <c>MarkInputChangedSinceAnalysis</c> も走る。戻さないと、開いただけで
        /// 編集扱いになり、ファイルに記録した値が無意味になる。
        /// </summary>
        [TestMethod]
        public void TheLoadPathRestoresTheFlagAfterSavingTheInitialUndoState()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(ContourRenderingTests).Assembly.Location)!);
            string? root = null;
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                {
                    root = dir.FullName;
                    break;
                }
            }
            Assert.IsNotNull(root);

            string code = File.ReadAllText(
                Path.Combine(root!, "Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"));

            int captured = code.IndexOf("bool changedSinceAnalysisOnLoad = InputChangedSinceAnalysis;",
                System.StringComparison.Ordinal);
            // ファイル内には別の SaveUndoState もあるので、控えた行より後ろを探す
            int undo = captured < 0 ? -1
                : code.IndexOf("SaveUndoState();", captured, System.StringComparison.Ordinal);
            int restored = code.IndexOf("RestoreInputChangedSinceAnalysis(changedSinceAnalysisOnLoad);",
                System.StringComparison.Ordinal);

            Assert.IsTrue(captured >= 0, "読込前の値を控えていません");
            Assert.IsTrue(restored >= 0, "読込後に値を戻していません");
            Assert.IsTrue(captured < undo && undo < restored,
                "控える → SaveUndoState → 戻す の順になっていません");
        }
    }
}
