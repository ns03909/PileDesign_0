using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// グラフが杭の先頭の要素 (beams[0]) を読む前に、杭が解析の要素とつながっているかを確かめていること (2026-11-04)。
    ///
    /// <para>解析結果はあるのに杭の要素 (Beams) が空のとき、杭応力の深さ分布が beams[0] で範囲外の例外を出して止まった
    /// (Visual Studio のデバッグ実行で利用者が遭遇)。原因の、控えを取り直すとつながりが空になる不具合は
    /// ResultSetRecaptureTests で押さえた。ほかの道で崩れても止まらないよう読む側でも確かめ、
    /// 省いた杭はログに残す (GraphViewModel.HasAnalysisElements)。</para>
    /// </summary>
    [TestClass]
    public class GraphPileElementGuardTests
    {
        [TestMethod]
        public void FirstElementIsReadOnlyAfterTheGuard()
        {
            var declare = new Regex(@"var\s+beams\s*=\s*\w+\.Beams\s*;");
            var firstElement = new Regex(@"\bbeams\[0\]");
            int scanned = 0;
            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1", "ViewModels"), "GraphViewModel*.cs"))
            {
                var lines = File.ReadAllLines(file);
                int lastDeclare = -1;
                bool guarded = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = lines[i].Split("//")[0];
                    if (declare.IsMatch(code)) { lastDeclare = i; guarded = false; }
                    if (lastDeclare >= 0 && code.Contains("HasAnalysisElements(")) guarded = true;
                    if (!firstElement.IsMatch(code)) continue;
                    scanned++;
                    if (!guarded) offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
            TestSource.AssertScanned(scanned, 2, "グラフで杭の先頭の要素を読む所");
            Assert.AreEqual(0, offenders.Count,
                "杭の要素が空かを確かめずに beams[0] を読んでいます (HasAnalysisElements で確かめる): " + string.Join(", ", offenders));
        }
    }
}
