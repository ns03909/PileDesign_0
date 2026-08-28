using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの開き方が 1 つに揃っていること。
    ///
    /// ヘルプは<b>別 UI スレッドのモードレス</b>で開く。読みながら元の画面を触れるようにするため。
    /// <c>new HelpWindow(...)</c> を各画面に書くと開き方が分かれ、実際に
    /// 杭頭バネのウィンドウだけ <c>ShowDialog</c> になっていた。
    /// その画面の説明を読みたいのに、読んでいる間はその画面を操作できない状態だった。
    ///
    /// 開くときは <c>MainWindowViewModel.OpenHelpWindow(At)</c> を通すこと。
    /// </summary>
    [TestClass]
    public class HelpWindowOpeningTests
    {
        /// <summary>ヘルプウィンドウを直接 new してよい場所 (共通の開き口そのもの)。</summary>
        private static readonly string[] Allowed = ["MainWindowViewModel.ToolWindowsAndMoveCopy.cs"];

        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(HelpWindowOpeningTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        [TestMethod]
        public void HelpIsAlwaysOpenedThroughTheSharedEntryPoint()
        {
            string root = Path.Combine(FindSolutionRoot(), "Graphics_r1");
            var offenders = new List<string>();

            foreach (string cs in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (Array.Exists(Allowed, a => Path.GetFileName(cs) == a)) continue;

                var lines = File.ReadAllLines(cs);
                for (int i = 0; i < lines.Length; i++)
                {
                    int comment = lines[i].IndexOf("//", StringComparison.Ordinal);
                    string code = comment >= 0 ? lines[i][..comment] : lines[i];
                    if (Regex.IsMatch(code, @"new\s+HelpWindow\s*\("))
                        offenders.Add($"{Path.GetFileName(cs)}:{i + 1}  {code.Trim()}");
                }
            }

            Assert.AreEqual(0, offenders.Count,
                "ヘルプウィンドウを直接作っています "
                + "(MainWindowViewModel.OpenHelpWindow(At) を通してください):\n  "
                + string.Join("\n  ", offenders));
        }
    }
}
