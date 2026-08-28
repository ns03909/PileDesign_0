using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 使えるショートカットが一覧に載っていること。
    ///
    /// 一覧 (Ctrl + /) に無いショートカットは、使えることを知る手立てが無い。
    /// 計算書出力 (Ctrl+Shift+W) とコマンドパレット (Ctrl+Shift+P) が漏れていた。
    /// </summary>
    [TestClass]
    public class ShortcutListTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(ShortcutListTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        /// <summary>
        /// メイン画面の <c>InputBindings</c> にあるものが、すべて一覧に載っていること。
        /// </summary>
        [TestMethod]
        public void EveryKeyBindingAppearsInTheList()
        {
            string root = FindSolutionRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Views", "MainWindow.xaml"));
            string list = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Views", "ShortcutKeysWindow.xaml.cs"));

            // 一覧の表記は "F4 / Ctrl + D" のように 1 行に複数書くことがある
            var listed = Regex.Matches(list, @"new\(""[^""]*"",\s*""[^""]*"",\s*""(?<keys>[^""]*)""\)")
                .Select(m => m.Groups["keys"].Value)
                .SelectMany(k => k.Split('/'))
                .Select(k => k.Trim())
                .ToHashSet();

            var missing = new List<string>();

            foreach (Match m in Regex.Matches(xaml,
                         @"<KeyBinding\s+Key=""(?<key>[^""]+)""(?:\s+Modifiers=""(?<mod>[^""]+)"")?"))
            {
                string key = m.Groups["key"].Value;
                string mod = m.Groups["mod"].Success ? m.Groups["mod"].Value : "";

                // 一覧の表記は "Ctrl + Shift + S" のように空白入り
                string expected = string.Join(" + ",
                    mod.Split('+', StringSplitOptions.RemoveEmptyEntries)
                       .Select(x => x.Trim() == "Control" ? "Ctrl" : x.Trim())
                       .Append(key));

                if (!listed.Contains(expected))
                    missing.Add(expected);
            }

            Assert.AreEqual(0, missing.Count,
                "ショートカット一覧に載っていないキー割当があります:\n  " + string.Join("\n  ", missing));
        }
    }
}
