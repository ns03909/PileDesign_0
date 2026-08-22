using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 手順の説明が 1 つに揃っているかの検査。
    ///
    /// 同じアプリの手順がリボン・クイックヒント・ヘルプで 3 通りあり、
    /// 順序も呼称も食い違っていた。ヘルプの Step 1〜8 を正とする。
    /// </summary>
    [TestClass]
    public class WorkflowConsistencyTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(WorkflowConsistencyTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        /// <summary>
        /// リボンのツールチップが参照する Step 番号が、ヘルプに実在すること。
        /// ヘルプ側の Step を増減したときに、リボンだけ取り残されるのを防ぐ。
        /// </summary>
        [TestMethod]
        public void RibbonStepReferences_ExistInHelp()
        {
            string root = FindSolutionRoot();
            string help = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Help", "help.html"));
            string xaml = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Views", "MainWindow.xaml"));

            var referenced = Regex.Matches(xaml, @"ToolTip=""Step (\d+)")
                .Select(m => int.Parse(m.Groups[1].Value))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            Assert.IsTrue(referenced.Count >= 4,
                $"リボンから参照している Step が {referenced.Count} 件しかない (収集が壊れている可能性)");

            var missing = referenced
                .Where(n => !help.Contains($"Step {n}:", StringComparison.Ordinal))
                .ToList();

            Assert.AreEqual(0, missing.Count,
                "リボンが参照している Step がヘルプにありません: " + string.Join(", ", missing));
        }

        /// <summary>
        /// クイックヒントに旧番号 (①〜⑬) が残っていないこと。
        /// ヘルプの Step 番号と別体系の番号が並ぶと、どちらが手順なのか分からない。
        /// </summary>
        [TestMethod]
        public void QuickHints_DoNotUseTheirOwnNumbering()
        {
            string root = FindSolutionRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Views", "MainWindow.xaml"));

            var hits = Regex.Matches(xaml, @"QuickHintBubble HintText=""([^""]*)""")
                .Select(m => m.Groups[1].Value)
                .Where(t => t.Any(c => c >= '①' && c <= '⑳'))   // ①〜⑳
                .ToList();

            Assert.AreEqual(0, hits.Count,
                "クイックヒントに独自番号が残っています (ヘルプの Step に合わせてください):\n  "
                + string.Join("\n  ", hits));
        }

        /// <summary>
        /// ステータスバーが、まだ実行していない段も灰色で先に並べること。
        /// 完了した項目しか出ないと「次に何をすればよいか」が読み取れない。
        /// </summary>
        [TestMethod]
        public void StatusBar_ShowsPendingStepsNotOnlyCompletedOnes()
        {
            var vm = new MainWindowViewModel();   // 何も実行していない状態

            var items = vm.AnalysisStatusItems;
            var texts = items.Select(i => i.Text).ToList();

            CollectionAssert.Contains(texts, "杭要素分割", "未完了の杭要素分割が並んでいない");
            CollectionAssert.Contains(texts, "水平解析", "未完了の水平解析が並んでいない");

            Assert.IsTrue(items.Where(i => !i.Text.EndsWith(" ✓", StringComparison.Ordinal))
                               .All(i => i.Color == "Inactive"),
                "未完了の項目に完了扱いの色が付いている");

            StringAssert.Contains(vm.AnalysisStatusText, "まだ実行していません",
                "ツールチップに未完了の説明が無い");
        }
    }
}
