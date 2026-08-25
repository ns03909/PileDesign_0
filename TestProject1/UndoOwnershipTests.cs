using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common.Undo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// Undo スタックの持ち主が 1 つに定まっていること。
    ///
    /// 以前はプロセス全体で 1 本の静的インスタンス (<c>UndoService.Instance</c>) があり、
    /// メイン画面の DataGrid 編集がそこへ積まれる一方、消費するのは杭頭バネのウィンドウ
    /// (ChangWindow) だけだった。つまり
    /// <b>ChangWindow で Ctrl+Z を押すとメイン画面のセル編集が巻き戻る</b>。
    /// メイン画面側では積んだものが誰にも消費されず、杭・節点の削除は
    /// <b>Ctrl+Z で戻せないまま</b>だった。
    ///
    /// メイン画面の Undo は ViewModel のスナップショット履歴が持つ。
    /// ウィンドウ固有の Undo は、それを消費する ViewModel が 1 本ずつ持つ。
    /// </summary>
    [TestClass]
    public class UndoOwnershipTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(UndoOwnershipTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        /// <summary>
        /// 共有の静的 Undo スタックが復活していないこと。
        /// 「手軽に使える共有インスタンス」は、必ず持ち主が曖昧になる。
        /// </summary>
        [TestMethod]
        public void NoSharedStaticUndoStack()
        {
            var shared = typeof(UndoManager).Assembly.GetTypes()
                .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Where(f => f.FieldType == typeof(UndoManager))
                .Select(f => $"{f.DeclaringType?.Name}.{f.Name}")
                .ToList();

            Assert.AreEqual(0, shared.Count,
                "プロセス全体で共有される Undo スタックがある " +
                "(別のウィンドウの編集が巻き戻る):\n  " + string.Join("\n  ", shared));
        }

        /// <summary>
        /// メイン画面の code-behind が、ウィンドウ固有の Undo スタックに積んでいないこと。
        /// メイン画面の Undo は ViewModel のスナップショット履歴 (SaveUndoState) が持つ。
        /// </summary>
        [TestMethod]
        public void MainWindow_PushesOnlyToTheSnapshotHistory()
        {
            string viewsDir = Path.Combine(FindSolutionRoot(), "Graphics_r1", "Views");
            var violations = new List<string>();

            foreach (string file in Directory.EnumerateFiles(viewsDir, "MainWindow*.xaml.cs"))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripComment(lines[i]);
                    if (code.Contains("UndoStack.Push") || code.Contains("UndoService"))
                        violations.Add($"{Path.GetFileName(file)}:{i + 1}  {code.Trim()}");
                }
            }

            Assert.AreEqual(0, violations.Count,
                "メイン画面が別の Undo スタックに積んでいる " +
                "(メイン画面の Ctrl+Z では戻せず、他のウィンドウで戻ってしまう):\n  "
                + string.Join("\n  ", violations));
        }

        private static string StripComment(string line)
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? line[..i] : line;
        }
    }
}
